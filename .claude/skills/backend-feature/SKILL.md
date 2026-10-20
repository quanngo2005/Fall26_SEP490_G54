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

Before implementation, follow the API Contract section below. Reuse a frontend-first
contract when present; do not independently invent a route or response shape.

1. **Entity (DAL)**: add `src/G54.DAL/Entities/<Name>.cs` inheriting `BaseEntity`. Configure table and snake_case columns in `AppDbContext.OnModelCreating`.
2. **Migration**: load the `db-migration` skill and create a migration.
3. **DTOs (BLL)**: add request/response records in `src/G54.BLL/Dtos/<Feature>/`. Never expose entities.
4. **Service (BLL)**: add `I<Name>Service` and `<Name>Service` in `src/G54.BLL/Services/`. Accept `CancellationToken`. Use `AsNoTracking()` for reads. Enforce ownership/authorization rules here.
5. **Register**: add the service in `G54.BLL/ServiceCollectionExtensions.AddBusinessLogic`.
6. **Controller (Api)**: add `src/G54.Api/Controllers/<Name>Controller.cs` with `[ApiController]`, an explicit root resource route such as `[Route("kpi-definitions")]`, `[Authorize]` unless public, and accurate `ProducesResponseType` attributes. Follow the published contract for existing endpoints. Controllers only call services.
7. **Tests**: add `tests/G54.UnitTests/<Name>ServiceTests.cs`.
8. **Review**: run `./scripts/codereview-backend.ps1` until it passes.

## API Contract (Backend First or Frontend First)

Read `docs/api/API_CONVENTIONS.md` and the feature contract in `docs/api/` before
creating/changing controllers or DTOs. If absent, define
`docs/api/<FEATURE>_CONTRACT.md` first: endpoint methods/routes, permissions,
parameters, DTO fields/nullability, enum wire values, status/error codes and JSON examples.
If the frontend already exists, inspect its typed API service and reconcile against
the agreed contract before implementation; report discrepancies rather than silently
adding aliases or preserving incompatible shapes.

- Resource routes use `/<plural-kebab-case-resource>` with explicit route attributes
  and no API/version prefix. Auth uses `/auth/...`. Update nginx routing and frontend
  auth allowlists for new resources; route changes require updating the shared contract.
- Reuse BLL `ApiResponse<T>`, `ApiResponse.Fail`, and the documented `PagedResult<T>`
  or existing pagination metadata. JSON is camelCase; never expose EF entities.
- Define GUIDs versus display codes, ISO timestamps/date-only values, nullable/required
  fields, decimal precision and enum serialization so TypeScript can match exactly.
- Return correct HTTP statuses, Location for creates, and no body for documented 204
  responses. Ensure any envelope status matches HTTP; `ApiResponse.Ok` defaults to 200.
- Map errors through the existing handler and stable ErrorCodes. Specify validation,
  auth, not-found and conflict behavior. Never report success for a failed write.
- Keep controllers thin; validation, ownership and business rules stay in BLL.
  Preserve authorization, cancellation tokens, and API -> BLL -> DAL dependencies.
- Publish accurate OpenAPI/Swagger metadata and examples for frontend-first or
  backend-first integration. Update the contract, affected client, DTOs and fixtures
  together when the public contract changes.
- Verify routes, JSON envelopes/statuses and invalid/auth/error cases with appropriate
  controller/integration/contract tests. Unit tests of services alone are insufficient
  to confirm the wire contract. Exercise a real frontend/client when available.
- Run `./scripts/codereview-backend.ps1`; state endpoints verified and remaining
  planned/mock-only integration. Matching the contract enables the configured client
  to connect without endpoint rewrites; do not claim untested automatic integration.

## Conventions

- File-scoped namespaces, `sealed` classes where possible, primary constructors for DI.
- Nullable enabled; warnings are errors (`Directory.Build.props`).
- Logging uses `LoggerMessage.Define` (CA1848).
- Application errors surface as `ApiResponse.Fail` via `GlobalExceptionHandler`; document framework/auth error responses explicitly.
- Config comes from `appsettings.json` + environment variables (`Section__Key`).
- SDK pinned by `global.json` to .NET 8.

## Never

- Access `AppDbContext` from controllers.
- Auto-migrate in API startup.
- Hardcode secrets or production URLs.
