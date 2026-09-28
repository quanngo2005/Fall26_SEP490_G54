---
name: security-review
description: Use when reviewing G54 backend or frontend code for security, triaging vulnerabilities, classifying findings as Critical/High/Medium/Low, or fixing SEC-BE/SEC-FE checklist items.
---

# Security Review

Security checklists live in:

- `docs/codereview/CODEREVIEW_BACKEND.md` (IDs `SEC-BE-*`)
- `docs/codereview/CODEREVIEW_FRONTEND.md` (IDs `SEC-FE-*`)

## Workflow

1. Identify changed files (`git diff --name-only` against the base branch).
2. Run the automated checks:
   - Backend: `./scripts/codereview-backend.ps1`
   - Frontend: `./scripts/codereview-frontend.ps1`
3. Walk every `[manual]` item in the matching checklist, starting with Critical, then High.
4. Report each finding in the standard format (below).
5. Fix Critical and High findings before declaring the work done. Medium needs a tracked issue. Low goes to backlog.

## Severity Rules

| Severity | Examples | Policy |
| --- | --- | --- |
| Critical | Hardcoded secret, SQL injection, missing `[Authorize]` on private data, IDOR, disabled JWT validation, plain-text passwords, `bypassSecurityTrustHtml` on user input, `innerHTML =` | Block merge, fix now |
| High | Vulnerable dependency (High/Critical CVE), mass assignment to entity, no rate limit on login, token sent to third-party host, open redirect, sensitive data in logs | Block merge |
| Medium | Missing security headers, Swagger exposed in prod, source maps public, verbose errors, long-lived tokens | Tracked issue |
| Low | Server version header, missing `autocomplete`, unpinned image tags | Backlog |

When unsure between two levels, choose the higher one and explain why.

## Finding Format

```text
[CRITICAL] SEC-BE-C04 Order detail endpoint leaks other users' orders
File: backend/src/G54.BLL/Services/OrderService.cs:42
Risk: Any authenticated user can read any order by guessing its id.
Fix: Filter by current user id in the query and return 404 when not owned.
```

## Backend Rules (ASP.NET Core 8)

- Secrets only from environment variables (`Jwt__Key`, `ConnectionStrings__Postgres`). Never commit real values.
- Use LINQ or `FromSqlInterpolated`; never `FromSqlRaw($"...")`.
- Protected controllers use `[Authorize]`; ownership checks live in BLL services, not controllers.
- Bind requests to DTOs, never to EF entities.
- Hash passwords with `PasswordHasher<T>` or BCrypt.
- Keep `TokenValidationParameters` validation flags `true`.
- Swagger only in Development; HSTS outside Development.
- Never log passwords, tokens, or full request bodies with personal data.

## Frontend Rules (Angular)

- Nothing secret in `src/` or environment files: everything ships to the browser.
- Do not use `bypassSecurityTrust*`. If unavoidable, add `// security-reviewed: <reason>` on the same or previous line and get approval.
- Do not assign `innerHTML`, call `eval`, or use `document.write`.
- The auth interceptor must only add `Authorization` for URLs starting with `environment.apiUrl`.
- Guards are UX only; the API must enforce authorization.
- Production `apiUrl` must be HTTPS or relative (`/api`).
- nginx keeps CSP, `nosniff`, `X-Frame-Options`, `Referrer-Policy`, `server_tokens off`.

## Dependency Vulnerabilities

- Backend: `dotnet list backend/G54.sln package --vulnerable --include-transitive`.
- Frontend: `npm --prefix frontend audit --omit=dev --audit-level=high`.
- Prefer upgrading to a patched minor/patch release. A major upgrade (for example Angular) must pass all gates afterwards.
- Never silence a check by editing the script or the generated report.
