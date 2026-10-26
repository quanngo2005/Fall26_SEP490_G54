# Frontend Code Review Standard

Run `./scripts/codereview-frontend.ps1` on Windows or `./scripts/codereview-frontend.sh` on Unix. The command creates `docs/codereview/reports/FRONTEND_REPORT.md` and exits non-zero when an automated check fails.

The report lists, at the top, every blocking failure, warning and skipped check with the `file:line` of each problem, so
the report alone says what to fix. Each check ends as **PASS**, **FAIL** (Gate/Critical/High: blocks merge), **WARN**
(Medium/Low: advisory) or **SKIP** (could not run here, e.g. a tool is missing: it is never counted as passed). The full
output of each check is in `docs/codereview/reports/logs/<ID>.log`. The report covers the `[auto]` checks only. In CI
the report is also shown on the run's Summary page.

## Severity Levels

| Severity | Meaning | Merge policy | Fix deadline |
| --- | --- | --- | --- |
| **Critical** | Directly exploitable: XSS, token/secret leak, auth bypass. | **Blocks merge.** | Immediately |
| **High** | Serious weakness exploitable with moderate effort. | **Blocks merge.** | Before merge |
| **Medium** | Defense-in-depth gap; needs other conditions to exploit. | Merge allowed with tracked issue. | Within current sprint |
| **Low** | Hardening or best-practice deviation. | Merge allowed. | Backlog |
| **Info** | Observation or suggestion. | No action required. | — |

Legend: `[auto]` checked by script, `[manual]` checked by reviewer.

## Automated Gates

- [ ] FE-01 `[auto]` Installed dependencies match `package-lock.json` (`npm ls`; CI installs them with `npm ci`).
- [ ] FE-02 `[auto]` ESLint passes without errors.
- [ ] FE-03 `[auto]` Prettier check passes.
- [ ] FE-04 `[auto]` Unit tests pass in headless mode; coverage is reported in the Metrics section.
- [ ] FE-05 `[auto]` Production build succeeds within configured budgets.

## TypeScript And Angular

- [ ] FE-06 `[manual]` Strict TypeScript remains enabled; avoid `any` and unsafe assertions.
- [ ] FE-07 `[manual]` Components focus on presentation and delegate HTTP/data work to services.
- [ ] FE-08 `[manual]` Subscriptions are finite or cleaned up with Angular lifecycle utilities.
- [ ] FE-09 `[manual]` Feature routes are lazy-loaded when their bundle is non-trivial.
- [ ] FE-10 `[manual]` Templates are accessible by keyboard and use semantic HTML.

## Configuration And UX

- [ ] FE-11 `[auto]` API URLs come from environment files, not components or services.
- [ ] FE-12 `[manual]` New production variables have placeholders and are added to `replace_env.sh`.
- [ ] FE-13 `[manual]` Loading, empty, error, and success states are handled.
- [ ] FE-14 `[manual]` Layout works on phone and desktop widths.
- [ ] FE-15 `[auto]` No debug `console.log`, `console.debug` or `debugger` committed in `src/`.

## Testing And Delivery

- [ ] FE-16 `[manual]` Changed behavior has focused tests.
- [ ] FE-17 `[manual]` API contracts use typed interfaces.
- [ ] FE-18 `[manual]` Docker nginx fallback continues to support Angular routes.
- [ ] FE-19 `[manual]` New runtime dependencies have a documented reason.
- [ ] FE-20 `[manual]` Review verifies the app against a running API, not mocks only.
- [ ] FE-22 `[auto]` Every locale in `localization.json` has the same keys and no empty text.
- [ ] FE-23 `[auto]` No focused tests (`fdescribe`, `fit`), which silently skip every other test.
- [ ] FE-24 `[auto]` (Medium) No skipped tests (`xdescribe`, `xit`, `pending`).
- [ ] FE-25 `[auto]` (Low) ESLint warnings are listed.
- [ ] FE-26 `[auto]` (Low) Production build warnings (budgets, CommonJS, unused files) are listed.
- [ ] FE-27 `[auto]` (Low) Components and services without a `.spec.ts` are listed.
- [ ] FE-28 `[auto]` (Low) No `TODO`/`FIXME`/`HACK` left in source.
- [ ] FE-21 `[manual]` UI follows `frontend/docs/DESIGN.md`; verify desktop, tablet, and mobile layouts, keyboard/focus states, and document intentional deviations.

## Security

### Critical

- [ ] SEC-FE-C01 `[auto]` No secrets, API keys, private keys, or passwords in `src/` or environment files. Frontend code is public; anything shipped is readable.
- [ ] SEC-FE-C02 `[auto]` No `bypassSecurityTrust*` calls (`bypassSecurityTrustHtml`, `...Url`, `...ResourceUrl`, `...Script`, `...Style`) without a documented, reviewed justification comment `// security-reviewed:`.
- [ ] SEC-FE-C03 `[auto]` No direct DOM injection: `innerHTML =`, `outerHTML`, `document.write`, `insertAdjacentHTML`, `eval(`, `new Function(`.
- [ ] SEC-FE-C04 `[manual]` Route guards are UX only; every protected action is also authorized by the API.
- [ ] SEC-FE-C05 `[manual]` Auth tokens are never placed in URLs, query strings, or logs.

### High

- [ ] SEC-FE-H01 `[auto]` `npm audit --omit=dev --audit-level=high` reports no High/Critical vulnerabilities in runtime dependencies.
- [ ] SEC-FE-H02 `[manual]` `[innerHTML]` bindings only render trusted or sanitized content (Angular sanitizer must not be bypassed).
- [ ] SEC-FE-H03 `[manual]` Tokens are stored with the least exposure possible (prefer HttpOnly cookies; if `localStorage` is used, document XSS risk and keep token lifetime short).
- [ ] SEC-FE-H04 `[manual]` HTTP interceptor attaches `Authorization` only to known API resource paths on the configured API origin, never third-party hosts or unrelated asset/page requests.
- [ ] SEC-FE-H05 `[manual]` Redirects after login use an allowlist of internal routes (open redirect prevented).
- [ ] SEC-FE-H06 `[auto]` Production API URL uses a runtime placeholder replaced with the HTTPS API origin; no `http://` in `environment.ts`.

### Medium

- [ ] SEC-FE-M01 `[manual]` nginx sets security headers: `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` (or CSP `frame-ancestors`), `Referrer-Policy`.
- [ ] SEC-FE-M02 `[manual]` Production build has source maps disabled or not publicly served.
- [ ] SEC-FE-M03 `[manual]` Error messages shown to users do not expose stack traces, SQL, or internal ids.
- [ ] SEC-FE-M04 `[manual]` Logout clears tokens, cached user data, and in-memory state.
- [ ] SEC-FE-M05 `[auto]` External links opened with `target="_blank"` include `rel="noopener noreferrer"`.
- [ ] SEC-FE-M06 `[manual]` Forms validate on the client for UX but never rely on client validation for security.

### Low

- [ ] SEC-FE-L01 `[manual]` Third-party scripts/CDNs use Subresource Integrity or are self-hosted.
- [ ] SEC-FE-L02 `[manual]` Sensitive inputs use proper `autocomplete` attributes (`current-password`, `new-password`, `one-time-code`).
- [ ] SEC-FE-L03 `[manual]` Dependencies stay on supported Angular major and patch versions.
- [ ] SEC-FE-L04 `[manual]` nginx hides version (`server_tokens off`).

## Reporting Findings

Report each finding in this format:

```text
[SEVERITY] SEC-FE-XXX <short title>
File: path/to/file.ts:line
Risk: what an attacker can do
Fix: concrete remediation
```

A review passes only when there are **zero Critical and zero High** findings and all `[auto]` checks pass.
