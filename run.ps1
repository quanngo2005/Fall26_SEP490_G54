param(
    [switch]$Docker,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($Stop) {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        docker compose -f "$root/docker/docker-compose.yml" down
    }
    Get-Job -Name "G54-*" -ErrorAction SilentlyContinue | Stop-Job | Remove-Job
    exit 0
}

if ($Docker) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker is not installed or not available in PATH."
    }
    if (-not (Test-Path "$root/docker/.env")) {
        Copy-Item "$root/docker/.env.example" "$root/docker/.env"
    }
    docker compose --env-file "$root/docker/.env" -f "$root/docker/docker-compose.yml" up --build
    exit $LASTEXITCODE
}

if (Get-Command docker -ErrorAction SilentlyContinue) {
    docker compose -f "$root/docker/docker-compose.yml" up -d postgres redis pgadmin
    dotnet run --project "$root/backend/src/G54.DbMigrator/G54.DbMigrator.csproj"
} else {
    Write-Warning "Docker is unavailable. PostgreSQL, Redis, pgAdmin, and DbMigrator were skipped."
}

if (-not (Test-Path "$root/frontend/node_modules")) {
    npm install --prefix "$root/frontend"
}

Start-Process powershell -ArgumentList "-NoExit", "-Command", "`$env:ASPNETCORE_URLS='http://localhost:5000'; dotnet watch --project '$root/backend/src/G54.Api/G54.Api.csproj' run"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "npm start --prefix '$root/frontend' -- --host 0.0.0.0"

Write-Host "G54 development started:"
Write-Host "  Frontend: http://localhost:4200"
Write-Host "  API:      http://localhost:5000/api/hello"
Write-Host "  Swagger:  http://localhost:5000/swagger"
Write-Host "  pgAdmin:  http://localhost:5050 (when Docker is available)"
