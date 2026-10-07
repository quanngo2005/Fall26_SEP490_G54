$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$frontend = Join-Path $root "frontend"

if (-not (Test-Path (Join-Path $frontend "node_modules"))) {
    npm install --prefix $frontend
    if ($LASTEXITCODE -ne 0) {
        throw "Frontend dependency installation failed."
    }
}

Push-Location $frontend
try {
    npm start -- --host 0.0.0.0
    if ($LASTEXITCODE -ne 0) {
        throw "The frontend dev server exited with code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
