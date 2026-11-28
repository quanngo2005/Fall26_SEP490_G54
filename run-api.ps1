$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $root "docker/.env"
if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $root "docker/.env.example") $envFile
}

$settings = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z0-9_]+)\s*=(.*)$') {
        $settings[$Matches[1]] = $Matches[2].Trim()
    }
}

$connectionString = [System.Data.Common.DbConnectionStringBuilder]::new()
$connectionString["Host"] = "localhost"
$connectionString["Port"] = 5432
$connectionString["Database"] = if ($settings.ContainsKey("POSTGRES_DB")) { $settings["POSTGRES_DB"] } else { "bpms_db" }
$connectionString["Username"] = if ($settings.ContainsKey("POSTGRES_USER")) { $settings["POSTGRES_USER"] } else { "postgres" }
$connectionString["Password"] = if ($settings.ContainsKey("POSTGRES_PASSWORD")) { $settings["POSTGRES_PASSWORD"] } else { "postgres" }

$env:ConnectionStrings__Postgres = $connectionString.ConnectionString
$env:Jwt__Key = if ($settings.ContainsKey("JWT_KEY")) { $settings["JWT_KEY"] } else { "" }
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Optional G54 Queue (sibling repo ..\G54-Queue): keep values already set by its setup.ps1, otherwise read its key here
# so the lobby and counter screens work without dot-sourcing setup.ps1 in the same PowerShell session first.
$queueEnvFile = Join-Path (Split-Path -Parent $root) "G54-Queue/.env"
if (-not $env:Queue__ServiceKey -and (Test-Path -LiteralPath $queueEnvFile)) {
    $env:Queue__ServiceKey = (Get-Content -Raw -LiteralPath $queueEnvFile | ConvertFrom-StringData).QUEUE_SERVICE_KEY
}
if ($env:Queue__ServiceKey) {
    if (-not $env:Queue__Origin) { $env:Queue__Origin = "http://localhost:5480" }
    if (-not $env:Queue__BranchCode) { $env:Queue__BranchCode = "CN-HN" }
}

$env:ASPNETCORE_URLS = "http://localhost:5000"
dotnet watch --project "$root/backend/src/G54.Api/G54.Api.csproj" run
if ($LASTEXITCODE -ne 0) {
    throw "The native API exited with code $LASTEXITCODE."
}
