# BPMS – Frontend Status (as of 05/11/2026)

Counterpart of [`BE_STATUS.md`](BE_STATUS.md). Legend: ✅ done · 🟡 partial · ❌ missing. "Wired" means the screen calls the real API; a route listed here is lazy-loaded in `frontend/src/app/app.routes.ts`.

Quality gate: `scripts/codereview-frontend.ps1` → **ALL CHECKS PASSED** (lint, prettier, 761 unit tests, production build, i18n key parity).

## 1. Screens

| Area | Route | Status | Notes |
|---|---|---|---|
| Auth | `/login` | ✅ | |
| Home | `/home` | ✅ | |
| Departments / job positions | `/departments`, `/job-positions`, `/department-performance` | ✅ | View-only (organization guard) |
| Employees | `/employees`, `/employee-detail[/:code]` | ✅ | |
| Work (employee / department manager) | `/my-work` | 🟡 | Board with start / complete actions; manager's "Giao việc" dialog (SCR-WRK-02) calls `POST /works`; no accept/reject (not in scope), no progress update yet |
| Work types | `/work-type-detail[/:id]` | ✅ | |
| Work type list (SCR-CFG-02) | `/work-types` | 🟡 | Admin guard; reached from the admin sidebar item "Work types" (new admin default menu, saved configurations must add it in `/sidebar-settings`); list, job position tabs, filters, detail drawer and activate / deactivate wired to the real API; "Create" / "Edit" disabled (screens missing); `SYSTEM_ADMIN` opens it but gets 403 on activate / deactivate (see `WORK_TYPE_CONTRACT.md`) |
| Evaluation profile list, detail and form (SCR-CFG-01, -D1, -M1) | `/evaluation-profiles`, `/evaluation-profiles/:id` | 🟡 | Organization guard (`ORGANIZATION_READ`: system admin, branch manager, department manager); list wired to `GET /evaluation-profiles`; the row button reads "View" (opens the detail) for ACTIVE, INACTIVE, ARCHIVED and expired profiles and "Edit" (opens the form dialog) for the rest; detail wired to `GET /evaluation-profiles/{id}` with approve / reject for `evaluation-profile:approve`; create / edit dialog (`POST` / `PUT`) enabled for `kpi:manage`; only the criteria tab and the previous-version weight comparison exist (tier, multiplier, quality ratio and history tabs show "not available"); a profile already in use only accepts a new name; no sidebar item and no link from SCR-ADM-01 or SCR-PRF-02/03 yet (open by URL); covered by unit specs only, not checked against a running API |
| Review & verify | `/review-verify[/:submissionId]` | ✅ | |
| KPI definitions | `/kpi-definitions` | ✅ | |
| Campaigns | `/campaigns`, `/campaigns/:id` | ✅ | |
| Evaluations | `/evaluations`, `/evaluations/form`, `/branch-performance/evaluations` | 🟡 | Branch period list + lock wired; profile approve/reject UI not checked |
| Target overview (SCR-STRAT-03) | `/target-overview` | 🟡 | BM tree and DM allocation/feedback, wired to `GET /target-assignments` and `/feedback`; see gaps below |
| Appeals | `/appeals/mine`, `/appeals/department`, `/appeals/department/:id` | 🟡 | Employee list with drawer, withdraw and "submit result appeal"; manager/branch queue and detail with the decision window (submission time, product, referrer and score from the API, other fields N/A); all wired to `/appeals` (04/11/2026). `/appeals` redirects to the queue and the guard sends users without `verification:decide` to `/appeals/mine`. Not checked against a running API or a seeded database |
| My targets (SCR-STRAT-03-D1) | `/my-targets` (sidebar "My performance") | 🟡 | Employee's quarterly targets split by month (`GET /target-assignments/mine`, `/employees/{id}/suggested-daily`), one column per KPI, confirm / re-confirm (`POST .../feedback/confirm`), feedback to the manager (`.../feedback/comments`, sent on every KPI target of the quarter), week / day panel from `.../breakdown`. Only quarters the employee has targets in are listed. Unit-tested, not yet run against a seeded database |
| Employee target allocation (SCR-STRAT-05) | `/target-allocation` | 🟡 | DM splits the department quarter target (SUM KPIs) over every employee one by one (a team row is only the total of its employees; saving splits the department level, then each team) via `POST /target-assignments/{id}/allocations`, and adjusts a saved split (reason required) via `PUT` of the same route; monthly split panel from `GET .../breakdown`; opened from the target overview button |
| Admin | `/user-accounts`, `/roles`, `/role-permissions`, `/sidebar-settings` | ✅ | Admin guard |

## 2. Known gaps

- **Target overview:** "Xuất file" and "Thiết lập mục tiêu năm" are disabled (target setup does not exist); "Mục tiêu nhân viên" in the sidebar opens it; the confirmation date now comes from `feedbackAt` (backend added, see `BE_STATUS.md`); "not applicable" (—) is inferred from data, not from KPI-position rules. Only checked against mock data, not a seeded database.
- **My targets (SCR-STRAT-03-D1):** after the manager changes a target the employee had confirmed, the screen shows the "confirm again" banner but not the old → new values (the API returns no previous value and the design marks that variant as an assumption); leave days are not subtracted (BR-30); a comment is stored once per KPI target of the quarter, so the manager's feedback view shows it on each KPI row.
- **Work type list:** filtering, search and paging are done in the browser over at most 100 work types; the work counts need one `GET /works` call per row (and two more per opened drawer). Checked against the seeded local database with `admin@bpms.com` (list, KPI options, work counts, activate / deactivate); the page itself was not opened in a browser, so the layout at tablet and mobile widths and keyboard use are not verified.
- **Target allocation / assignment screens (SCR-STRAT-01/02/04):** missing. **SCR-STRAT-05:** working days ignore holidays/leave (no calendar, so no MSG25 note or daily view); the monthly panel only shows saved targets. Not exercised against a running API.
- **SCR-WRK-02:** no customer/CIF field (not in `POST /works`), work type list is not filtered by the assignee's position, the title is the work type name (the API needs one), and checklist items are sent but not stored by the server yet.
- **Work:** progress update, workload and funnel views missing (backend task pending).
- **Appeals:** N/A until the API has the data: processing deadline, transaction code, CIF, Core Banking comparison and reconciliation status, "impact if accepted"; escalation to the branch director (button and the "escalated" tab) is disabled; deciding a score appeal can keep the score or add points (lifting a risk event or a task deduction is not in the API); there is no screen that files a score appeal; the evaluation period and job position filters need the matching lookups to load; evidence is opened through a blob in a new tab.
- **Handover, risk events, notifications, personal KPI/performance, HR export:** no screens.
- Backend counterparts and their gaps: see `BE_STATUS.md`.
