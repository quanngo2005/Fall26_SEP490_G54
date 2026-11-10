# GB-337 FE brief — Campaign Plan Approval + Detail polish (frontend only)

Working dir: `D:\Code\Fall26_SEP490_G54`, branch `feat-YearKPI-Import`. Touch **only `frontend/**`** (plus `docs/FE_STATUS.md`). Do not touch backend, contract, or the existing uncommitted files. Follow `AGENTS.md` and load the `frontend-feature` skill (Localization section) before starting.

API contract (already implemented, do not change): `docs/api/CAMPAIGN_CONTRACT.md`. Existing FE: `frontend/src/app/features/campaigns/`, `frontend/src/app/core/campaign.service.ts`, `campaign.models.ts`, strings in `frontend/src/app/core/i18n/localization.json` (+ `i18n.models.ts`).

Design references (open these images first; they are the visual source of truth, 1280px wide):
- `docs/figma-gb337/SCR-CMP-02-approval.png` — Task 1 (approval screen)
- `docs/figma-gb337/SCR-CMP-01-D1-detail.png` — Task 2 (detail; the KPI table, edit button, history tab and creator/approver rows in it are out of scope)
- `docs/figma-gb337/SCR-CMP-01-M1-create.png` — Task 3 (create modal; KPI rows, host department, draft save are out of scope)

Figma source: file "Design SEP", page "Updated", frames `2634:3602`, `2634:3220`, `2634:2786`. Match layout, spacing and hierarchy from the images using the existing design tokens, not raw pixel values.

## Rules from the `frontend-feature` skill (mandatory)

- Stack: Angular 19 standalone components, strict TypeScript, signals, Node 22. Pages live in `app/features/campaigns/`; HTTP calls only in `app/core/campaign.service.ts` via `inject(HttpClient)` and `environment.apiUrl`, with typed interfaces in `campaign.models.ts`. Never hardcode API URLs. No new environment variables are needed.
- Read `frontend/docs/DESIGN.md` before writing any UI (colors, typography, spacing, elevation, shapes, buttons, inputs, status badges, tables, modals). Reuse the shared styles in `app/shared/ui/` (`ui-controls.scss`, `workspace-page.scss`, `modal.scss`, `display.ts`) and the layout components, as `campaign-list` / `campaign-detail` do. Keep responsive behaviour and accessibility (labels, roles, `aria-*`, keyboard); note any intentional design deviation in the final report.
- New page: standalone component with a lazy route via `loadComponent` in `app.routes.ts`. Model loading, empty, error and success states with signals.
- Each new/changed service and component gets a `*.spec.ts`; the service spec uses `HttpTestingController`.
- Security (`security-review` skill): no secrets in `src/`, no `innerHTML =`, no `bypassSecurityTrust*`, no `console.log`, no `any` without justification, tokens only go to `environment.apiUrl` (use the existing interceptor, do not add headers by hand).
- Do not use `ng g` (no shell); create the files by hand matching the sibling components' structure.

## Task 1 — SCR-CMP-02 Plan Approval screen (new, main work)

- Route `campaigns/approval` in `app.routes.ts`, declared **before** `campaigns/:id`, `authGuard`, title `Duyệt kế hoạch chiến dịch`. New standalone component `campaign-approval.component.{ts,html,scss,spec.ts}` extending `LocalizedComponent`, using `app-sidebar` / `app-header` like `campaign-detail`.
- Layout (Figma): breadcrumb `Trang chủ / Chiến dịch / Duyệt kế hoạch`, H1 + subtitle. Two columns:
  - Left card "Danh sách chờ duyệt" with count badge, search box (client or `search` param), list of campaigns loaded with `CampaignService.list({ status: 'PLANNED' })`. Each item: code + name, objective code, "Chờ duyệt" badge, updated date. Selected item highlighted; optional `?id=` query param preselects.
  - Right top card "Hồ sơ thẩm định": code – name, link "Mở chi tiết đầy đủ" → `/campaigns/:id`, objective, period, participants count, work-type chips (from `CampaignService.detail(id)`).
  - Right bottom card "Quyết định phê duyệt": textarea note, buttons **Từ chối** (`REJECT`, note required) and **Duyệt kế hoạch** (`APPROVE`, note optional), hint text.
- **Omit** what the backend does not provide: "Trả lại chỉnh sửa", approved-budget field, KPI-allocation table, "Người lập", "so với chỉ tiêu phòng". Do not fake data.
- Add `decide(id, { decision: 'APPROVE' | 'REJECT', note })` to `CampaignService` → `POST /campaigns/{id}/decision`, plus `CampaignDecisionPayload` / `CampaignDecisionResult` in `campaign.models.ts` (fields per contract §3.4). Disable both buttons unless `detail.canApprove`. On success remove the item from the list, select the next, show a notice. Surface API errors (e.g. `MAKER_CANNOT_BE_CHECKER`, 409 not PLANNED) with the server message; never mask failures.
- Navigation: add a "Duyệt kế hoạch" entry point in the campaign list/detail header, visible only when permitted (look at `navigation.service.ts` and how `campaign:manage` is checked elsewhere; backend requires `campaign:manage`).

## Task 2 — Campaign detail polish (SCR-CMP-01-D1), only with data we have

- In `campaign-detail.component.html`: when `c.canApprove` show a "Duyệt kế hoạch" button linking to `/campaigns/approval?id=<id>`; show participant initials avatars (max 4 + "+N") in the participants stat card; work-type chips keep `code` in mono. Keep tabs Overview/Participants only. No KPI table, edit button, history tab or "người lập/duyệt".

## Task 3 — Create modal (SCR-CMP-01-M1)

No functional change. The contract still requires `code`, and KPI rows, "phòng ban chủ trì", draft save are not supported by the API. Only fix visual drift if trivial; otherwise leave unchanged and say so.

## Localization (mandatory)

Typed `{{localization.campaign.approval.xxx}}` bindings, no padding inside braces. Add the same keys under both `translations.vi` and `translations.en` in `localization.json` (`formats` holds formatting config only; never merge or cast it into translations) and the matching types in `i18n.models.ts`. Key sets must be identical in vi and en. Localize labels, placeholders, aria-labels, validation messages, toasts/notices and confirmation dialogs; no hardcoded text, including route `title`s where the app already localizes them. Verify live language switching (vi ⇄ en) updates the new screen.

## Done when

- `./scripts/codereview-frontend.ps1` ends with `## ALL CHECKS PASSED` (eslint, prettier, tests with coverage, `ng build`). Never weaken a check.
- Unit specs for the new component and `CampaignService.decide`, covering approve, reject (note required), error path, empty list, `canApprove=false`.
- `docs/FE_STATUS.md` updated (Campaigns row + gaps: no "return for edit", no KPI table/history, pending BE).
- Final report: files changed, what was exercised, what was not.
