# BPMS – Frontend Status (as of 03/11/2026)

Counterpart of [`BE_STATUS.md`](BE_STATUS.md). Legend: ✅ done · 🟡 partial · ❌ missing. "Wired" means the screen calls the real API; a route listed here is lazy-loaded in `frontend/src/app/app.routes.ts`.

Quality gate: `scripts/codereview-frontend.ps1` → **ALL CHECKS PASSED** (lint, prettier, 525 unit tests, production build, i18n key parity).

## 1. Screens

| Area | Route | Status | Notes |
|---|---|---|---|
| Auth | `/login` | ✅ | |
| Home | `/home` | ✅ | |
| Departments / job positions | `/departments`, `/job-positions`, `/department-performance` | ✅ | View-only (organization guard) |
| Employees | `/employees`, `/employee-detail[/:code]` | ✅ | |
| Work (employee) | `/my-work` | 🟡 | Board with start / complete actions; no accept/reject (not in scope), no progress update yet |
| Work types | `/work-type-detail[/:id]` | ✅ | |
| Review & verify | `/review-verify[/:submissionId]` | ✅ | |
| KPI definitions | `/kpi-definitions` | ✅ | |
| Campaigns | `/campaigns`, `/campaigns/:id` | ✅ | |
| Evaluations | `/evaluations`, `/evaluations/form`, `/branch-performance/evaluations` | 🟡 | Branch period list + lock wired; profile approve/reject UI not checked |
| Target overview (SCR-STRAT-03) | `/target-overview` | 🟡 | BM tree and DM allocation/feedback, wired to `GET /target-assignments` and `/feedback`; see gaps below |
| Appeals | `/appeals/mine`, `/appeals/department`, `/appeals/department/:id` | 🟡 | Employee list with drawer, withdraw and "submit result appeal"; manager/branch queue and detail with the decision window (submission time, product, referrer and score from the API, other fields N/A); all wired to `/appeals` (04/11/2026). `/appeals` redirects to the queue and the guard sends users without `verification:decide` to `/appeals/mine`. Not checked against a running API or a seeded database |
| Admin | `/user-accounts`, `/roles`, `/role-permissions`, `/sidebar-settings` | ✅ | Admin guard |

## 2. Known gaps

- **Target overview:** "Xuất file", "Thiết lập mục tiêu năm" and "Phân bổ cho nhân viên" are disabled (target setup / allocation screens do not exist); no menu link yet (sidebar "Chỉ tiêu & chiến dịch" still opens `/campaigns`); the confirmation date now comes from `feedbackAt` (backend added, see `BE_STATUS.md`); "not applicable" (—) is inferred from data, not from KPI-position rules. Only checked against mock data, not a seeded database.
- **Target allocation / assignment screens (SCR-STRAT-01/02/04/05):** missing.
- **Work:** progress update, workload and funnel views missing (backend task pending).
- **Appeals:** N/A until the API has the data: processing deadline, transaction code, CIF, Core Banking comparison and reconciliation status, "impact if accepted"; escalation to the branch director (button and the "escalated" tab) is disabled; deciding a score appeal can keep the score or add points (lifting a risk event or a task deduction is not in the API); there is no screen that files a score appeal; the evaluation period and job position filters need the matching lookups to load; evidence is opened through a blob in a new tab.
- **Handover, risk events, notifications, personal KPI/performance, HR export:** no screens.
- Backend counterparts and their gaps: see `BE_STATUS.md`.
