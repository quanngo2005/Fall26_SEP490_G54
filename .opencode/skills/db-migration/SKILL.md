---
name: db-migration
description: Use when changing PostgreSQL schema, EF Core entities, migrations, seed data, connection strings, or the G54 DbMigrator.
---

# Database Migration

## Facts

- Database: PostgreSQL 16. Dev credentials: database `postgres`, user `postgres`, password `postgres`, `localhost:5432`.
- DbContext: `backend/src/G54.DAL/AppDbContext.cs`. Migrations: `backend/src/G54.DAL/Migrations`.
- DbMigrator: `backend/src/G54.DbMigrator/Program.cs` reads `ConnectionStrings__Postgres`, runs `MigrateAsync`, then `DatabaseSeeder.SeedAsync`.
- In Docker, the `migrator` service must exit 0 before `api` starts.

## Create A Migration

```powershell
dotnet tool install --global dotnet-ef --version 8.*   # once
dotnet ef migrations add <Name> --project backend/src/G54.DAL --startup-project backend/src/G54.DbMigrator
```

If design-time creation fails, add an `IDesignTimeDbContextFactory<AppDbContext>` in DAL that reads `ConnectionStrings__Postgres`.

## Apply

- Native: `dotnet run --project backend/src/G54.DbMigrator`
- Docker: `docker compose --env-file docker/.env -f docker/docker-compose.yml up migrator`

## Rules

- Every schema change needs a migration; never edit applied migrations.
- `Down` must reverse `Up` safely.
- Use snake_case table/column names, matching existing configuration.
- Seeds must be idempotent (check before insert).
- Never call `Database.Migrate()` from the API.
- Destructive changes (drop column/table) need a data-migration plan in the PR description.
- Reset the local Docker database only with `docker compose ... down -v` (deletes data).
