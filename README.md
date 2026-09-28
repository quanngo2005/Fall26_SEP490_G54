# Fall26 SEP490 G54

Full-stack starter using ASP.NET Core 8, Angular 19, PostgreSQL, Redis, EF Core DbMigrator, Docker Compose, and automated code-review gates.

## Start Development

Prerequisites: .NET 8 SDK (or a newer SDK that can target .NET 8), Node 22 LTS, npm, and optionally Docker Desktop.

```powershell
./run.ps1
```

This starts PostgreSQL, Redis, and pgAdmin through Docker when available, applies migrations, then opens API and frontend watch processes. Without Docker it still starts API and Angular so the Hello World page can be developed.

Run every service in containers:

```powershell
./run.ps1 -Docker
```

- Frontend: http://localhost:4200 (native) or http://localhost:8080 (Docker)
- API: http://localhost:5000/api/hello
- Swagger: http://localhost:5000/swagger
- pgAdmin: http://localhost:5050

## Quality Gates

```powershell
./scripts/codereview-backend.ps1
./scripts/codereview-frontend.ps1
```

Detailed standards live in `docs/codereview`. Reports are generated in `docs/codereview/reports`.

## Configuration

Copy `docker/.env.example` to `docker/.env` and replace development credentials before sharing an environment. Production Angular settings use placeholders that `frontend/scripts/replace_env.sh` replaces when nginx starts.
