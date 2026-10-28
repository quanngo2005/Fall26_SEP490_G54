$ErrorActionPreference = "Continue"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Import-Module (Join-Path $PSScriptRoot "codereview-common.psm1") -Force -DisableNameChecking
Initialize-Review $root

$backend = Join-Path $root "backend"
$solution = Join-Path $backend "G54.sln"
$report = Join-Path $root "docs/codereview/reports/BACKEND_REPORT.md"
$testResults = Join-Path ([IO.Path]::GetTempPath()) "g54-review-$([guid]::NewGuid().ToString('N'))"

function Get-SourceFiles([string[]]$Include, [switch]$SkipMigrations, [string]$Under = "src") {
    Get-ChildItem -Path (Join-Path $backend $Under) -Recurse -File -Include $Include |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Where-Object { -not $SkipMigrations -or $_.FullName -notmatch '[\\/]Migrations[\\/]' }
}

$csFiles = Get-SourceFiles -Include "*.cs" -SkipMigrations
$controllerFiles = $csFiles | Where-Object { $_.FullName -match '[\\/]Controllers[\\/]' }
$serviceFiles = $csFiles | Where-Object { $_.FullName -notmatch '[\\/]G54\.DbMigrator[\\/]' }
$productionSettings = Get-SourceFiles -Include "appsettings.Production*.json"
$script:trx = $null
$script:cobertura = $null
$script:testsBlocked = $false

function Get-NormalizedRepoPath([string]$Path) {
    # Coverage produced in Docker reports /work/backend/...; map every form back to backend/...
    $normalized = ConvertTo-RepoPath $Path
    $index = $normalized.IndexOf("backend/", [StringComparison]::OrdinalIgnoreCase)
    if ($index -gt 0) { return $normalized.Substring($index) }
    return $normalized
}

<# Failed tests from the TRX log, located at the first stack frame inside the test project. #>
function Add-TrxFailures([xml]$Trx) {
    foreach ($result in @($Trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "Failed" })) {
        $message = "$($result.Output.ErrorInfo.Message)".Trim() -split "`r?`n" | Select-Object -First 2
        $message = ([string]::Join(" ", $message) -replace '\s+', ' ')
        if ($message.Length -gt 300) { $message = $message.Substring(0, 300) + "..." }
        $frames = [regex]::Matches("$($result.Output.ErrorInfo.StackTrace)", ' in (?<file>.+?):line (?<line>\d+)')
        $frame = @($frames | Where-Object { $_.Groups['file'].Value -match '[\\/]tests[\\/]' } | Select-Object -First 1)
        if ($frame.Count -eq 0) { $frame = @($frames | Select-Object -First 1) }
        $method = $result.testName -replace '\(.*$', ''
        $name = (($method -split '\.') | Select-Object -Last 2) -join '.'
        if ($result.testName -match '\(.*\)$') { $name += $Matches[0] }
        if ($frame.Count -gt 0) { Add-Finding "$name - $message" (Get-NormalizedRepoPath $frame[0].Groups['file'].Value) ([int]$frame[0].Groups['line'].Value) }
        else { Add-Finding "$name - $message" }
    }
}

<# Lists endpoints that are anonymous or rely only on [Authorize] (any signed-in user) instead of [HasPermission]. #>
# Endpoints reviewed by hand and intentionally left without [HasPermission]. Keyed by "VERB /route" exactly as the
# check prints it, so renaming or moving a route puts it back under review. Every entry must say why it is safe.
$intentionallyOpen = @{
    # Public by necessity: the caller has no token yet (or only an expired one)
    "POST /auth/login"                                     = "anonymous: obtains the token"
    "POST /auth/refresh"                                   = "anonymous: validates the refresh token against the account"
    "POST /auth/logout"                                    = "service requires access + refresh token of the same account"
    "GET /hello"                                           = "static demo payload, no data"
    # Own data only: the id comes from the token, never from the request
    "GET /employees/me"                                    = "caller's own profile"
    "GET /me/permissions"                                  = "caller's own permissions"
    "PUT /me/profile"                                      = "ProfileService updates the token's account"
    "POST /me/password"                                    = "AuthService verifies the current password"
    "GET /sidebar-configurations"                          = "SidebarConfigurationService filters by the caller's roles"
    "GET /sidebar-configurations/current"                  = "SidebarConfigurationService filters by the caller's roles"
    "PUT /sidebar-configurations/{role}"                   = "SidebarConfigurationService requires the admin role"
    # Reference/catalog data used to fill forms, nothing sensitive (kpi:read is granted to every role)
    "GET /employees/departments"                           = "reference list"
    "GET /employees/job-positions"                         = "reference list"
    "GET /kpi-definitions"                                 = "catalog read"
    "GET /kpi-definitions/groups"                          = "catalog read"
    "GET /kpi-definitions/{id:guid}"                       = "catalog read"
    "GET /business-objectives/{objectiveId:guid}/kpis"     = "catalog read"
    "GET /business-objectives/{objectiveId:guid}/kpis/available" = "catalog read"
    "GET /work-types"                                      = "catalog read"
    "GET /work-types/kpis"                                 = "catalog read"
    "GET /work-types/{id:guid}"                            = "catalog read"
    # Ownership/scope enforced in the service (covered by unit tests)
    "POST /submissions/{submissionId:guid}/appeals"        = "AppealService: caller must be the submitter"
    "POST /appeals/{id:guid}/withdraw"                     = "AppealService: caller must be the filer"
    "GET /appeals"                                         = "AppealService: scoped to own or department"
    "GET /appeals/{id:guid}/score-entries"                 = "AppealService: same own/department scope as the appeal detail"
    "POST /appeals/{id:guid}/score-dispute"                = "AppealService: caller must be the filer"
    "GET /appeals/{id:guid}"                               = "AppealService: scoped to own or department"
    "GET /evidence/{id:guid}/download"                     = "EvidenceService.CanViewAsync"
    "GET /evidence/{id:guid}/preview"                      = "EvidenceService.CanViewAsync"
    "GET /works/{workId:guid}/submission-context"          = "ResultSubmissionService: assignee only"
    "POST /works/{workId:guid}/submissions"                = "ResultSubmissionService: assignee only"
    "GET /works/{workId:guid}/submissions"                 = "ResultSubmissionService: submitter/assignee/submission:read scope"
    "GET /submissions/{submissionId:guid}"                 = "ResultSubmissionService: submitter/assignee/submission:read scope"
    "GET /works/{workId:guid}/submissions/{submissionId:guid}" = "ResultSubmissionService: submitter/assignee/submission:read scope"
    "POST /submissions/{submissionId:guid}/withdraw"       = "ResultSubmissionService: submitter only"
    "POST /works/{workId:guid}/submissions/{submissionId:guid}/withdraw" = "ResultSubmissionService: submitter only"
    "PATCH /works/{id:guid}/status"                        = "WorkService: assignee or direct manager"
}

function Find-OpenEndpoints {
    $clean = $true
    foreach ($file in $controllerFiles) {
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $classIndex = 0
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '\bclass\s+\w+') { $classIndex = $i; break } }
        $classAttrs = [string]::Join("`n", $lines[0..$classIndex])
        $baseRoute = if ($classAttrs -match '\[Route\("(?<r>[^"]*)"\)\]') { $Matches['r'] } else { "" }
        for ($i = $classIndex + 1; $i -lt $lines.Count; $i++) {
            $verb = [regex]::Match($lines[$i], '\[Http(?<verb>Get|Post|Put|Patch|Delete)(\("(?<route>[^"]*)"\))?')
            if (-not $verb.Success) { continue }
            $start = $i
            while ($start -gt 0 -and $lines[$start - 1] -match '^\s*(\[|///)') { $start-- }
            $end = $i
            while ($end -lt $lines.Count - 1 -and $lines[$end] -notmatch '\b(public|private|protected)\b.*\(') { $end++ }
            $attrs = [string]::Join("`n", $lines[$start..$end])
            $route = "/" + ((@($baseRoute, $verb.Groups['route'].Value) | Where-Object { $_ }) -join "/")
            $endpoint = "$($verb.Groups['verb'].Value.ToUpperInvariant()) $route"
            if ($intentionallyOpen.ContainsKey($endpoint)) { $i = $end; continue }
            if ($attrs -match 'AllowAnonymous' -or $classAttrs -match 'AllowAnonymous') {
                Add-Finding "$endpoint is anonymous - confirm it must be public" $file.FullName ($i + 1); $clean = $false
            }
            elseif ($attrs -notmatch 'HasPermission\(|Authorize\(\s*(Roles|Policy)' -and $classAttrs -notmatch 'HasPermission\(|Authorize\(\s*(Roles|Policy)') {
                Add-Finding "$endpoint has no [HasPermission]: every signed-in user reaches it - confirm the service checks ownership/role (IDOR)" $file.FullName ($i + 1); $clean = $false
            }
            $i = $end
        }
    }
    return $clean
}

$checks = @(
    @{ Id = "BE-01"; Severity = "Gate"; Name = "Restore"; Test = {
            $run = Invoke-Logged { dotnet restore $solution }
            Add-MsBuildDiagnostics $run.Lines
            $run.ExitCode -eq 0 } },
    @{ Id = "BE-02"; Severity = "Gate"; Name = "Release build (warnings as errors)"; Test = {
            $run = Invoke-Logged { dotnet build $solution -c Release --no-restore }
            Add-MsBuildDiagnostics $run.Lines -IncludeWarnings
            $run.ExitCode -eq 0 } },
    @{ Id = "BE-03"; Severity = "Gate"; Name = "Unit tests (with coverage collection)"; Test = {
            $run = Invoke-Logged { dotnet test $solution -c Release --no-build --settings (Join-Path $backend "coverlet.runsettings") --collect:"XPlat Code Coverage" --logger "trx;LogFileName=tests.trx" --results-directory $testResults }
            $script:cobertura = Get-ChildItem -Path $testResults -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue | Select-Object -First 1
            $trxFile = Get-ChildItem -Path $testResults -Recurse -Filter "tests.trx" -ErrorAction SilentlyContinue | Select-Object -First 1
            if (($run.Lines -join "`n") -match 'Application Control policy|0x800711C7') {
                $script:testsBlocked = $true
                Add-Finding "Windows Smart App Control / Application Control blocked the test DLLs, so no test ran. Turn Smart App Control off or run the tests in Docker (scripts/coverage-in-docker.ps1)."
            }
            if ($trxFile) {
                [xml]$script:trx = Get-Content -LiteralPath $trxFile.FullName -Raw -Encoding utf8
                $counters = $script:trx.TestRun.ResultSummary.Counters
                Add-Metric "Backend tests" "$($counters.passed) passed, $($counters.failed) failed, $($counters.notExecuted) skipped of $($counters.total)"
                Add-TrxFailures $script:trx
                if ([int]$counters.executed -eq 0) { Add-Finding "no test was executed" }
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "BE-04"; Severity = "Gate"; Name = "Formatting (dotnet format)"; Test = {
            $run = Invoke-Logged { dotnet format $solution --verify-no-changes --no-restore }
            Add-MsBuildDiagnostics $run.Lines -IncludeWarnings -GroupByFile
            if ($run.ExitCode -ne 0) { Add-Finding "fix with: dotnet format backend/G54.sln" }
            $run.ExitCode -eq 0 } },
    @{ Id = "BE-05"; Severity = "Gate"; Name = "No build output, binaries or secret files committed"; Test = {
            Push-Location $root
            $tracked = @(git ls-files 2>$null)
            Pop-Location
            $bad = @($tracked | Where-Object { $_ -match '(^|/)(bin|obj)/|\.(dll|exe|pdb|nupkg|pfx|p12|key)$|(^|/)\.env$' })
            foreach ($path in $bad) { Add-Finding "tracked by git but must not be committed" $path }
            $bad.Count -eq 0 } },
    @{ Id = "BE-06"; Severity = "Gate"; Name = "Controllers do not access DAL/AppDbContext"; Test = { Find-Pattern $controllerFiles 'using G54\.DAL|AppDbContext' "controller touches the data layer" } },
    @{ Id = "BE-07"; Severity = "Gate"; Name = "Layering: BLL does not reference Api; DAL references neither BLL nor Api"; Test = {
            $rules = @{ "G54.BLL" = @("G54.Api"); "G54.DAL" = @("G54.BLL", "G54.Api") }
            $clean = $true
            foreach ($project in $rules.Keys) {
                $csproj = Join-Path $backend "src/$project/$project.csproj"
                foreach ($forbidden in $rules[$project]) {
                    $hit = Select-String -LiteralPath $csproj -Pattern "ProjectReference.*$([regex]::Escape($forbidden))\.csproj"
                    foreach ($h in $hit) { Add-Finding "$project must not reference $forbidden" $csproj $h.LineNumber; $clean = $false }
                }
            }
            $clean } },
    @{ Id = "BE-08"; Severity = "Gate"; Name = "EF model has no changes missing a migration"; Test = {
            dotnet ef --version *> $null
            if ($LASTEXITCODE -ne 0) {
                if ($env:CI -eq "true") { Add-Finding "dotnet-ef is not installed on the CI runner"; return $false }
                return (Skip-Check "dotnet-ef is not installed (dotnet tool install -g dotnet-ef --version 8.*)")
            }
            $dal = Join-Path $backend "src/G54.DAL"
            $run = Invoke-Logged { dotnet ef migrations has-pending-model-changes --project $dal --startup-project $dal --context AppDbContext }
            if ($run.ExitCode -ne 0 -and ($run.Lines -join "`n") -match 'Changes have been made to the model') {
                Add-Finding "the EF model changed without a migration: run dotnet ef migrations add <Name> --project backend/src/G54.DAL"
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "BE-18"; Severity = "Gate"; Name = "Line coverage thresholds (migrations excluded)"; Test = {
            if ($script:testsBlocked -and $env:CI -ne "true") {
                docker info *> $null
                if ($LASTEXITCODE -ne 0) { return (Skip-Check "Application Control blocked the tests and Docker is unavailable; thresholds are enforced in CI") }
                Write-Host "Application Control blocked coverage on this host; measuring inside a Docker container."
                $dockerOut = Join-Path $testResults "docker"
                $run = Invoke-Logged { & (Join-Path $PSScriptRoot "coverage-in-docker.ps1") -OutDir $dockerOut }
                if ($run.ExitCode -ne 0) { Add-Finding "tests failed inside Docker, see the log"; return $false }
                $script:cobertura = Get-ChildItem -Path $dockerOut -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue | Select-Object -First 1
            }
            if ($null -eq $script:cobertura) { Add-Finding "no coverage report was produced (the test run failed or did not start)"; return $false }
            Copy-Item -LiteralPath $script:cobertura.FullName -Destination (Join-Path (Split-Path -Parent $report) "coverage.cobertura.xml") -Force
            $run = Invoke-Logged { & (Join-Path $PSScriptRoot "check-coverage.ps1") -CoberturaFile $script:cobertura.FullName }
            foreach ($line in $run.Lines) {
                $m = [regex]::Match($line, '^(?<name>\S+)\s+\d+\s+\d+\s+(?<pct>[\d.]+)%\s+(?<thr>[\d.]*)%(?<fail>\s+FAIL)?')
                if (-not $m.Success) { continue }
                $threshold = if ($m.Groups['thr'].Value) { "threshold $($m.Groups['thr'].Value)%" } else { "no threshold" }
                Add-Metric "Line coverage $($m.Groups['name'].Value)" "$($m.Groups['pct'].Value)% ($threshold)"
                if ($m.Groups['fail'].Success) { Add-Finding "$($m.Groups['name'].Value) line coverage $($m.Groups['pct'].Value)% is below the $($m.Groups['thr'].Value)% threshold" }
            }
            $run.ExitCode -eq 0 } },
    @{ Id = "SEC-BE-C01"; Severity = "Critical"; Name = "No hardcoded private keys, cloud keys, or production credentials"; Test = {
            $a = Find-Pattern $csFiles '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk_live_[0-9a-zA-Z]{16,}' "secret in source"
            $b = Find-Pattern $productionSettings '(?i)(password|pwd)\s*=\s*[^;"\s]+|"Key"\s*:\s*"[^"_]{16,}"' "credential in production settings"
            $a -and $b } },
    @{ Id = "SEC-BE-C02"; Severity = "Critical"; Name = "No raw SQL built from interpolation or concatenation"; Test = { Find-Pattern $csFiles '(FromSqlRaw|ExecuteSqlRaw)(Async)?\s*\(\s*(\$"|[^)]*"\s*\+)' "SQL injection risk" } },
    @{ Id = "SEC-BE-C05"; Severity = "Critical"; Name = "JWT validation flags are never disabled"; Test = { Find-Pattern $csFiles '(ValidateIssuerSigningKey|ValidateLifetime|ValidateIssuer|ValidateAudience)\s*=\s*false' "JWT validation disabled" } },
    @{ Id = "SEC-BE-C07"; Severity = "Critical"; Name = "No insecure deserialization"; Test = { Find-Pattern $csFiles 'BinaryFormatter|TypeNameHandling\.(All|Auto|Objects)' "insecure deserializer" } },
    @{ Id = "SEC-BE-H01"; Severity = "High"; Name = "No High/Critical vulnerable NuGet packages"; Test = {
            $run = Invoke-Logged { dotnet list $solution package --vulnerable --include-transitive }
            if ($run.ExitCode -ne 0) { Add-Finding "dotnet list package --vulnerable failed (no network?)"; return $false }
            $bad = @($run.Lines | Where-Object { $_ -match '^\s*>\s*\S+.*\b(High|Critical)\b' })
            foreach ($line in $bad) { Add-Finding "vulnerable package: $(($line.Trim() -replace '^>\s*', '') -replace '\s{2,}', ' | ')" }
            $bad.Count -eq 0 } },
    @{ Id = "SEC-BE-H05"; Severity = "High"; Name = "CORS never allows any origin with credentials"; Test = {
            $bad = @($csFiles | Where-Object { (Select-String -Path $_.FullName -Pattern 'AllowAnyOrigin\s*\(' -Quiet) -and (Select-String -Path $_.FullName -Pattern 'AllowCredentials\s*\(' -Quiet) })
            foreach ($file in $bad) { Add-Finding "combines AllowAnyOrigin and AllowCredentials" $file.FullName }
            $bad.Count -eq 0 } },
    @{ Id = "SEC-BE-M08"; Severity = "Medium"; Name = "Endpoints without a declarative permission are listed for review"; Test = { Find-OpenEndpoints } },
    @{ Id = "SEC-BE-M09"; Severity = "Medium"; Name = "Log statements do not mention passwords, tokens or secrets"; Test = {
            Find-Pattern $csFiles '(?i)(Log(Trace|Debug|Information|Warning|Error|Critical)|LoggerMessage\.Define).*\b(password|passwd|token|secret)\b' "possible sensitive data in a log" } },
    @{ Id = "BE-19"; Severity = "Medium"; Name = "No test was skipped"; Test = {
            if ($null -eq $script:trx) { return (Skip-Check "no test results (BE-03 did not run the tests)") }
            $skippedTests = @($script:trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "NotExecuted" })
            foreach ($test in $skippedTests) {
                $reason = "$($test.Output.StdOut)$($test.Output.ErrorInfo.Message)".Trim()
                Add-Finding "skipped: $($test.testName)$(if ($reason) { " - $reason" })"
            }
            $skippedTests.Count -eq 0 } },
    @{ Id = "BE-20"; Severity = "Medium"; Name = "No sync-over-async, async void, empty catch or console output in services"; Test = {
            $a = Find-Pattern $serviceFiles '\.Result\b(?!\s*=)|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)' "blocking on async code (deadlock/thread starvation risk)"
            $b = Find-Pattern $serviceFiles '\basync\s+void\b' "async void: exceptions crash the process"
            $c = Find-PatternInContent $serviceFiles 'catch\s*(\([^)]*\))?\s*\{\s*\}' "empty catch swallows errors"
            $d = Find-Pattern $serviceFiles 'Console\.Write(Line)?\(' "use ILogger instead of Console"
            $a -and $b -and $c -and $d } },
    @{ Id = "BE-21"; Severity = "Low"; Name = "Files with low test coverage (under 50%, at least 30 lines)"; Test = {
            if ($null -eq $script:cobertura) { return (Skip-Check "no coverage report") }
            [xml]$xml = Get-Content -LiteralPath $script:cobertura.FullName -Raw
            $files = @{}
            foreach ($class in $xml.SelectNodes("//class")) {
                $name = $class.filename
                if (-not $files.ContainsKey($name)) { $files[$name] = @{} }
                foreach ($line in $class.SelectNodes("lines/line")) {
                    $number = [int]$line.number
                    $hit = [int]$line.hits -gt 0
                    $files[$name][$number] = ($files[$name][$number] -or $hit)
                }
            }
            # Files that hold no unit-testable logic: host/DI wiring, database seeding and plain data carriers (DTO records,
            # entity classes). They are exercised by the integration/startup path, not by unit tests, so they are not reported.
            $noUnitLogic = '(^|/)Program\.cs$|ServiceCollectionExtensions\.cs$|(^|/)DatabaseSeeder\.cs$|(^|/)Dtos/|(^|/)Entities/'
            foreach ($name in @($files.Keys)) { if ((Get-NormalizedRepoPath $name) -replace '\\', '/' -match $noUnitLogic) { $files.Remove($name) } }
            $low = @($files.Keys | ForEach-Object {
                    $total = $files[$_].Count
                    $covered = @($files[$_].Values | Where-Object { $_ }).Count
                    [pscustomobject]@{ File = $_; Total = $total; Percent = if ($total) { [math]::Round(100.0 * $covered / $total, 1) } else { 100 } }
                } | Where-Object { $_.Total -ge 30 -and $_.Percent -lt 50 } | Sort-Object Percent | Select-Object -First 20)
            foreach ($entry in $low) { Add-Finding "$($entry.Percent)% of $($entry.Total) lines covered" (Get-NormalizedRepoPath $entry.File) }
            $low.Count -eq 0 } },
    @{ Id = "BE-22"; Severity = "Low"; Name = "No TODO/FIXME/HACK left in source"; Test = { Find-Pattern $csFiles '\b(TODO|FIXME|HACK)\b' "unfinished work" } }
)

$exitCode = Invoke-Review "Backend Review Report" $checks $report
Remove-Item -LiteralPath $testResults -Recurse -Force -ErrorAction SilentlyContinue
exit $exitCode
