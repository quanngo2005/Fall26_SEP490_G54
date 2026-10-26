$ErrorActionPreference = "Continue"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Import-Module (Join-Path $PSScriptRoot "codereview-common.psm1") -Force -DisableNameChecking
Initialize-Review $root

$frontend = Join-Path $root "frontend"
$src = Join-Path $frontend "src"
$report = Join-Path $root "docs/codereview/reports/FRONTEND_REPORT.md"
$work = Join-Path ([IO.Path]::GetTempPath()) "g54-review-fe-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $work | Out-Null

$appFiles = Get-ChildItem -Path $src -Recurse -File -Include "*.ts", "*.html" | Where-Object { $_.Name -notlike "*.spec.ts" }
$appTsFiles = $appFiles | Where-Object Extension -eq ".ts"
$htmlFiles = $appFiles | Where-Object Extension -eq ".html"
$specFiles = Get-ChildItem -Path $src -Recurse -File -Filter "*.spec.ts"
$servicesAndComponents = $appTsFiles | Where-Object { $_.FullName -notmatch '[\\/]environments[\\/]' }
$envFiles = Get-ChildItem -Path (Join-Path $src "environments") -File -Filter "*.ts"
$prodEnv = Get-Item -LiteralPath (Join-Path $src "environments/environment.ts")
$script:eslint = $null
$script:buildLines = @()

<# Angular/esbuild and tsc errors: "[ERROR] TS2322: msg" followed by "src/x.ts:12:4:", or "src/x.ts:12:4 - error TS2322: msg". #>
function Add-AngularErrors([string[]]$Lines, [string]$Kind = "ERROR") {
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $inline = [regex]::Match($Lines[$i], '(?<file>src/[\w./@-]+\.\w+):(?<line>\d+):\d+ - (error|warning) (?<code>\w+): (?<msg>.+)$')
        if ($inline.Success -and $Kind -eq "ERROR") {
            Add-Finding "$($inline.Groups['code'].Value): $($inline.Groups['msg'].Value)" "frontend/$($inline.Groups['file'].Value)" ([int]$inline.Groups['line'].Value)
            continue
        }
        $header = [regex]::Match($Lines[$i], "\[$Kind\]\s*(?<msg>.+?)(\s+\[plugin [^\]]+\])?\s*$")
        if (-not $header.Success) { continue }
        $message = $header.Groups['msg'].Value
        $located = $false
        for ($j = $i + 1; $j -lt [math]::Min($i + 8, $Lines.Count); $j++) {
            $where = [regex]::Match($Lines[$j], '^\s*(?<file>[\w./@-]+\.\w+):(?<line>\d+):\d+:?\s*$')
            if ($where.Success) {
                Add-Finding $message "frontend/$($where.Groups['file'].Value)" ([int]$where.Groups['line'].Value)
                $located = $true
                break
            }
        }
        if (-not $located) { Add-Finding $message }
    }
}

<# Karma: "<browser> Suite should x FAILED" followed by the error and a stack frame pointing into src/. #>
function Add-KarmaFailures([string[]]$Lines) {
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $failed = [regex]::Match($Lines[$i], '^(?:.*?\(.*?\)\s+)?(?<name>.+?)\s+FAILED\s*$')
        if (-not $failed.Success -or $Lines[$i] -match 'Executed \d+ of') { continue }
        $message = ""
        $file = ""
        $lineNumber = 0
        for ($j = $i + 1; $j -lt [math]::Min($i + 25, $Lines.Count); $j++) {
            $text = $Lines[$j].Trim()
            if (-not $text) { continue }
            if ($Lines[$j] -notmatch '^\s' -and $j -gt $i + 1) { break }
            if (-not $message -and $text -notmatch '^at ') { $message = $text }
            $frame = [regex]::Match($text, '(?<file>src/[\w./@-]+\.(ts|html)):(?<line>\d+):\d+')
            if (-not $file -and $frame.Success) { $file = "frontend/$($frame.Groups['file'].Value)"; $lineNumber = [int]$frame.Groups['line'].Value }
        }
        if ($message.Length -gt 300) { $message = $message.Substring(0, 300) + "..." }
        Add-Finding "$($failed.Groups['name'].Value): $message" $file $lineNumber
    }
}

function Get-TranslationKeys($Node, [string]$Prefix = "") {
    $keys = [ordered]@{}
    foreach ($property in $Node.PSObject.Properties) {
        $path = if ($Prefix) { "$Prefix.$($property.Name)" } else { $property.Name }
        if ($property.Value -is [System.Management.Automation.PSCustomObject]) {
            $nested = Get-TranslationKeys $property.Value $path
            foreach ($key in $nested.Keys) { $keys[$key] = $nested[$key] }
        }
        else { $keys[$path] = $property.Value }
    }
    return $keys
}

$checks = @(
    @{ Id = "FE-01"; Severity = "Gate"; Name = "Installed dependencies match package-lock.json"; Test = {
            $run = Invoke-Logged { npm ls --depth=0 } $frontend
            foreach ($line in $run.Lines | Where-Object { $_ -match '(?i)\b(missing|invalid|extraneous|UNMET)\b' }) { Add-Finding "$($line.Trim()) - run npm ci in frontend/" }
            $run.ExitCode -eq 0 } },
    @{ Id = "FE-02"; Severity = "Gate"; Name = "ESLint (errors)"; Test = {
            $json = Join-Path $work "eslint.json"
            $run = Invoke-Logged { npx --no-install eslint . --format json --output-file $json } $frontend
            if (-not (Test-Path -LiteralPath $json)) { Add-Finding "ESLint did not produce a report (configuration error?)"; return $false }
            $parsed = Get-Content -LiteralPath $json -Raw -Encoding utf8 | ConvertFrom-Json
            $script:eslint = @($parsed | ForEach-Object { $_ })
            foreach ($file in $script:eslint) {
                foreach ($message in @($file.messages | Where-Object { $_.severity -eq 2 })) {
                    Add-Finding "$($message.ruleId): $($message.message)" $file.filePath ([int]$message.line)
                }
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "FE-03"; Severity = "Gate"; Name = "Prettier"; Test = {
            $run = Invoke-Logged { npx --no-install prettier --list-different . } $frontend
            foreach ($line in $run.Lines | Where-Object { $_.Trim() }) {
                if ($line -match '^\[error\]\s*(?<msg>.+)$') { Add-Finding "prettier error: $($Matches['msg'])" }
                elseif ($line -notmatch '^\[') { Add-Finding "not formatted - run npm run format" "frontend/$($line.Trim())" }
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "FE-04"; Severity = "Gate"; Name = "Unit tests (headless, with coverage)"; Test = {
            $run = Invoke-Logged { node scripts/test-ci.js --code-coverage } $frontend
            Add-AngularErrors $run.Lines
            Add-KarmaFailures $run.Lines
            if (($run.Lines -join "`n") -match 'No binary for|Cannot start (Chrome|Edge)|CHROME_BIN') {
                Add-Finding "no headless browser found: install Chrome/Edge or set CHROME_BIN"
            }
            $total = $run.Lines | Where-Object { $_ -match 'Executed \d+ of \d+' } | Select-Object -Last 1
            if ($total -match 'Executed (?<done>\d+) of (?<all>\d+)(?<rest>.*)') {
                $outcome = ($Matches['rest'] -replace '\(\d+(\.\d+)? secs.*$', '').TrimEnd()
                Add-Metric "Frontend tests" ("executed {0} of {1}{2}" -f $Matches['done'], $Matches['all'], $outcome)
            }
            foreach ($line in $run.Lines) {
                if ($line -match '^(?<kind>Statements|Branches|Functions|Lines)\s*:\s*(?<pct>[\d.]+%)\s*(?<detail>\(.*\))?') {
                    Add-Metric "Frontend coverage $($Matches['kind'])" "$($Matches['pct']) $($Matches['detail'])"
                }
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "FE-05"; Severity = "Gate"; Name = "Production build (within budgets)"; Test = {
            $run = Invoke-Logged { npm run build -- --configuration production } $frontend
            $script:buildLines = $run.Lines
            Add-AngularErrors $run.Lines
            $run.ExitCode -eq 0 } },
    @{ Id = "FE-11"; Severity = "Gate"; Name = "No hardcoded API URLs outside environment files"; Test = { Find-Pattern $servicesAndComponents 'https?://(localhost|127\.0\.0\.1|[a-z0-9.-]+\.(com|net|io|vn|app))' "hardcoded URL, use environment.apiUrl" } },
    @{ Id = "FE-15"; Severity = "Gate"; Name = "No console.log/console.debug/debugger in application code"; Test = { Find-Pattern $appTsFiles 'console\.(log|debug)\(|\bdebugger\s*;' "debug statement" } },
    @{ Id = "FE-22"; Severity = "Gate"; Name = "i18n: vi and en have the same keys and no empty text"; Test = {
            $file = Join-Path $src "app/core/i18n/localization.json"
            $translations = (Get-Content -LiteralPath $file -Raw -Encoding utf8 | ConvertFrom-Json).translations
            $locales = @($translations.PSObject.Properties.Name)
            $keys = @{}
            foreach ($locale in $locales) { $keys[$locale] = Get-TranslationKeys $translations.$locale }
            $clean = $true
            foreach ($locale in $locales) {
                foreach ($other in $locales | Where-Object { $_ -ne $locale }) {
                    foreach ($key in $keys[$locale].Keys | Where-Object { -not $keys[$other].Contains($_) }) {
                        Add-Finding "key '$key' exists in '$locale' but is missing in '$other'" $file; $clean = $false
                    }
                }
                foreach ($key in $keys[$locale].Keys | Where-Object { $keys[$locale][$_] -is [string] -and -not $keys[$locale][$_].Trim() }) {
                    Add-Finding "key '$key' is empty in '$locale'" $file; $clean = $false
                }
            }
            Add-Metric "Translation keys" "$(@($keys[$locales[0]].Keys).Count) per locale ($([string]::Join(', ', $locales)))"
            $clean } },
    @{ Id = "FE-23"; Severity = "Gate"; Name = "No focused tests (fdescribe/fit silently skip every other test)"; Test = { Find-Pattern $specFiles '\b(fdescribe|fit)\s*\(' "focused test" } },
    @{ Id = "SEC-FE-C01"; Severity = "Critical"; Name = "No secrets or private keys in frontend source"; Test = { Find-Pattern ($appFiles + $envFiles) '(?i)(-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk_live_[0-9a-zA-Z]{16,}|(secret|password|api[_-]?key|client[_-]?secret)\s*[:=]\s*[''"][^''"_]{6,}[''"])' "secret in shipped code" } },
    @{ Id = "SEC-FE-C02"; Severity = "Critical"; Name = "No unreviewed DomSanitizer bypass"; Test = { Find-Pattern $appTsFiles 'bypassSecurityTrust(Html|Url|ResourceUrl|Script|Style)' "sanitizer bypass without // security-reviewed:" 'security-reviewed:' } },
    @{ Id = "SEC-FE-C03"; Severity = "Critical"; Name = "No direct DOM injection or dynamic code execution"; Test = { Find-Pattern $appTsFiles '\.(innerHTML|outerHTML)\s*=|document\.write\(|insertAdjacentHTML\(|\beval\(|new Function\(' "XSS sink" } },
    @{ Id = "SEC-FE-H01"; Severity = "High"; Name = "No High/Critical vulnerabilities in runtime dependencies"; Test = {
            $run = Invoke-Logged { npm audit --omit=dev --json 2>$null } $frontend
            try { $audit = ($run.Lines -join "`n") | ConvertFrom-Json }
            catch { Add-Finding "npm audit did not return JSON (no network?)"; return $false }
            $counts = $audit.metadata.vulnerabilities
            if ($counts) { Add-Metric "npm audit (runtime)" "critical $($counts.critical), high $($counts.high), moderate $($counts.moderate), low $($counts.low)" }
            $clean = $true
            $entries = if ($audit.vulnerabilities) { @($audit.vulnerabilities.PSObject.Properties | ForEach-Object { $_.Value }) } else { @() }
            foreach ($entry in $entries) {
                if ($entry.severity -notin @("high", "critical")) { continue }
                $titles = @($entry.via | Where-Object { $_ -isnot [string] } | ForEach-Object { "$($_.title) $($_.url)" }) | Select-Object -First 2
                $fix = if ($entry.fixAvailable -eq $true) { "fix: npm audit fix" } elseif ($entry.fixAvailable) { "fix: upgrade $($entry.fixAvailable.name) to $($entry.fixAvailable.version)" } else { "no fix available" }
                $origin = if ($entry.isDirect) { "direct" } else { "transitive" }
                Add-Finding "$($entry.name) ($($entry.severity), $origin, $($entry.range)): $([string]::Join('; ', $titles)) - $fix"
                $clean = $false
            }
            $clean } },
    @{ Id = "SEC-FE-H06"; Severity = "High"; Name = "Production API URL is not plain HTTP"; Test = { Find-Pattern @($prodEnv) 'apiUrl\s*:\s*[''"]http://' "plain HTTP API URL in production" } },
    @{ Id = "SEC-FE-M05"; Severity = "Medium"; Name = "Links with target=_blank set rel=noopener"; Test = {
            Find-PatternInContent $htmlFiles '<a\b[^>]*\btarget\s*=\s*"_blank"[^>]*>' 'target="_blank" without rel="noopener noreferrer"' { param($match) $match.Value -notmatch 'noopener' } } },
    @{ Id = "FE-24"; Severity = "Medium"; Name = "No skipped tests (xdescribe/xit/pending)"; Test = { Find-Pattern $specFiles '\b(xdescribe|xit)\s*\(|\bpending\s*\(' "skipped test" } },
    @{ Id = "FE-25"; Severity = "Low"; Name = "ESLint warnings"; Test = {
            if ($null -eq $script:eslint) { return (Skip-Check "ESLint did not run") }
            $clean = $true
            foreach ($file in $script:eslint) {
                foreach ($message in @($file.messages | Where-Object { $_.severity -eq 1 })) {
                    Add-Finding "$($message.ruleId): $($message.message)" $file.filePath ([int]$message.line); $clean = $false
                }
            }
            $clean } },
    @{ Id = "FE-26"; Severity = "Low"; Name = "Production build warnings"; Test = {
            if ($script:buildLines.Count -eq 0) { return (Skip-Check "the production build did not run") }
            Add-AngularErrors $script:buildLines "WARNING"
            @($script:buildLines | Where-Object { $_ -match '\[WARNING\]' }).Count -eq 0 } },
    @{ Id = "FE-27"; Severity = "Low"; Name = "Components and services are exercised by at least one spec"; Test = {
            # A class counts as tested when any spec mentions it (layout.spec.ts covers header, sidebar, navigation ...), so
            # indirect tests are not reported. Abstract base classes carry no behaviour; hello.service.ts is a demo wrapper.
            $specText = ($specFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
            $exempt = @("hello.service.ts")
            $untested = @()
            foreach ($file in @($appTsFiles | Where-Object { $_.Name -match '\.(component|service)\.ts$' -and $_.Name -notin $exempt })) {
                $source = Get-Content -LiteralPath $file.FullName -Raw
                $class = [regex]::Match($source, 'export\s+(?<abstract>abstract\s+)?class\s+(?<name>\w+)')
                if (-not $class.Success -or $class.Groups['abstract'].Success) { continue }
                # A component with an empty class and a purely static template (no text, bindings, control flow or child
                # components) has nothing a spec could assert beyond "it is created".
                if ($file.Name -match '\.component\.ts$' -and $source -match 'export\s+class\s+\w+\s*\{\s*\}') {
                    $templateUrl = [regex]::Match($source, "templateUrl:\s*'(?<t>[^']+)'")
                    $templatePath = if ($templateUrl.Success) { Join-Path $file.DirectoryName $templateUrl.Groups['t'].Value }
                    if ($templatePath -and (Test-Path -LiteralPath $templatePath)) {
                        $template = Get-Content -LiteralPath $templatePath -Raw
                        $text = ($template -replace '<!--.*?-->', '' -replace '<[^>]*>', '').Trim()
                        if (-not $text -and $template -notmatch '\{\{|@(if|for|switch|defer)|\s[\(\[\*][\w.-]+|<(app-|router-outlet|ng-)') { continue }
                    }
                }
                if ($specText -notmatch "\b$([regex]::Escape($class.Groups['name'].Value))\b") {
                    Add-Finding "$($class.Groups['name'].Value) is not referenced by any spec" $file.FullName
                    $untested += $file
                }
            }
            $untested.Count -eq 0 } },
    @{ Id = "FE-28"; Severity = "Low"; Name = "No TODO/FIXME/HACK left in source"; Test = { Find-Pattern $appFiles '\b(TODO|FIXME|HACK)\b' "unfinished work" } }
)

$exitCode = Invoke-Review "Frontend Review Report" $checks $report
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
exit $exitCode
