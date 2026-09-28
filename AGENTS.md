# G54 Agent Guide

## Stack

- Backend: ASP.NET Core 8 MVC controllers, BLL/DAL layers, EF Core, PostgreSQL 16, Redis 7.
- Frontend: Angular standalone components with strict TypeScript, Node 22 LTS.
- Operations: Docker Compose, a one-shot DbMigrator, nginx runtime environment replacement.

## Commands

- Start native development: `./run.ps1` on Windows or `./run.sh` on Unix.
- Start all containers: `./run.ps1 -Docker` or `docker compose --env-file docker/.env -f docker/docker-compose.yml up --build`.
- Backend validation: `./scripts/codereview-backend.ps1`.
- Frontend validation: `./scripts/codereview-frontend.ps1`.

## Skills

Load the matching skill before working:

| Task | Skill |
| --- | --- |
| Controllers, services, entities, backend tests | `backend-feature` |
| Schema, migrations, seeds, DbMigrator | `db-migration` |
| Angular components, routes, services, env vars | `frontend-feature` |
| Running review scripts, reports | `code-review` |
| Security review, Critical/High/Medium/Low findings | `security-review` |
| Docker, run scripts, nginx, deployment | `deploy` |

## Rules

- Preserve dependency direction: API -> BLL -> DAL.
- Never put business logic in controllers or access `AppDbContext` from controllers.
- Every schema change requires an EF Core migration; deployment migrations run through DbMigrator.
- Never hardcode deployment URLs or secrets. Add frontend placeholders to both environment files and `replace_env.sh`.
- Critical and High security findings block completion; fix them before finishing.
- Run the applicable code-review script before declaring work complete.
- Do not edit generated reports manually or weaken review scripts to make them pass.
