param(
    [string]$SwaggerUrl = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$FrontendDir = Join-Path $RootDir "frontend"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "  G54: Sinh Angular API Client từ Swagger / OpenAPI" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

if ($SwaggerUrl) {
    $env:SWAGGER_URL = $SwaggerUrl
}

Push-Location $FrontendDir
try {
    node scripts/generate-api.mjs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Lỗi trong quá trình sinh API Client."
        exit 1
    }
}
finally {
    Pop-Location
}
