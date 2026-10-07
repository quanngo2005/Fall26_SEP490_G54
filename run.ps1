param(
    [switch]$Docker,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiUrl = "http://localhost:5000/health"
$frontendUrl = "http://localhost:4200/login"
$composeFile = Join-Path $root "docker/docker-compose.yml"
$envFile = Join-Path $root "docker/.env"

function Import-DevelopmentEnvironment {
    if (-not (Test-Path $envFile)) {
        Copy-Item (Join-Path $root "docker/.env.example") $envFile
    }

    $settings = @{}
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([A-Za-z0-9_]+)\s*=(.*)$') {
            $settings[$Matches[1]] = $Matches[2].Trim()
        }
    }

    $env:POSTGRES_DB = if ($settings.ContainsKey("POSTGRES_DB")) { $settings["POSTGRES_DB"] } else { "bpms_db" }
    $env:POSTGRES_USER = if ($settings.ContainsKey("POSTGRES_USER")) { $settings["POSTGRES_USER"] } else { "postgres" }
    $env:POSTGRES_PASSWORD = if ($settings.ContainsKey("POSTGRES_PASSWORD")) { $settings["POSTGRES_PASSWORD"] } else { "postgres" }
    $env:BootstrapAdmin__Enabled = if ($settings.ContainsKey("BOOTSTRAP_ADMIN_ENABLED")) { $settings["BOOTSTRAP_ADMIN_ENABLED"] } else { "false" }
    $env:BootstrapAdmin__Email = if ($settings.ContainsKey("BOOTSTRAP_ADMIN_EMAIL")) { $settings["BOOTSTRAP_ADMIN_EMAIL"] } else { "" }
    $env:BootstrapAdmin__Password = if ($settings.ContainsKey("BOOTSTRAP_ADMIN_PASSWORD")) { $settings["BOOTSTRAP_ADMIN_PASSWORD"] } else { "" }

    $connectionString = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connectionString["Host"] = "localhost"
    $connectionString["Port"] = 5432
    $connectionString["Database"] = $env:POSTGRES_DB
    $connectionString["Username"] = $env:POSTGRES_USER
    $connectionString["Password"] = $env:POSTGRES_PASSWORD
    $env:ConnectionStrings__Postgres = $connectionString.ConnectionString
}

function Test-Endpoint {
    param([string]$Uri)

    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 400
    } catch {
        return $false
    }
}

function Wait-ForEndpoint {
    param(
        [string]$Uri,
        [string]$ServiceName,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Endpoint -Uri $Uri) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "$ServiceName did not become ready at $Uri within $TimeoutSeconds seconds. Check its PowerShell window for startup errors."
}

if ($Stop) {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $composeEnvFile = if (Test-Path $envFile) { $envFile } else { Join-Path $root "docker/.env.example" }
        docker compose --env-file $composeEnvFile -f $composeFile down
    }
    Get-Job -Name "G54-*" -ErrorAction SilentlyContinue | Stop-Job | Remove-Job
    exit 0
}

if ($Docker) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker is not installed or not available in PATH."
    }
    Import-DevelopmentEnvironment

    Write-Host "Starting G54 Docker stack:"
    Write-Host "  Frontend: http://localhost:8080/login"
    Write-Host "  API:      http://localhost:5000"
    Write-Host "  Swagger:  http://localhost:5000/swagger"
    Write-Host "  pgAdmin:  http://localhost:5050"

    docker compose --env-file $envFile -f $composeFile up --build
    exit $LASTEXITCODE
}

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Import-DevelopmentEnvironment
    docker compose --env-file $envFile -f $composeFile stop api frontend
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to stop the Docker API and frontend services before starting native development."
    }

    docker compose --env-file $envFile -f $composeFile up -d postgres redis pgadmin
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start PostgreSQL, Redis, and pgAdmin."
    }

    $originalEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project "$root/backend/src/G54.DbMigrator/G54.DbMigrator.csproj"
    $env:ASPNETCORE_ENVIRONMENT = $originalEnvironment
    if ($LASTEXITCODE -ne 0) {
        throw "Database migration failed. Native API and frontend were not started."
    }
} else {
    Write-Warning "Docker is unavailable. PostgreSQL, Redis, pgAdmin, and DbMigrator were skipped."
}

$powerShell = (Get-Process -Id $PID).Path
$apiRunner = Join-Path $root "run-api.ps1"
$frontendRunner = Join-Path $root "run-frontend.ps1"

$apiAlreadyRunning = Test-Endpoint -Uri $apiUrl
$frontendAlreadyRunning = Test-Endpoint -Uri $frontendUrl

if (-not $apiAlreadyRunning -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
    $apiListener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($apiListener) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($apiListener.OwningProcess)" -ErrorAction SilentlyContinue
        $processDescription = if ($process) { "$($process.Name) (PID $($process.ProcessId))" } else { "PID $($apiListener.OwningProcess)" }
        throw "Cannot start the native API because port 5000 is in use by $processDescription, but $apiUrl is not responding. Free port 5000 and run .\run.ps1 again."
    }
}

if (-not $frontendAlreadyRunning -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
    $frontendListener = Get-NetTCPConnection -LocalPort 4200 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($frontendListener) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($frontendListener.OwningProcess)" -ErrorAction SilentlyContinue
        $processDescription = if ($process) { "$($process.Name) (PID $($process.ProcessId))" } else { "PID $($frontendListener.OwningProcess)" }
        throw "Cannot start the frontend because port 4200 is in use by $processDescription, but $frontendUrl is not responding. Free port 4200 and run .\run.ps1 again."
    }
}

if (-not $apiAlreadyRunning) {
    $apiWindow = Start-Process -FilePath $powerShell `
        -ArgumentList @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", "`"$apiRunner`"") `
        -WorkingDirectory $root `
        -PassThru
    Write-Host "Starting API in PowerShell window (PID $($apiWindow.Id))..."
} else {
    Write-Host "API is already responding at http://localhost:5000."
}

if (-not $frontendAlreadyRunning) {
    $frontendWindow = Start-Process -FilePath $powerShell `
        -ArgumentList @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", "`"$frontendRunner`"") `
        -WorkingDirectory $root `
        -PassThru
    Write-Host "Starting frontend in PowerShell window (PID $($frontendWindow.Id))..."
} else {
    Write-Host "Frontend is already responding at $frontendUrl."
}

Write-Host "Waiting for the API and frontend to become ready..."
Wait-ForEndpoint -Uri $apiUrl -ServiceName "API" -TimeoutSeconds 240
Wait-ForEndpoint -Uri $frontendUrl -ServiceName "Frontend" -TimeoutSeconds 240

Write-Host "G54 development is ready:"
Write-Host "  Frontend: $frontendUrl"
Write-Host "  API:      http://localhost:5000"
Write-Host "  Swagger:  http://localhost:5000/swagger"
Write-Host "  pgAdmin:  http://localhost:5050 (when Docker is available)"
Write-Host "Opening the login page in your browser..."
Start-Process $frontendUrl
Write-Host "Keep the API and frontend PowerShell windows open while using the site."
