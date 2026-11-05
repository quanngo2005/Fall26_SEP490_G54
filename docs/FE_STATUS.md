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
| Target overview (SCR-STRAT-03) | `/target-overview` | 🟡 | BM tree and DM allocation/feedback, wired to `GET /target-assignments` and `/feedback`. "Thiết lập mục tiêu năm" now opens `ImportBranchTargetModalComponent` (SCR-STRAT-04-M1: choose file / preview / result) for users with `target:manage`, wired to the new `POST /target-assignments/import/preview` and `/import/commit` (the modal keeps the chosen file and re-sends it on commit), `GET …/import/template`; reloads the tree on commit. See gaps below |
| Admin | `/user-accounts`, `/roles`, `/role-permissions`, `/sidebar-settings` | ✅ | Admin guard |

## 2. Known gaps

- **Target overview:** "Xuất file" and "Phân bổ cho nhân viên" are still disabled (export and employee allocation screens do not exist). "Thiết lập mục tiêu năm" is wired (2026-11-03) but only covers phase 1 (import branch-year targets); the quarter split and department allocation steps now exist in the setup wizard (see below); the modal's "branch picker" for ADMIN is not built (branch comes from the overview's own department list). The sidebar has a "Phân bổ chỉ tiêu" item (`targetAllocation`) opening `/target-overview` for branch and department managers; the confirmation date now comes from `feedbackAt` (backend added, see `BE_STATUS.md`); "not applicable" (—) is inferred from data, not from KPI-position rules. Checked with unit tests, lint, build and a browser run as the branch manager (see the next bullet).
- **Target allocation / assignment screens (SCR-STRAT-01/02/04/05):** import (04-M1), quarter split (04, `/target-setup/:year` step 2) and department allocation (04-D1, step 3) components exist with unit tests, and the wizard shell opens from the overview's "Tiếp tục thiết lập" button (as of 2026-11-03). Opened in a browser as the branch manager against re-seeded dev data (2026-11-03): the overview lists both departments with the employees of their teams (12 active employees, 2 departments) and shows branch, department and employee values for the whole year and Q1-Q3; step 2 has a "NĂM (TỔNG 4 QUÝ)" column after "CẢ NĂM (FILE)" and is editable while the year is a draft; step 3 (04-D1) renders and is editable; "Nhập chỉ tiêu từ Excel" is always on the overview (disabled once the year is published) and opens the 04-M1 modal. Not exercised: choosing a file and committing an import, publishing (step 4), the DM view, and nothing was compared pixel-by-pixel with Figma. Dev data: run `backend/src/G54.DAL/Scripts/reset_dev_business_data.sql` then the seed to get the 2026 scenario (Q1-Q2 allocated, Q3 half-way, Q4 open); The seed also holds 2025 (all quarters) and 2026 Q1-Q3 down to employees and months for HDV, KH_MOI and DU_NO, with actual progress (2026 Q3 is half-finished: employee rows DRAFT, July and August only, September left to the evaluation sample); The two KPI_* year profiles now start on 2027-01-01, so 2026 evaluations use EP-SALES / EP-TELLER and the evaluation list and form show the same targets as the overview (Sales employee Q3 HDV target 170 = Jul 52 + Aug 56 + Sep 62); 2027 stays empty for import tests. The quarter-split API now returns the status as `DRAFT`/`ACTIVE` as the contract says (it returned `Draft`, which kept step 2 read-only).
- **Work:** progress update, workload and funnel views missing (backend task pending).
- **Handover, appeals, risk events, notifications, personal KPI/performance, HR export:** no screens.
- Backend counterparts and their gaps: see `BE_STATUS.md`.
