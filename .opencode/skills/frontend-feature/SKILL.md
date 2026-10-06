---
name: frontend-feature
description: Use when adding Angular components, routes, services, interceptors, guards, API calls, frontend environment variables, nginx config, or frontend tests in G54.
---

# Frontend Feature

## Layout

```
frontend/src/
  app/core/          singleton services, interceptors, guards, API clients
  app/shared/        reusable presentational components, pipes
  app/features/<x>/  feature pages (lazy-loaded routes)
  environments/      environment.development.ts (dev), environment.ts (prod placeholders)
```

Stack: Angular 19 standalone components, strict TypeScript, signals, Node 22.

## UI Design System

Before creating or redesigning frontend UI, read `frontend/docs/DESIGN.md`. Treat it as the canonical source for brand, color, typography, spacing, elevation, shapes, and component guidance. Keep responsive behavior and accessibility intact; document intentional design deviations in the change.

## Steps To Add A Feature

1. Generate: `npx ng g c features/<name>/<name>-page` (standalone by default).
2. Add a lazy route in `app.routes.ts` with `loadComponent`.
3. Put HTTP calls in a service under `app/core/` using `inject(HttpClient)` and `environment.apiUrl`. Type responses with interfaces.
4. Model loading, empty, error, and success states in the component (signals).
5. Add a `*.spec.ts` using `HttpTestingController`.
6. Run `./scripts/codereview-frontend.ps1` until it passes.

## Environment Variables

To add a deployment variable (for example `FEATURE_X_URL`):

1. `environment.development.ts`: real dev value.
2. `environment.ts`: placeholder `'__FEATURE_X_URL__'`.
3. `scripts/replace_env.sh`: add `-e "s|__FEATURE_X_URL__|${FEATURE_X_URL}|g"` with a default.
4. `docker/docker-compose.yml` frontend `environment:` and `docker/.env.example`.

## Security

Load the `security-review` skill. In short: no secrets in `src/`, no `innerHTML =`, no `bypassSecurityTrust*`, interceptor sends tokens only to `environment.apiUrl`.

## Never

- Hardcode API URLs in components or services.
- Leave `console.log` in `src/`.
- Use `any` without justification.
