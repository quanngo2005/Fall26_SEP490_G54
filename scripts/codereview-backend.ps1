$ErrorActionPreference = "Continue"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$backend = Join-Path $root "backend"
$solution = Join-Path $backend "G54.sln"
$report = Join-Path $root "docs/codereview/reports/BACKEND_REPORT.md"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $report) | Out-Null

function Get-SourceFiles([string[]]$Include, [switch]$SkipMigrations) {
    Get-ChildItem -Path (Join-Path $backend "src") -Recurse -File -Include $Include |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Where-Object { -not $SkipMigrations -or $_.FullName -notmatch '[\\/]Migrations[\\/]' }
}

function Test-NoMatch([object[]]$Files, [string]$Pattern) {
    $hits = $Files | Select-String -Pattern $Pattern
    foreach ($hit in $hits) {
        $relative = $hit.Path.Substring($root.Length + 1)
        Write-Host "  -> ${relative}:$($hit.LineNumber): $($hit.Line.Trim())"
        $script:findings += "  - ``${relative}:$($hit.LineNumber)`` $($hit.Line.Trim())"
    }
    return (@($hits).Count -eq 0)
}

function Invoke-Native([scriptblock]$Command) {
    & $Command | Out-Host
    return ($LASTEXITCODE -eq 0)
}

$csFiles = Get-SourceFiles -Include "*.cs" -SkipMigrations
$controllerFiles = $csFiles | Where-Object { $_.FullName -match '[\\/]Controllers[\\/]' }
$productionSettings = Get-SourceFiles -Include "appsettings.Production*.json"

$checks = @(
    @{ Id = "BE-01"; Severity = "Gate"; Name = "Restore"; Test = { Invoke-Native { dotnet restore $solution } } },
    @{ Id = "BE-02"; Severity = "Gate"; Name = "Release build (warnings as errors)"; Test = { Invoke-Native { dotnet build $solution -c Release --no-restore } } },
    @{ Id = "BE-03"; Severity = "Gate"; Name = "Unit tests"; Test = { Invoke-Native { dotnet test $solution -c Release --no-build } } },
    @{ Id = "BE-04"; Severity = "Gate"; Name = "Formatting"; Test = { Invoke-Native { dotnet format $solution --verify-no-changes --no-restore } } },
    @{ Id = "BE-06"; Severity = "Gate"; Name = "Controllers do not access DAL/AppDbContext"; Test = { Test-NoMatch $controllerFiles 'using G54\.DAL|AppDbContext' } },
    @{ Id = "SEC-BE-C01"; Severity = "Critical"; Name = "No hardcoded private keys, cloud keys, or production credentials"; Test = {
        $a = Test-NoMatch $csFiles '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk_live_[0-9a-zA-Z]{16,}'
        $b = Test-NoMatch $productionSettings '(?i)(password|pwd)\s*=\s*[^;"\s]+|"Key"\s*:\s*"[^"_]{16,}"'
        $a -and $b } },
    @{ Id = "SEC-BE-C02"; Severity = "Critical"; Name = "No raw SQL built from interpolation or concatenation"; Test = { Test-NoMatch $csFiles '(FromSqlRaw|ExecuteSqlRaw)(Async)?\s*\(\s*(\$"|[^)]*"\s*\+)' } },
    @{ Id = "SEC-BE-C05"; Severity = "Critical"; Name = "JWT validation flags are never disabled"; Test = { Test-NoMatch $csFiles '(ValidateIssuerSigningKey|ValidateLifetime|ValidateIssuer|ValidateAudience)\s*=\s*false' } },
    @{ Id = "SEC-BE-C07"; Severity = "Critical"; Name = "No insecure deserialization"; Test = { Test-NoMatch $csFiles 'BinaryFormatter|TypeNameHandling\.(All|Auto|Objects)' } },
    @{ Id = "SEC-BE-H01"; Severity = "High"; Name = "No High/Critical vulnerable NuGet packages"; Test = {
        $output = dotnet list $solution package --vulnerable --include-transitive 2>&1 | Out-String
        $exit = $LASTEXITCODE
        Write-Host $output
        $global:LASTEXITCODE = $exit
        if ($LASTEXITCODE -ne 0) { $script:findings += "  - dotnet list package --vulnerable failed"; return $false }
        $bad = $output -split "`n" | Where-Object { $_ -match '\b(High|Critical)\b' }
        foreach ($line in $bad) { $script:findings += "  - $($line.Trim())" }
        return (@($bad).Count -eq 0) } },
    @{ Id = "SEC-BE-H05"; Severity = "High"; Name = "CORS never allows any origin with credentials"; Test = {
        $bad = $csFiles | Where-Object { (Select-String -Path $_.FullName -Pattern 'AllowAnyOrigin\s*\(' -Quiet) -and (Select-String -Path $_.FullName -Pattern 'AllowCredentials\s*\(' -Quiet) }
        foreach ($file in $bad) { $script:findings += "  - ``$($file.FullName.Substring($root.Length + 1))`` combines AllowAnyOrigin and AllowCredentials" }
        return (@($bad).Count -eq 0) } }
)

$results = @()
foreach ($check in $checks) {
    $script:findings = @()
    Write-Host "==> $($check.Id) $($check.Name)"
    $passed = [bool](& $check.Test | Select-Object -Last 1)
    $results += [pscustomobject]@{ Id = $check.Id; Severity = $check.Severity; Name = $check.Name; Passed = $passed; Findings = $script:findings }
}

$failed = @($results | Where-Object { -not $_.Passed })
$blocking = @($failed | Where-Object { $_.Severity -in @("Gate", "Critical", "High") })

$lines = @("# Backend Review Report", "", "Generated: $(Get-Date -Format o)", "")
foreach ($group in @("Gate", "Critical", "High", "Medium", "Low")) {
    $items = @($results | Where-Object Severity -eq $group)
    if ($items.Count -eq 0) { continue }
    $title = if ($group -eq "Gate") { "Quality Gates" } else { "Security - $group" }
    $lines += "## $title"
    $lines += ""
    foreach ($item in $items) {
        $mark = if ($item.Passed) { "x" } else { " " }
        $lines += "- [$mark] $($item.Id) $($item.Name)"
        $lines += $item.Findings
    }
    $lines += ""
}
$lines += "## Summary"
$lines += ""
$lines += "| Severity | Passed | Failed |"
$lines += "| --- | --- | --- |"
foreach ($group in @("Gate", "Critical", "High")) {
    $items = @($results | Where-Object Severity -eq $group)
    $lines += "| $group | $(@($items | Where-Object Passed).Count) | $(@($items | Where-Object { -not $_.Passed }).Count) |"
}
$lines += ""
$lines += "Manual Critical/High/Medium/Low security items: see ``docs/codereview/CODEREVIEW_BACKEND.md``."
$lines += ""
$lines += if ($blocking.Count -gt 0) { "## CHECKS FAILED" } else { "## ALL CHECKS PASSED" }
$lines | Set-Content -Path $report -Encoding utf8

Write-Host ""
Write-Host "Report: $report"
if ($blocking.Count -gt 0) {
    Write-Host "FAILED: $($blocking.Count) blocking check(s)."
    exit 1
}
Write-Host "ALL CHECKS PASSED"
