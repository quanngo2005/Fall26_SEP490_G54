# BPMS – AI Agent Context (as of 25/10/2026, incl. BR decisions)

> Paste this at the start of any AI session working on BPMS. Where this file conflicts with older docs (Report 1 rev 12/10, Report 3 rev 21/10), **this file wins**. Items marked ⚠ are not final — ask before relying on them.

## 1. Project
- **BPMS – Banking Employee Work Management and Performance Evaluation System**. Capstone SEP490-G54, web app. All documents in **English**.
- Team: Ngô Thế Quân (leader, QuanNT – DevOps + overview docs), Nguyễn Tiến Thành (tech lead + AI), Trương Duy Mạnh (dev + QA), Dương Xuân Phú (dev), Nguyễn Tuấn Kiệt (dev).
- Jira site `sep490-bpms`, project `GB`. Planning basis 5h/day × 7 days/week. Coding phase: 5 weeks from 12/10/2026; code must ship together with design spec + sequence diagrams.
- Reference banks: Techcombank, BIDV (interviews). Generic single bank, one branch, general structure.
- Report 3 source: https://docs.google.com/document/d/1Pa0Qo6Vf8U1EleIWR2EXi63MIRLBnxAS/edit

## 2. Focus & scope
- **Main focus = performance evaluation.** Task management is kept but is supporting, not the core.
- Core chain: Business Objective → KPI Definition → Target Allocation → Work → Result (transaction/CIF code) → Reconciliation with imported data → KPI Contribution (immutable ledger) → Performance Evaluation → Ranking → HR export.
- Principles: traceability (score → evidence/imported transaction), KPI actuals never entered manually, role-specific evaluation profiles.
- **Standalone system**, no real-time/API integration. Data in/out only by Excel (.xlsx) with fixed templates.
- **2 job positions only: Teller, Sales Officer** (individual customers). Credit Officer fully removed; loans go to a department outside the system.
- Out of scope: Credit Officer/loans/NPL, Delegation, AI scoring/alerts, priority customers, queue system (JCM/VCM), email, self-service forgot-password, employee transfer UC, session management UC, Head Office actor, regional director, mobile.

## 3. Actors (7)
| Actor | Key responsibilities |
|---|---|
| System Admin | Accounts (create manually or via employee import; deactivate, never delete; reset passwords), roles/permission matrix (Resource×Action×Scope), system parameters, KPI Definitions, WorkTypes, Evaluation Profiles, product multipliers, quality ratio per period, **all data imports (Admin only)**, audit log |
| Teller | Counter transactions; creates own work, submits transaction code; hands customers over to Sales Officer; views own KPI; appeals |
| Sales Officer | Serves/sells to individual customers; creates work when approaching a customer (lost deal → Lost); receives handovers; submits transaction/CIF code; views own KPI; appeals |
| Department Manager | Allocates targets to staff, assigns work, approves only work outside WorkType list, evaluates & ranks department, first-level dispute handling, campaign plans |
| Branch Manager | Objectives, branch/dept targets, approves KPI definitions, campaigns, multipliers and rankings; pre-close, publish, lock period; second-level appeals. Does not adjust employee targets directly |
| QA / Inspector | Risk events (penalty points), reconciliation review, quality flags, own QA dashboard |
| HR Officer | Views/exports locked ranking records (xlsx/PDF) |

## 4. Key business decisions (latest)
- **KPI sources (23/10):** all KPI results come from Core Banking transaction imports + service-rating imports. No manual manager approval of results; matched results are auto-approved. Manual approval only for work whose type is outside the WorkType list.
- Employee submits a **transaction code or CIF code** for each work item; system reconciles against imported data (Matched / Not Matched / In Review).
- **Handover Teller → Sales Officer (23/10):** no approval step, no acceptance deadline/SLA — once handed over it must be accepted. Successful handover is identified by transaction/customer code + a **Refer ID** column (referrer's employee ID) in the imported transaction file. Teller and Sales Officer each get credit under **two different KPI items**.
- **Targets (23/10):** system auto-splits targets into month / week / day. (Replaces earlier "Dept Manager splits monthly manually + suggested daily".)
- **Quality ratio** (weight between groups) is set by System Admin before each quarterly/annual period. Weights are never hard-coded (Teller 40/30/30 is only an example).
- **Product multipliers** follow bank internal rules, entered by System Admin, approved by Branch Manager.
- KPI Definitions created by System Admin, approved by Branch Manager; Branch Manager can request a missing KPI while creating an objective.
- **Period-end:** pre-close check (pending In Review results carried to next period after deadline) → Dept ranking → Branch approval → **Publish** (appeal window opens) → **Lock** (snapshot). Disputes 2-level: Dept Manager → Branch Manager. One shared appeal modal for result dispute and score appeal; employee may withdraw.
- Ranking: within department + job position; groups too small rank at branch level per position. Tiers by forced distribution (top %); qualitative score has configurable cap.
- Employee data changes only via import (no Update Employee UC); missing employees not deleted, only TERMINATED deactivates. Department / Job Position view-only.
- Sales deposit KPI measured by new-opening volume, not balance.
- Work Details (SCR-WRK-03) has no Accept/Reject section.
- Department Manager has a personal KPI of their own, separate from the department KPI (25/10).
- QA random 5% sampling of approved results continues, independent of any approver (25/10).

## 5. Artifacts & conventions
- **Use cases:** 118 UCs (UC01–UC118, list as of 15/10). No generic "Manage X" — split Create/Update/View/Delete with explicit actors. Descriptions use "As a <actor>, I want to …, so that …".
- **UC diagrams:** draw.io, black-and-white, one A4 portrait page, FCM style (actor → few main UCs, others via visible <<include>>/<<extend>>), compact ellipses, no UC numbers, actor lines attach at UC upper-left. Separate Teller and Sales Officer diagrams.
- **Swimlanes:** UML activity style, black-and-white, English, single start node, one continuous flow, black-box actor interactions only (no system internals), no text on connectors, no overlapping lines. Analyze AS-IS before TO-BE.
- **Context diagram:** BPMS as center circle, actors around, no external systems, one line per data flow, straight/orthogonal connectors only.
- **Screen flow:** one diagram per actor, complete; Arial, square boxes; list / details / create-edit are separate screens (one job per screen). Codes: SCR-AUTH, USR, NTF, ADM, ORG, STRAT, CFG, CMP, WRK, REV, PRF, EVL, RSK, APL (106 screens in Screen_Descriptions.md).
- **ERD:** Chen style (rectangles/diamonds), black-and-white, cardinality on every relationship; no WorkflowInstance; associative tables as M:N; **no import entities** — imported data maps directly into business tables.

## 6. Tech stack
- Backend: ASP.NET Core 8 Web API, 3-tier (G54.Api, G54.BLL, G54.DAL, G54.DbMigrator), EF Core 8 + PostgreSQL, Redis, JWT Bearer, Serilog, xUnit.
- Frontend: Angular 22, TypeScript 6, Karma/Jasmine, ESLint, Prettier, Husky.
- Docker Compose (postgres, redis, pgadmin, migrator, api, frontend; separate prod file). GitHub Actions: backend-ci, frontend-ci, codeql.

## 7. Known document inconsistencies to fix
- Report 1 still describes 3 positions (Credit Officer) — must be updated to 2 positions.
- Report 3 entities still list ImportBatch / ImportRowError / ImportedTransaction / ImportedServiceRating (conflicts with ERD decision); NFR-09 depends on ImportBatch ID.
- Report 3 screen list (43) ≠ Screen_Descriptions.md (106) — the 106-screen version is current.
- SCR-WRK-05 (handover approval), SCR-ADM-04 handover SLA parameter, non-screen function #5 (Handover SLA monitor), MSG16, NFR-11 wording all assume handover approval/SLA → removed 23/10.
- SCR-REV-04 Verification Queue/Decision assumes manual approval of all results → now only for non-WorkType work.
- Report 3 still allows Excel import of employee targets by Dept Manager; SCR-STRAT-05 manual monthly split vs auto-split.
- BR-39, BR-42 missing; MSG references to BR numbers to be re-checked.

## 8. BR decisions (25/10/2026)
| BR | Decision | New rule / follow-up |
|---|---|---|
| BR-01 | Keep, threshold-based | Requests ≥ configurable amount (e.g. 50M VND) → Teller hands over to Sales Officer. Threshold is a system parameter (SCR-ADM-04). |
| BR-02 | Narrowed | Maker ≠ Checker applies only to work outside the WorkType list (manual approval). |
| BR-07 | Rewrite | Teller (referrer) and Sales Officer (processor) credited under two different KPI items, identified by transaction/customer code + Refer ID column in the transaction file. Missing/wrong Refer ID → credit only to processor; row reported as import error for the referral part. |
| BR-09 | Rewrite | Matched with imported transaction → auto-approved. No data yet → In Review. Mismatch → Not Matched; employee fixes the code or appeals. |
| BR-13 / BR-45 | Move | Become system storage assumptions (Assumptions section), not business rules. |
| BR-21 | Delete | Also delete MSG16, handover SLA parameter in SCR-ADM-04, handover part of non-screen function #5. |
| BR-22 | Keep | Importing an employee with status TERMINATED deactivates the account and returns pending work to the department pool. |
| BR-23 | Delete | Also delete MSG29. |
| BR-30 | Rewrite | Approved leave does not change targets; total and annual targets stay the same. Delete MSG25. |
| BR-32 | Rewrite | System still samples 5% of approved results for QA re-verification; no link to or penalty for the approver. Update MSG26. |
| BR-34 | Rewrite | Department Manager has own personal KPI, separate from the department KPI (department KPI still rolls up from staff). |
| BR-37 | Delete | |
| BR-38 | Delete | Also delete MSG19. |
| BR-44 | Delete | Also delete MSG31; mandatory training goes to Limitations. |
| BR-46 | ⏸ On hold | Waiting to confirm whether the employee file has a PENDING_RESIGNATION status. |

### Lead rules (BR-25, BR-43) – open
- A lead = a potential customer assigned to a Sales Officer in a campaign (usually one work item). BR-25 locks the lead and its credit to the assignee for 7 days; BR-43 returns an unstarted lead to the common pool after 72h and records a Missed Lead violation.
- Problem: BPMS imports only employee, transaction and service-rating files, so there is no lead source.
- Options: (1) drop the lead concept, campaigns only generate work from targets — delete BR-25/43 and MSG30; (2) lead = work with a CIF, created manually or via Batch Work Generator, keep both rules; (3) add a lead import file (conflicts with the 3-file decision).

## 9. Remaining open BR questions
| BR | Current text (short) | Conflict / question |
|---|---|---|
| BR-10 | Submitted gives no points | Keep; confirm "Approved" includes auto-approval |
| BR-12 | Campaign deadline + 5-min grace | Still relevant if points come from transaction date, not submission time? |
| BR-15 | Mid-period target change: freeze/new/recalc | Confirm with auto month/week/day split |
| BR-16 / BR-29 | Reversal entry; hard-lock month at 23:59:59 | Lock is per month or per evaluation period (Publish → Lock flow)? |
| BR-20 / BR-35 | Veto cap; ≥50% per category for Good | Keep? Which tiers (A1–C)? |
| BR-31 | Dispute rejected results within 48h | Window still 48h? Now 2-level (Dept → Branch) |
| BR-33 | Multipliers 1.5x / 1.0x | Confirm: Admin enters, Branch Manager approves, examples only |
| BR-40a/b | Sales uncapped, Operations capped 150% | Keep; cap configurable? |
| BR-41 | Unit < 70% → individual × 0.8 | Keep? Configurable? |
| BR-06 / BR-49 | Weights per profile | Confirm weights = quality ratio set by Admin per period |
| BR-48 | Contribution = Σ(Actual/Target × Multiplier) | Confirm formula with auto-split targets |
| BR-17 / BR-18 | 1-star → alert; rating mapped by employee + transaction code | Keep; alert recipient still Dept Manager? |
| BR-39, BR-42 | Missing | Removed or renumber? |
