#!/usr/bin/env sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if command -v docker >/dev/null 2>&1; then
  docker compose -f "$ROOT/docker/docker-compose.yml" up -d postgres redis pgadmin
  dotnet run --project "$ROOT/backend/src/G54.DbMigrator/G54.DbMigrator.csproj"
else
  echo "Warning: Docker unavailable; infrastructure and migrations skipped."
fi

[ -d "$ROOT/frontend/node_modules" ] || npm install --prefix "$ROOT/frontend"
ASPNETCORE_URLS=http://localhost:5000 dotnet watch --project "$ROOT/backend/src/G54.Api/G54.Api.csproj" run &
npm start --prefix "$ROOT/frontend" -- --host 0.0.0.0 &
trap 'kill 0' INT TERM EXIT
wait
