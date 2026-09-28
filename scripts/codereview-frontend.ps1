$ErrorActionPreference = "Continue"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$frontend = Join-Path $root "frontend"
$src = Join-Path $frontend "src"
$report = Join-Path $root "docs/codereview/reports/FRONTEND_REPORT.md"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $report) | Out-Null

function Test-NoMatch([object[]]$Files, [string]$Pattern, [string]$AllowMarker = "") {
    $hits = $Files | Select-String -Pattern $Pattern
    if ($AllowMarker) {
        $hits = $hits | Where-Object {
            $lines = Get-Content -LiteralPath $_.Path
            $previous = if ($_.LineNumber -gt 1) { $lines[$_.LineNumber - 2] } else { "" }
            -not ($_.Line -match [regex]::Escape($AllowMarker) -or $previous -match [regex]::Escape($AllowMarker))
        }
    }
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

$appFiles = Get-ChildItem -Path $src -Recurse -File -Include "*.ts", "*.html" | Where-Object { $_.Name -notlike "*.spec.ts" }
$appTsFiles = $appFiles | Where-Object Extension -eq ".ts"
$servicesAndComponents = $appTsFiles | Where-Object { $_.FullName -notmatch '[\\/]environments[\\/]' }
$envFiles = Get-ChildItem -Path (Join-Path $src "environments") -File -Filter "*.ts"
$prodEnv = Get-Item -LiteralPath (Join-Path $src "environments/environment.ts")

$checks = @(
    @{ Id = "FE-02"; Severity = "Gate"; Name = "ESLint"; Test = { Invoke-Native { npm --prefix $frontend run lint } } },
    @{ Id = "FE-03"; Severity = "Gate"; Name = "Prettier"; Test = { Invoke-Native { npm --prefix $frontend run format:check } } },
    @{ Id = "FE-04"; Severity = "Gate"; Name = "Unit tests"; Test = { Invoke-Native { npm --prefix $frontend run test:ci } } },
    @{ Id = "FE-05"; Severity = "Gate"; Name = "Production build"; Test = { Invoke-Native { npm --prefix $frontend run build -- --configuration production } } },
    @{ Id = "FE-11"; Severity = "Gate"; Name = "No hardcoded API URLs outside environment files"; Test = { Test-NoMatch $servicesAndComponents 'https?://(localhost|127\.0\.0\.1|[a-z0-9.-]+\.(com|net|io|vn|app))' } },
    @{ Id = "FE-15"; Severity = "Gate"; Name = "No console.log in application code"; Test = { Test-NoMatch $appTsFiles 'console\.log\(' } },
    @{ Id = "SEC-FE-C01"; Severity = "Critical"; Name = "No secrets or private keys in frontend source"; Test = { Test-NoMatch ($appFiles + $envFiles) '(?i)(-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk_live_[0-9a-zA-Z]{16,}|(secret|password|api[_-]?key|client[_-]?secret)\s*[:=]\s*[''"][^''"_]{6,}[''"])' } },
    @{ Id = "SEC-FE-C02"; Severity = "Critical"; Name = "No unreviewed DomSanitizer bypass"; Test = { Test-NoMatch $appTsFiles 'bypassSecurityTrust(Html|Url|ResourceUrl|Script|Style)' 'security-reviewed:' } },
    @{ Id = "SEC-FE-C03"; Severity = "Critical"; Name = "No direct DOM injection or dynamic code execution"; Test = { Test-NoMatch $appTsFiles '\.(innerHTML|outerHTML)\s*=|document\.write\(|insertAdjacentHTML\(|\beval\(|new Function\(' } },
    @{ Id = "SEC-FE-H01"; Severity = "High"; Name = "No High/Critical vulnerabilities in runtime dependencies"; Test = { Invoke-Native { npm --prefix $frontend audit --omit=dev --audit-level=high } } },
    @{ Id = "SEC-FE-H06"; Severity = "High"; Name = "Production API URL is not plain HTTP"; Test = { Test-NoMatch @($prodEnv) 'apiUrl\s*:\s*[''"]http://' } }
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

$lines = @("# Frontend Review Report", "", "Generated: $(Get-Date -Format o)", "")
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
$lines += "Manual Critical/High/Medium/Low security items: see ``docs/codereview/CODEREVIEW_FRONTEND.md``."
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
