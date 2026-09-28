---
name: deploy
description: Use when running, containerizing, configuring, or deploying the G54 API, frontend, PostgreSQL, Redis, pgAdmin, and DbMigrator, or when editing run.ps1, run.sh, Dockerfiles, docker-compose, or nginx.
---

# Deploy

## Services (docker/docker-compose.yml)

| Service | Port | Notes |
| --- | --- | --- |
| postgres | 5432 | `postgres`/`postgres`/`postgres` in dev |
| redis | 6379 | cache |
| pgadmin | 5050 | `admin@example.com` / `admin`; connect to host `postgres` |
| migrator | - | one-shot, must exit 0 |
| api | 5000 -> 8080 | healthcheck `curl /health` |
| frontend | 8080 -> 80 | nginx, proxies `/api` to `api:8080` |

Startup order: postgres healthy -> migrator completed -> redis healthy -> api healthy -> frontend.

## Commands

- Native dev (infra in Docker, apps with hot reload): `./run.ps1` or `./run.sh`
- Full stack in Docker: `./run.ps1 -Docker`
- Stop: `./run.ps1 -Stop`
- Manual: `docker compose --env-file docker/.env -f docker/docker-compose.yml up --build -d`
- Reset data: `docker compose --env-file docker/.env -f docker/docker-compose.yml down -v`
- Production overlay: add `-f docker/docker-compose.prod.yml`

## Configuration

- `docker/.env.example` is the variable contract; `docker/.env` is local and git-ignored.
- Backend reads env vars `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `Jwt__Key`.
- Frontend image is built once; `frontend/scripts/replace_env.sh` replaces `__API_URL__` and `__APP_VERSION__` at container start.

## Toolchain

- .NET SDK 8.0.425 (pinned in `global.json`), Node 22 LTS, Docker Desktop with WSL2.

## Troubleshooting

- `dockerDesktopLinuxEngine` pipe missing: start Docker Desktop; check `wsl --version`.
- API unhealthy: `docker compose ... logs api`; verify healthcheck tool exists in the image.
- Migrator failed: check the Postgres credentials and that the volume was created with the same credentials (recreate with `down -v`).

## Production Rules

- Replace all dev credentials and `JWT_KEY` with strong secrets from the host or secret store.
- Do not expose postgres/redis ports; disable pgAdmin.
- Serve behind HTTPS; keep nginx security headers.
- Never commit `.env` or real secrets.
