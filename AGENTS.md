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
| UI text, templates, localization dictionaries, language switching | `frontend-feature` (Localization section) |
| Running review scripts, reports | `code-review` |
| Security review, Critical/High/Medium/Low findings | `security-review` |
| Docker, run scripts, nginx, deployment | `deploy` |

## Rules

- Preserve dependency direction: API -> BLL -> DAL.
- Never put business logic in controllers or access `AppDbContext` from controllers.
- Every schema change requires an EF Core migration; deployment migrations run through DbMigrator.
- Never hardcode deployment URLs or secrets. Add frontend placeholders to both environment files and `replace_env.sh`.
- For frontend API clients and backend endpoints, follow `docs/api/API_CONVENTIONS.md` and a shared feature contract in `docs/api/` before implementation, regardless of which side is built first. Match routes, DTOs, envelopes, identifier/enum types, pagination, and errors; do not conceal failed API calls or empty responses with automatic mock success.
- Critical and High security findings block completion; fix them before finishing.
- Run the applicable code-review script before declaring work complete.
- Do not edit generated reports manually or weaken review scripts to make them pass.

## After Every Code Change

Run these automatically, without waiting to be asked, before reporting the task as done. They are the same scripts CI runs (`.github/workflows/*-ci.yml`), so a local pass means CI will pass.

1. **Run CI locally** for each side you touched:
   - `frontend/**` changed → `./scripts/codereview-frontend.ps1`
   - `backend/**` changed → `./scripts/codereview-backend.ps1`
   - Both changed → run both.
2. **Fix and rerun** until the generated report (`docs/codereview/reports/FRONTEND_REPORT.md` / `BACKEND_REPORT.md`) ends with `## ALL CHECKS PASSED`. Fix the code; never weaken a check.
3. **Code review**: load the `code-review` skill (plus `security-review`), walk the `[manual]` items for the changed files, and report findings with `file:line`, severity, risk, and fix. Critical/High findings block completion.
4. **If a PR exists**, check its CI after pushing (`gh run list --branch <branch>`; `gh run view <id> --log-failed` on failure) and fix any failing job the same way.
5. Report what passed, what was fixed, and anything not verified (e.g. no running API, UI not checked at all widths).

If a script cannot run in your environment (missing PowerShell, execution policy, sandbox), say so, run its gates directly (frontend: `npx eslint .`, `npx prettier --list-different .`, `npm test -- --watch=false --code-coverage`, `npx ng build`; backend: `dotnet build`, `dotnet test`), and state that the report was not generated.
- For every frontend UI change, load `frontend-feature`, follow its Localization section, and use typed `{{localization.section.key}}` template bindings with no padding inside braces. Localize labels, placeholders, accessibility text, validation messages, toasts, and confirmation dialogs in both Vietnamese and English.
- Keep translation dictionaries under `translations.vi` and `translations.en`; `formats` contains formatting configuration only. Verify identical key sets and live language switching; do not hide malformed dictionaries with casts or merges from `formats`.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **Fall26_SEP490_G54** (3293 symbols, 6892 relationships, 144 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "prod"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "prod" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/Fall26_SEP490_G54/context` | Codebase overview, check index freshness |
| `gitnexus://repo/Fall26_SEP490_G54/clusters` | All functional areas |
| `gitnexus://repo/Fall26_SEP490_G54/processes` | All execution flows |
| `gitnexus://repo/Fall26_SEP490_G54/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
