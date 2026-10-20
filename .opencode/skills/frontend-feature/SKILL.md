---
name: frontend-feature
description: Use when adding or changing G54 Angular components, HTML templates, UI text, localization.json, Vietnamese/English language switching, routes, services, interceptors, guards, API calls, frontend environment variables, nginx config, or frontend tests.
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

For every user-facing text or template change, follow the Localization section below. All UI copy must use the project's typed `localization` object, with matching Vietnamese and English keys.
Before implementing API calls, follow the API Contract section below, including when the backend has not been implemented yet.

1. Generate: `npx ng g c features/<name>/<name>-page` (standalone by default).
2. Add a lazy route in `app.routes.ts` with `loadComponent`.
3. Put HTTP calls in a service under `app/core/` using `inject(HttpClient)` and `environment.apiUrl`. Type responses with interfaces.
4. Model loading, empty, error, and success states in the component (signals).
5. Add a `*.spec.ts` using `HttpTestingController`.
6. Run `./scripts/codereview-frontend.ps1` until it passes.

## API Contract (Frontend First or Backend First)

Read `docs/api/API_CONVENTIONS.md` and the feature contract in `docs/api/` before
creating or changing an API client. If no feature contract exists, define
`docs/api/<FEATURE>_CONTRACT.md` first with endpoint methods/routes, permissions,
parameters, DTO fields/nullability, enum wire values, status/error codes and JSON examples.
Mark planned endpoints explicitly; inspect existing backend routes before choosing paths.

- Use root resource endpoints `/<plural-kebab-case-resource>`, without API/version prefixes.
  `environment.apiUrl` is the separate backend origin without a trailing slash;
  append `/<resource>` once. nginx routes by Host, not Accept headers. Configure
  FRONTEND_HOST, API_HOST, API_URL, FRONTEND_ORIGIN/CORS and CSP together; keep the auth allowlist synchronized.
- Build typed HttpClient services immediately, even for frontend-first work.
  Keep API models separate from presentation models; use explicit adapters.
- Match backend `ApiResponse<T>` and the documented pagination shape. Do not define
  competing response wrappers per feature or assume raw DTOs when the API wraps `data`.
- Preserve backend identifier types, enum serialization and ISO date/time values.
  Display labels/codes are not wire IDs. UI localization never changes payload values.
- Real API is the default. Development mocks, if needed, are separate explicitly
  selected adapters using the same contract. When backend is ready at the configured
  API base, the real client works without rewriting component logic or endpoint paths.
- Never use `catchError` to turn a failed write/auth request into mock success, or
  replace a valid empty response with sample records. Render actual errors and empty states.
- Test method, URL, query, payload, response envelope, empty data and error behavior
  with HttpTestingController. Verify a running backend when available; report endpoints
  that remain planned/mock-only instead of claiming they are integrated.
- Update the contract and both affected sides together for contract changes. Run
  `./scripts/codereview-frontend.ps1` before completion.

## Localization

Templates use the typed dictionary exposed by `LocalizedComponent`:

```html
<h1>{{localization.login.title}}</h1>
<input [placeholder]="localization.login.passwordPlaceholder" />
<button [attr.aria-label]="localization.common.close">
  {{localization.common.close}}
</button>
<span>{{saving() ? localization.kpi.saving : localization.kpi.saveConfiguration}}</span>
```

The canonical dictionary is `frontend/src/app/core/i18n/localization.json`.
`translations.vi` and `translations.en` must have identical nested key sets and string leaves.
`formats.vi` and `formats.en` contain only date, dateTime, currency, currencySymbol,
thousandSeparator, and decimalSeparator settings. Never put UI copy in `formats`.

### Required workflow

1. Read `AGENTS.md`, affected templates/components, and current i18n service/types before editing. Run the required GitNexus upstream impact analysis on affected symbols.
2. Inventory visible text, placeholders, tooltips, aria-labels, validation errors, loading/empty/error states, success messages, toasts, and confirmation dialogs.
3. Add meaningful feature-scoped keys to both locale dictionaries. Provide real translations; do not copy Vietnamese into English as a substitute for translation.
4. Expose the dictionary through `LocalizedComponent` or the existing typed reactive service. Read the current language in a getter/computed value; never take a one-time dictionary snapshot during component construction.
5. Use `{{localization.section.key}}` with no padding inside braces and property/attribute bindings in HTML. Keep the compact Angular Prettier plugin enabled so formatting preserves this convention. Do not add translate pipe bindings, hardcoded localized strings, or template calls to translation methods.
6. Component-generated notifications must come from the dictionary. For variable messages, use named placeholders and the service interpolation API; never concatenate translated fragments when word order differs across locales. Store message keys/parameters where messages must update after a language switch.
7. Preserve stable navigation IDs, enum values, API payloads, option values, names, codes, and user-entered/business data. Translate display labels, not identifiers. Do not infer UI state from a translated label.
8. Keep one canonical dictionary source. Do not leave a stale public copy of localization.json; investigate consumers before removing or synchronizing duplicates.
9. Verify locale key parity, missing/empty values, named placeholder parity, and absence of translation groups under formats. Add meaningful regression tests for live language changes in rendered templates, accessibility text, and messages.
10. Run `./scripts/codereview-frontend.ps1`, read its generated report, and resolve failures. Check the rendered UI in VI and EN at desktop/tablet/mobile sizes and keyboard/focus behavior. Report any check that could not be performed explicitly.

### Completion report

State the components covered, dictionary/type changes, language-switch regression results,
build/review results, and any remaining untranslated UI or blocked checks.

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
- Hardcode UI copy in templates or component logic, or introduce new `translate` pipe usage instead of `{{ localization.section.key }}`.
- Use `as unknown as` or a broad string index signature to silence missing localization feature keys.
- Merge translations out of `formats` to work around an incorrectly structured dictionary.
- Claim localization success merely because a build passes; verify language switching and translated UI coverage.
- Weaken review scripts or manually edit generated reports to pass validation.
