<#
.SYNOPSIS
Shared engine for codereview-backend.ps1 and codereview-frontend.ps1.
.DESCRIPTION
A check is @{ Id; Severity; Name; Test = { ... } }. The Test block returns $true (clean) or $false and records where the
problem is with Add-Finding. Every failure ends up in the report with file:line locations: when a check fails without
recording a finding, the error lines of the last command it ran are copied in, and the full output is kept in
docs/codereview/reports/logs/<Id>.log.

Severities Gate/Critical/High block (FAIL); Medium/Low are advisory (WARN). A check that cannot run calls Skip-Check and is
reported as SKIP, never as passed.
#>

$script:Root = $null
$script:LogDir = $null
$script:Current = $null
$script:Metrics = [ordered]@{}
$script:MaxFindingsInReport = 40
$script:BlockingSeverities = @("Gate", "Critical", "High")

function Initialize-Review([string]$Root) {
    $script:Root = $Root
    $script:LogDir = Join-Path $Root "docs/codereview/reports/logs"
    New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null
    Get-ChildItem -Path $script:LogDir -File -ErrorAction SilentlyContinue | Remove-Item -Force
    $script:Metrics = [ordered]@{}
    try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }
}

function ConvertTo-RepoPath([string]$Path) {
    if (-not $Path) { return $Path }
    $normalized = $Path.Trim().Replace('\', '/')
    $rootNormalized = $script:Root.Replace('\', '/').TrimEnd('/') + '/'
    if ($normalized.StartsWith($rootNormalized, [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring($rootNormalized.Length)
    }
    return $normalized
}

<# Records a problem. Location is optional (e.g. a failing package has none). #>
function Add-Finding([string]$Message, [string]$File = "", [int]$Line = 0) {
    $text = (Remove-AnsiEscapes $Message).Trim()
    if ($File) {
        $location = ConvertTo-RepoPath $File
        if ($Line -gt 0) { $location = "${location}:$Line" }
        $text = "``$location`` $text"
    }
    if (-not $script:Current.Findings.Contains($text)) { [void]$script:Current.Findings.Add($text) }
}

function Skip-Check([string]$Reason) {
    $script:Current.SkipReason = $Reason
    return $true
}

function Add-Metric([string]$Name, [string]$Value) {
    $script:Metrics[$Name] = Remove-AnsiEscapes $Value
}

<# Tools such as Karma/Angular colour their output with ANSI escapes (ESC[32m ...); they are invisible in a terminal but
show up as stray "[32m" text in the logs and reports, so they are stripped from everything that is stored. #>
function Remove-AnsiEscapes([string]$Text) {
    return [regex]::Replace($Text, "\x1B\[[0-9;?]*[ -/]*[@-~]", "")
}

<#
Runs a native command, streams its output, saves it to logs/<Id>.log and returns @{ ExitCode; Lines }. The lines are also kept
so a failing check without findings can show the relevant part of the output.
#>
function Invoke-Logged([scriptblock]$Command, [string]$WorkingDirectory = "") {
    if ($WorkingDirectory) { Push-Location $WorkingDirectory }
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Command *>&1 | ForEach-Object { "$_" } | Tee-Object -Variable streamed | Out-Host; $streamed)
        $exit = $global:LASTEXITCODE
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
    }
    $lines = @($lines | Where-Object { $null -ne $_ } | ForEach-Object { Remove-AnsiEscapes $_ })
    $log = Join-Path $script:LogDir "$($script:Current.Id).log"
    if ($lines.Count -gt 0) { Add-Content -LiteralPath $log -Value $lines -Encoding utf8 }
    $script:Current.LastOutput = $lines
    return @{ ExitCode = $exit; Lines = $lines }
}

<# Adds one finding per regex hit, optionally ignoring hits marked with $AllowMarker on the same or previous line. #>
function Find-Pattern([object[]]$Files, [string]$Pattern, [string]$Message = "", [string]$AllowMarker = "") {
    $hits = @($Files | Where-Object { $_ } | Select-String -Pattern $Pattern)
    if ($AllowMarker) {
        $hits = @($hits | Where-Object {
                $lines = Get-Content -LiteralPath $_.Path
                $previous = if ($_.LineNumber -gt 1) { $lines[$_.LineNumber - 2] } else { "" }
                -not ($_.Line -match [regex]::Escape($AllowMarker) -or $previous -match [regex]::Escape($AllowMarker))
            })
    }
    foreach ($hit in $hits) {
        $text = if ($Message) { "$Message - $($hit.Line.Trim())" } else { $hit.Line.Trim() }
        Add-Finding $text $hit.Path $hit.LineNumber
    }
    return ($hits.Count -eq 0)
}

<# Like Find-Pattern but matches across lines (e.g. an empty catch block written over several lines). #>
function Find-PatternInContent([object[]]$Files, [string]$Pattern, [string]$Message, [scriptblock]$Accept = { $true }) {
    $clean = $true
    foreach ($file in @($Files | Where-Object { $_ })) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if (-not $content) { continue }
        foreach ($match in [regex]::Matches($content, $Pattern)) {
            if (-not (& $Accept $match)) { continue }
            $line = ([regex]::Matches($content.Substring(0, $match.Index), "`n")).Count + 1
            $snippet = ($match.Value -replace '\s+', ' ').Trim()
            if ($snippet.Length -gt 120) { $snippet = $snippet.Substring(0, 120) + "..." }
            Add-Finding "$Message - $snippet" $file.FullName $line
            $clean = $false
        }
    }
    return $clean
}

<# MSBuild / dotnet format / Roslyn diagnostics: "path(line,col): error CODE: message [project]". #>
function Add-MsBuildDiagnostics([string[]]$Lines, [switch]$IncludeWarnings, [switch]$GroupByFile) {
    $pattern = '^\s*(?<file>[^\s(][^(]*?)\((?<line>\d+)(,\d+)?\)\s*:\s*(?<kind>error|warning)\s+(?<code>[A-Za-z]+\d*)\s*:\s*(?<msg>.*?)(\s+\[[^\]]+\])?\s*$'
    $plain = '^\s*(?<where>[^:]*?)\s*:\s*(?<kind>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*?)(\s+\[[^\]]+\])?\s*$'
    $perFile = [ordered]@{}
    foreach ($line in $Lines) {
        $m = [regex]::Match($line, $pattern)
        if ($m.Success) {
            if ($m.Groups['kind'].Value -eq 'warning' -and -not $IncludeWarnings) { continue }
            if ($GroupByFile) {
                $key = $m.Groups['file'].Value
                if (-not $perFile.Contains($key)) { $perFile[$key] = @{ Line = [int]$m.Groups['line'].Value; Codes = New-Object System.Collections.Generic.HashSet[string]; Issues = 0 } }
                $perFile[$key].Issues++
                [void]$perFile[$key].Codes.Add($m.Groups['code'].Value)
                continue
            }
            Add-Finding "$($m.Groups['kind'].Value) $($m.Groups['code'].Value): $($m.Groups['msg'].Value)" $m.Groups['file'].Value ([int]$m.Groups['line'].Value)
            continue
        }
        $p = [regex]::Match($line, $plain)
        if ($p.Success -and ($p.Groups['kind'].Value -eq 'error' -or $IncludeWarnings)) {
            $where = ConvertTo-RepoPath $p.Groups['where'].Value
            Add-Finding "$($p.Groups['kind'].Value) $($p.Groups['code'].Value): $($p.Groups['msg'].Value) ($where)"
        }
    }
    foreach ($file in $perFile.Keys) {
        $entry = $perFile[$file]
        Add-Finding "$($entry.Issues) formatting issue(s) ($([string]::Join(', ', $entry.Codes))), first at this line" $file $entry.Line
    }
}

<# Fallback for failures that recorded no finding: the error-looking lines of the last command, or its tail. #>
function Add-OutputExcerpt([string[]]$Lines) {
    $interesting = @($Lines | Where-Object { $_ -match '(?i)\berror\b|\bfail(ed|ure)?\b|exception|\[ERROR\]|\u2718|not found|denied|blocked' } | Select-Object -Unique)
    $excerpt = if ($interesting.Count -gt 0) { $interesting | Select-Object -First 15 } else { $Lines | Where-Object { $_.Trim() } | Select-Object -Last 15 }
    foreach ($line in $excerpt) { Add-Finding "output: $($line.Trim())" }
    if (@($excerpt).Count -eq 0) { Add-Finding "the command failed without output" }
}

function Get-GitContext {
    Push-Location $script:Root
    try {
        $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
        $commit = (git rev-parse --short HEAD 2>$null)
        $dirty = @(git status --porcelain 2>$null).Count
    }
    finally { Pop-Location }
    if (-not $commit) { return "not a git checkout" }
    $state = if ($dirty -gt 0) { "$dirty uncommitted file(s) included" } else { "clean working tree" }
    return "$branch @ $commit ($state)"
}

function Format-Duration([TimeSpan]$Span) {
    if ($Span.TotalMinutes -ge 1) { return "{0}m{1:00}s" -f [int][math]::Floor($Span.TotalMinutes), $Span.Seconds }
    return "{0:0.0}s" -f $Span.TotalSeconds
}

<# Runs the checks, writes the report and returns the process exit code (1 when a blocking check failed). #>
function Invoke-Review([string]$Title, [object[]]$Checks, [string]$ReportPath) {
    $started = Get-Date
    $results = @()
    foreach ($check in $Checks) {
        $script:Current = [pscustomobject]@{
            Id = $check.Id; Severity = $check.Severity; Name = $check.Name
            Findings = New-Object System.Collections.Generic.List[string]
            SkipReason = $null; LastOutput = @(); Status = $null; Duration = $null
        }
        Write-Host ""
        Write-Host "==> $($check.Id) [$($check.Severity)] $($check.Name)"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            $passed = [bool](& $check.Test | Select-Object -Last 1)
        }
        catch {
            $passed = $false
            Add-Finding "the check itself crashed: $($_.Exception.Message)"
        }
        $clock.Stop()
        $script:Current.Duration = $clock.Elapsed

        $blocking = $script:BlockingSeverities -contains $check.Severity
        if ($script:Current.SkipReason) { $status = "SKIP" }
        elseif ($passed) { $status = "PASS" }
        elseif ($blocking) { $status = "FAIL" }
        else { $status = "WARN" }

        if ($status -in @("FAIL", "WARN") -and $script:Current.Findings.Count -eq 0) { Add-OutputExcerpt $script:Current.LastOutput }
        $script:Current.Status = $status
        $results += $script:Current

        $summary = if ($status -eq "SKIP") { "SKIP - $($script:Current.SkipReason)" } else { $status }
        Write-Host "<== $($check.Id) $summary ($(Format-Duration $clock.Elapsed))" -ForegroundColor $(switch ($status) { "PASS" { "Green" } "FAIL" { "Red" } default { "Yellow" } })
        foreach ($finding in $script:Current.Findings | Select-Object -First 10) { Write-Host "    $finding" }
    }

    $failed = @($results | Where-Object Status -eq "FAIL")
    $warned = @($results | Where-Object Status -eq "WARN")
    $skipped = @($results | Where-Object Status -eq "SKIP")
    $verdict = if ($failed.Count -gt 0) { "FAILED" } else { "PASSED" }

    $lines = @("# $Title", "")
    $lines += "**Result: $verdict** - $($failed.Count) blocking failure(s), $($warned.Count) warning(s), $($skipped.Count) skipped, $(@($results | Where-Object Status -eq 'PASS').Count) passed."
    $lines += ""
    $lines += "- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') in $(Format-Duration ((Get-Date) - $started))"
    $lines += "- Code reviewed: $(Get-GitContext)"
    $lines += "- Full output of every check: ``docs/codereview/reports/logs/<ID>.log``"
    $lines += ""

    $sections = @(
        @{ Title = "Blocking failures (must fix before merge)"; Items = $failed },
        @{ Title = "Warnings (advisory, merge allowed with a tracked issue)"; Items = $warned },
        @{ Title = "Skipped (NOT verified - run where the tool is available)"; Items = $skipped }
    )
    foreach ($section in $sections) {
        if ($section.Items.Count -eq 0) { continue }
        $lines += "## $($section.Title)"
        $lines += ""
        foreach ($item in $section.Items) {
            $lines += "### $($item.Status) $($item.Id) $($item.Name) [$($item.Severity)]"
            $lines += ""
            if ($item.Status -eq "SKIP") { $lines += "- Reason: $($item.SkipReason)" }
            foreach ($finding in $item.Findings | Select-Object -First $script:MaxFindingsInReport) { $lines += "- $finding" }
            if ($item.Findings.Count -gt $script:MaxFindingsInReport) {
                $lines += "- ... and $($item.Findings.Count - $script:MaxFindingsInReport) more, see ``docs/codereview/reports/logs/$($item.Id).log``"
            }
            $lines += ""
        }
    }

    if ($script:Metrics.Count -gt 0) {
        $lines += "## Metrics"
        $lines += ""
        foreach ($name in $script:Metrics.Keys) { $lines += "- ${name}: $($script:Metrics[$name])" }
        $lines += ""
    }

    $lines += "## All automated checks"
    $lines += ""
    $lines += "| Result | ID | Severity | Check | Findings | Time |"
    $lines += "| --- | --- | --- | --- | --- | --- |"
    foreach ($item in $results) {
        $lines += "| $($item.Status) | $($item.Id) | $($item.Severity) | $($item.Name) | $($item.Findings.Count) | $(Format-Duration $item.Duration) |"
    }
    $lines += ""

    $lines += "## $verdict"
    $lines | Set-Content -Path $ReportPath -Encoding utf8

    Write-Host ""
    Write-Host "Report: $ReportPath"
    Write-Host "$verdict - $($failed.Count) blocking, $($warned.Count) warning(s), $($skipped.Count) skipped"
    foreach ($item in $failed) { Write-Host "  FAIL $($item.Id) $($item.Name)" -ForegroundColor Red }
    if ($failed.Count -gt 0) { return 1 }
    return 0
}

Export-ModuleMember -Function Initialize-Review, ConvertTo-RepoPath, Add-Finding, Skip-Check, Add-Metric, Invoke-Logged, Find-Pattern, Find-PatternInContent, Add-MsBuildDiagnostics, Add-OutputExcerpt, Invoke-Review
