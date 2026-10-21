<#
.SYNOPSIS
Runs the backend tests with coverage inside a Linux .NET SDK container and writes coverage.cobertura.xml to -OutDir.
.DESCRIPTION
Fallback for hosts that block coverlet's rewritten assemblies (Windows Application Control, error 0x800711C7). The source
is copied inside the container, so no Linux build output lands in the repository. The container does not get the Docker
socket; it reaches Postgres and Redis already running on the host (docker compose up) through host.docker.internal.
CI=true makes a missing database fail instead of skip, so a thin coverage number can never come from skipped tests.
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Image = "mcr.microsoft.com/dotnet/sdk:8.0",
    [string]$Postgres = "Host=host.docker.internal;Port=5432;Database=postgres;Username=postgres;Password=postgres",
    [string]$Redis = "host.docker.internal:6379",
    [string]$Filter = ""
)

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$inner = 'set -e; mkdir /work; tar -C /src --exclude=bin --exclude=obj -cf - backend global.json | tar -C /work -xf -; ' +
    'cd /work/backend; dotnet test tests/G54.UnitTests -c Release --settings coverlet.runsettings ' +
    '--collect:''XPlat Code Coverage'' --results-directory /out'
if ($Filter) { $inner += " --filter '$Filter'" }
docker run --rm `
    -v "${root}:/src:ro" `
    -v "${OutDir}:/out" `
    -e CI=true `
    -e "G54_TEST_POSTGRES=$Postgres" `
    -e "G54_TEST_REDIS=$Redis" `
    $Image bash -c $inner
exit $LASTEXITCODE
