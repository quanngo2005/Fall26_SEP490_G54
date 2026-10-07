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
$env:ASPNETCORE_ENVIRONMENT = "Development"

$env:ASPNETCORE_URLS = "http://localhost:5000"
dotnet watch --project "$root/backend/src/G54.Api/G54.Api.csproj" run
if ($LASTEXITCODE -ne 0) {
    throw "The native API exited with code $LASTEXITCODE."
}
