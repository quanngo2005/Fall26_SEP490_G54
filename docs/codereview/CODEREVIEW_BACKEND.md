# Backend Code Review Standard

Run `./scripts/codereview-backend.ps1` on Windows or `./scripts/codereview-backend.sh` on Unix. The command creates `docs/codereview/reports/BACKEND_REPORT.md` and exits non-zero when an automated check fails.

## Severity Levels

| Severity | Meaning | Merge policy | Fix deadline |
| --- | --- | --- | --- |
| **Critical** | Directly exploitable: data breach, auth bypass, RCE, secret leak. | **Blocks merge.** | Immediately |
| **High** | Serious weakness exploitable with moderate effort or insider access. | **Blocks merge.** | Before merge |
| **Medium** | Defense-in-depth gap; exploitable only with other conditions. | Merge allowed with tracked issue. | Within current sprint |
| **Low** | Hardening or best-practice deviation with minimal risk. | Merge allowed. | Backlog |
| **Info** | Observation or suggestion. | No action required. | — |

Legend: `[auto]` checked by script, `[manual]` checked by reviewer.

## Automated Gates

- [ ] BE-01 `[auto]` Solution restores without package errors.
- [ ] BE-02 `[auto]` Release build succeeds with warnings treated as errors.
- [ ] BE-03 `[auto]` All xUnit tests pass.
- [ ] BE-04 `[auto]` `dotnet format --verify-no-changes` passes.
- [ ] BE-05 `[auto]` No committed build output or secrets are introduced.

## Architecture

- [ ] BE-06 `[auto]` Controllers only handle HTTP concerns; controllers never reference `AppDbContext` or `G54.DAL`.
- [ ] BE-07 `[manual]` BLL does not depend on API; DAL does not depend on BLL or API.
- [ ] BE-08 `[manual]` Database schema changes include an EF Core migration and safe rollback.
- [ ] BE-09 `[manual]` DbMigrator remains idempotent and owns migration/seed execution.
- [ ] BE-10 `[manual]` DTOs do not expose persistence entities directly.

## Correctness And Performance

- [ ] BE-11 `[manual]` Async I/O accepts and forwards `CancellationToken` where appropriate.
- [ ] BE-12 `[manual]` Read-only EF queries use `AsNoTracking`; list endpoints avoid N+1 queries and are paginated.
- [ ] BE-13 `[manual]` User input is validated before persistence.
- [ ] BE-14 `[manual]` Error responses use ProblemDetails and do not reveal stack traces.
- [ ] BE-15 `[manual]` New behavior includes focused unit or integration tests.

## Operations

- [ ] BE-16 `[manual]` `/health` is a liveness check; `/health/ready` verifies required dependencies.
- [ ] BE-17 `[manual]` Swagger describes public endpoints and response status codes.

## Security

### Critical

- [ ] SEC-BE-C01 `[auto]` No hardcoded secrets, API keys, private keys, or production connection strings in source (`appsettings.Production.json` must not contain real credentials).
- [ ] SEC-BE-C02 `[auto]` No SQL built by string concatenation/interpolation (`FromSqlRaw`/`ExecuteSqlRaw` with `$"..."` or `+`). Use LINQ or `FromSqlInterpolated`/parameters.
- [ ] SEC-BE-C03 `[manual]` Every endpoint that reads or mutates non-public data has `[Authorize]` (or a policy). Anonymous access requires explicit `[AllowAnonymous]` with justification.
- [ ] SEC-BE-C04 `[manual]` Object-level authorization (IDOR): resource ownership/tenant is verified in BLL before returning or modifying data by id.
- [ ] SEC-BE-C05 `[auto]` JWT validation is never disabled (`ValidateIssuerSigningKey`, `ValidateLifetime`, `ValidateIssuer`, `ValidateAudience` stay `true`).
- [ ] SEC-BE-C06 `[manual]` Passwords are hashed with a strong adaptive algorithm (ASP.NET Identity `PasswordHasher`, BCrypt, Argon2). Never plain text, MD5, or SHA1.
- [ ] SEC-BE-C07 `[auto]` No insecure deserialization (`BinaryFormatter`, `TypeNameHandling.All/Auto`).

### High

- [ ] SEC-BE-H01 `[auto]` Vulnerable NuGet packages are not introduced (`dotnet list package --vulnerable` reports none with High/Critical severity).
- [ ] SEC-BE-H02 `[manual]` Mass assignment prevented: requests bind to dedicated DTOs, never directly to EF entities.
- [ ] SEC-BE-H03 `[manual]` Input validation (FluentValidation/DataAnnotations) covers length, format, and range for all user input.
- [ ] SEC-BE-H04 `[manual]` Logs never contain passwords, tokens, full card numbers, or sensitive personal data.
- [ ] SEC-BE-H05 `[auto]` CORS never combines `AllowAnyOrigin` with credentials; production origins come from configuration.
- [ ] SEC-BE-H06 `[manual]` File uploads validate size, extension, and content type; files are stored outside web root with generated names (path traversal prevented).
- [ ] SEC-BE-H07 `[manual]` Authentication endpoints (login, register, reset password) have rate limiting or lockout.
- [ ] SEC-BE-H08 `[manual]` JWT signing key is at least 256 bits, loaded from environment/secret store, with short access-token lifetime.
- [ ] SEC-BE-H09 `[manual]` Outbound HTTP calls using user-supplied URLs validate against an allowlist (SSRF).

### Medium

- [ ] SEC-BE-M01 `[manual]` Production error responses hide exception details; `DeveloperExceptionPage` only in Development.
- [ ] SEC-BE-M02 `[manual]` Security headers are set for API responses where applicable (`X-Content-Type-Options: nosniff`, HSTS behind TLS).
- [ ] SEC-BE-M03 `[manual]` Swagger UI is disabled or protected in Production.
- [ ] SEC-BE-M04 `[manual]` Redis cache keys do not contain sensitive data; cached user data has expiration.
- [ ] SEC-BE-M05 `[manual]` Database user used by API has least privilege in production (no superuser).
- [ ] SEC-BE-M06 `[manual]` Refresh tokens (if used) are rotated, stored hashed, and revocable.
- [ ] SEC-BE-M07 `[manual]` Seed data does not create default admin accounts with known passwords in production.

### Low

- [ ] SEC-BE-L01 `[manual]` Server/version headers are minimized (`AddServerHeader = false` on Kestrel).
- [ ] SEC-BE-L02 `[manual]` Security-relevant events (login failure, permission denied) are logged with trace id.
- [ ] SEC-BE-L03 `[manual]` Dependencies are kept on supported patch versions.
- [ ] SEC-BE-L04 `[manual]` Docker images run as non-root and use pinned base image tags.

## Reporting Findings

Report each finding in this format:

```text
[SEVERITY] SEC-BE-XXX <short title>
File: path/to/File.cs:line
Risk: what an attacker can do
Fix: concrete remediation
```

A review passes only when there are **zero Critical and zero High** findings and all `[auto]` checks pass.
