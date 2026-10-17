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
- Critical and High security findings block completion; fix them before finishing.
- Run the applicable code-review script before declaring work complete.
- Do not edit generated reports manually or weaken review scripts to make them pass.

- For every frontend UI change, load `frontend-feature`, follow its Localization section, and use typed `{{localization.section.key}}` template bindings with no padding inside braces. Localize labels, placeholders, accessibility text, validation messages, toasts, and confirmation dialogs in both Vietnamese and English.
- Keep translation dictionaries under `translations.vi` and `translations.en`; `formats` contains formatting configuration only. Verify identical key sets and live language switching; do not hide malformed dictionaries with casts or merges from `formats`.
<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **Fall26_SEP490_G54** (3082 symbols, 7235 relationships, 135 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/Fall26_SEP490_G54/context` | Codebase overview, check index freshness |
| `gitnexus://repo/Fall26_SEP490_G54/clusters` | All functional areas |
| `gitnexus://repo/Fall26_SEP490_G54/processes` | All execution flows |
| `gitnexus://repo/Fall26_SEP490_G54/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
