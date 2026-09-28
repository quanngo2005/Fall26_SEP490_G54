---
name: backend-feature
description: Use when adding or changing ASP.NET Core controllers, BLL services, DAL entities, repositories, DTOs, validators, or backend tests in G54.
---

# Backend Feature

## Layout

```
backend/
  G54.sln
  src/G54.Api          Controllers, middleware, health checks, Program.cs
  src/G54.BLL          Services, interfaces, DTOs, validation, business rules
  src/G54.DAL          AppDbContext, entities, migrations, seeder
  src/G54.DbMigrator   Console app that applies migrations and seeds
  tests/G54.UnitTests  xUnit tests
```

Dependency direction: `Api -> BLL -> DAL`. DbMigrator -> DAL only.

## Steps To Add A Feature

1. **Entity (DAL)**: add `src/G54.DAL/Entities/<Name>.cs` inheriting `BaseEntity`. Configure table and snake_case columns in `AppDbContext.OnModelCreating`.
2. **Migration**: load the `db-migration` skill and create a migration.
3. **DTOs (BLL)**: add request/response records in `src/G54.BLL/Dtos/<Feature>/`. Never expose entities.
4. **Service (BLL)**: add `I<Name>Service` and `<Name>Service` in `src/G54.BLL/Services/`. Accept `CancellationToken`. Use `AsNoTracking()` for reads. Enforce ownership/authorization rules here.
5. **Register**: add the service in `G54.BLL/ServiceCollectionExtensions.AddBusinessLogic`.
6. **Controller (Api)**: add `src/G54.Api/Controllers/<Name>Controller.cs` with `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]` unless public, and `ProducesResponseType` attributes. Controllers only call services.
7. **Tests**: add `tests/G54.UnitTests/<Name>ServiceTests.cs`.
8. **Review**: run `./scripts/codereview-backend.ps1` until it passes.

## Conventions

- File-scoped namespaces, `sealed` classes where possible, primary constructors for DI.
- Nullable enabled; warnings are errors (`Directory.Build.props`).
- Logging uses `LoggerMessage.Define` (CA1848).
- Errors surface as `ProblemDetails` via `GlobalExceptionHandler`.
- Config comes from `appsettings.json` + environment variables (`Section__Key`).
- SDK pinned by `global.json` to .NET 8.

## Never

- Access `AppDbContext` from controllers.
- Auto-migrate in API startup.
- Hardcode secrets or production URLs.
