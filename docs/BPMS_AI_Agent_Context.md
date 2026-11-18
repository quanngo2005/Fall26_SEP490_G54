# BPMS – AI Agent Context (as of 25/10/2026)

> Paste at the start of any AI session on BPMS. On conflict with older docs (Report 1 rev 12/10, Report 3 rev 21/10) **this file wins**. ⚠ = not final, ask before relying on it. All documents are in **English**.

## 1. Project
- **BPMS – Banking Employee Work Management and Performance Evaluation System**, SEP490-G54 capstone, web app. Generic single bank, one branch (references: Techcombank, BIDV interviews).
- Team: Ngô Thế Quân (leader, DevOps + overview docs), Nguyễn Tiến Thành (tech lead + AI), Trương Duy Mạnh (dev + QA), Dương Xuân Phú (dev), Nguyễn Tuấn Kiệt (dev).
- Jira `sep490-bpms`, project `GB`. Planning: 5h/day × 7 days/week. Coding phase: 5 weeks from 12/10/2026; code ships with design spec + sequence diagrams.
- Report 3: https://docs.google.com/document/d/1Pa0Qo6Vf8U1EleIWR2EXi63MIRLBnxAS/edit

## 2. Scope
- **Main focus = performance evaluation**; task management is supporting.
- Core chain: Business Objective → KPI Definition → Target Allocation → Work → Result (transaction/CIF code) → Reconciliation with imported data → KPI Contribution (immutable ledger) → Performance Evaluation → Ranking → HR export.
- Principles: traceability (score → evidence/imported transaction); KPI actuals never entered manually; role-specific evaluation profiles.
- **Standalone**: no real-time/API integration. Data in/out only via Excel (.xlsx) fixed templates.
- **2 positions only: Teller, Sales Officer** (individual customers). Credit Officer removed; loans go to a department outside the system.
- Out of scope: Credit Officer/loans/NPL, Delegation, AI scoring/alerts, priority customers, queue system (JCM/VCM), email, self-service forgot-password, employee transfer UC, session management UC, Head Office actor, regional director, mobile.

## 3. Actors (7)
| Actor | Responsibilities |
|---|---|
| System Admin | Accounts (manual or via employee import; deactivate, never delete; reset passwords), permission matrix (Resource×Action×Scope), system parameters, KPI Definitions, WorkTypes, Evaluation Profiles, product multipliers, quality ratio per period, **all data imports (Admin only)**, audit log |
| Teller | Counter transactions; creates own work, submits transaction code; hands customers to Sales Officer; views own KPI; appeals |
| Sales Officer | Serves/sells to individual customers; creates work when approaching a customer (lost deal → Lost); receives handovers; submits transaction/CIF code; views own KPI; appeals |
| Department Manager | Allocates targets to staff, assigns work, approves only work outside WorkType list, evaluates & ranks department, first-level disputes, campaign plans; has a personal KPI separate from department KPI |
| Branch Manager | Objectives, branch/dept targets, approves KPI definitions, campaigns, multipliers, rankings; pre-close, publish, lock period; second-level appeals. Does not adjust employee targets directly |
| QA / Inspector | Risk events (penalty points), reconciliation review, quality flags, QA dashboard |
| HR Officer | Views/exports locked ranking records (xlsx/PDF) |

## 4. Key business decisions
- **KPI sources (23/10):** all results come from Core Banking transaction imports + service-rating imports. Matched results are auto-approved; manual approval only for work outside the WorkType list.
- Employee submits a **transaction or CIF code** per work item; system reconciles with imported data → Matched / Not Matched / In Review.
- **Handover Teller → Sales Officer (23/10):** no approval, no acceptance deadline/SLA. Success identified by transaction/customer code + **Refer ID** column (referrer's employee ID) in the transaction file. Teller and Sales Officer are credited under **two different KPI items**.
- **Targets (23/10):** system auto-splits into month/week/day (replaces manual monthly split + suggested daily).
- **Quality ratio** (weights between groups) set by Admin before each quarterly/annual period; never hard-coded (Teller 40/30/30 is only an example).
- **Product multipliers** follow bank rules, entered by Admin, approved by Branch Manager.
- KPI Definitions created by Admin, approved by Branch Manager; Branch Manager can request a missing KPI while creating an objective.
- **Period-end:** pre-close check (pending In Review results carry to next period after deadline) → Dept ranking → Branch approval → **Publish** (appeal window opens) → **Lock** (snapshot).
- **Disputes:** 2 levels (Dept Manager → Branch Manager). One shared appeal modal for result dispute and score appeal; employee may withdraw.
- **Ranking:** within department + job position; groups too small rank at branch level per position. Tiers by forced distribution (top %); qualitative score has a configurable cap.
- Employee data changes only via import (no Update Employee UC); missing employees are not deleted, only TERMINATED deactivates. Department / Job Position are view-only.
- Sales deposit KPI = new-opening volume, not balance.
- Work Details (SCR-WRK-03) has no Accept/Reject section.
- QA randomly samples 5% of approved results, independent of any approver (25/10).

## 5. Artifact conventions
- **Use cases:** 118 UCs (UC01–UC118, list as of 15/10). No generic "Manage X"; split Create/Update/View/Delete with explicit actors. Descriptions: "As a <actor>, I want to …, so that …".
- **UC diagrams:** draw.io, black-and-white, one A4 portrait page, FCM style (actor → few main UCs, others via visible <<include>>/<<extend>>), compact ellipses, no UC numbers, actor lines attach at UC upper-left. Separate Teller and Sales Officer diagrams.
- **Swimlanes:** UML activity, black-and-white, single start node, one continuous flow, black-box actor interactions only, no text on connectors, no overlapping lines. AS-IS before TO-BE.
- **Context diagram:** BPMS as center circle, actors around, no external systems, one line per data flow, straight/orthogonal connectors.
- **Screen flow:** one diagram per actor, complete; Arial, square boxes; list / details / create-edit are separate screens. Codes: SCR-AUTH, USR, NTF, ADM, ORG, STRAT, CFG, CMP, WRK, REV, PRF, EVL, RSK, APL (106 screens in Screen_Descriptions.md).
- **ERD:** Chen style, black-and-white, cardinality on every relationship; no WorkflowInstance; associative tables for M:N; **no import entities** (imported data maps directly into business tables).

## 6. Tech stack
- Backend: ASP.NET Core 8 Web API, 3-tier (G54.Api, G54.BLL, G54.DAL, G54.DbMigrator), EF Core 8 + PostgreSQL, Redis, JWT Bearer, Serilog, xUnit.
- Frontend: Angular 22, TypeScript 6, Karma/Jasmine, ESLint, Prettier, Husky.
- Docker Compose (postgres, redis, pgadmin, migrator, api, frontend; separate prod file). GitHub Actions: backend-ci, frontend-ci, codeql.

## 7. Known document inconsistencies to fix
- Report 1 still has 3 positions (Credit Officer) → update to 2.
- Report 3 entities list ImportBatch / ImportRowError / ImportedTransaction / ImportedServiceRating (conflicts with ERD decision); NFR-09 depends on ImportBatch ID.
- Report 3 screen list (43) ≠ Screen_Descriptions.md (106); the 106 version is current.
- Handover approval/SLA remnants (removed 23/10): SCR-WRK-05, SLA parameter in SCR-ADM-04, non-screen function #5, MSG16, NFR-11 wording.
- SCR-REV-04 Verification Queue/Decision assumes manual approval of all results → now only non-WorkType work.
- Report 3 still allows Dept Manager Excel import of employee targets; SCR-STRAT-05 manual monthly split vs auto-split.
- BR-39, BR-42 missing; re-check MSG references to BR numbers.

## 8. BR decisions (25/10/2026)
| BR | Decision | Rule / follow-up |
|---|---|---|
| BR-01 | Keep | Requests ≥ configurable amount (e.g. 50M VND) → Teller hands over to Sales Officer. Threshold is a parameter (SCR-ADM-04) |
| BR-02 | Narrowed | Maker ≠ Checker only for work outside WorkType list |
| BR-07 | Rewrite | Teller (referrer) and Sales Officer (processor) credited under different KPI items via transaction/customer code + Refer ID. Missing/wrong Refer ID → credit processor only; row reported as import error for referral part |
| BR-09 | Rewrite | Matched → auto-approved; no data yet → In Review; mismatch → Not Matched (employee fixes code or appeals) |
| BR-13 / BR-45 | Move | To Assumptions section (system storage assumptions) |
| BR-21 | Delete | Also MSG16, handover SLA parameter (SCR-ADM-04), handover part of non-screen function #5 |
| BR-22 | Keep | Importing employee with TERMINATED deactivates account, returns pending work to department pool |
| BR-23 | Delete | Also MSG29 |
| BR-30 | Rewrite | Approved leave does not change targets (total/annual unchanged). Delete MSG25 |
| BR-32 | Rewrite | 5% sampling of approved results for QA; no link to or penalty for approver. Update MSG26 |
| BR-34 | Rewrite | Dept Manager has personal KPI separate from department KPI (dept KPI still rolls up from staff) |
| BR-37 | Delete | |
| BR-38 | Delete | Also MSG19 |
| BR-44 | Delete | Also MSG31; mandatory training → Limitations |
| BR-46 | ⏸ Hold | Waiting to confirm whether the employee file has PENDING_RESIGNATION status |

### Lead rules (BR-25, BR-43) – open
- Lead = potential customer assigned to a Sales Officer in a campaign (usually one work item). BR-25 locks lead + credit to assignee for 7 days; BR-43 returns an unstarted lead to the pool after 72h and records a Missed Lead violation.
- Problem: only employee, transaction and service-rating files are imported → no lead source.
- Options: (1) drop leads; campaigns only generate work from targets; delete BR-25/43, MSG30; (2) lead = work with a CIF, created manually or via Batch Work Generator, keep both rules; (3) add a lead import file (conflicts with 3-file decision).

## 9. Open BR questions
| BR | Current text | Question |
|---|---|---|
| BR-06 / BR-49 | Weights per profile | Confirm weights = Admin-set quality ratio per period |
| BR-10 | Submitted gives no points | Keep; does "Approved" include auto-approval? |
| BR-12 | Campaign deadline + 5-min grace | Relevant if points use transaction date, not submission time? |
| BR-15 | Mid-period target change: freeze/new/recalc | Confirm with auto month/week/day split |
| BR-16 / BR-29 | Reversal entry; hard-lock month at 23:59:59 | Lock per month or per evaluation period (Publish → Lock)? |
| BR-17 / BR-18 | 1-star → alert; rating mapped by employee + transaction code | Keep; alert recipient still Dept Manager? |
| BR-20 / BR-35 | Veto cap; ≥50% per category for Good | Keep? Which tiers (A1–C)? |
| BR-31 | Dispute rejected results within 48h | Still 48h with 2-level (Dept → Branch)? |
| BR-33 | Multipliers 1.5x / 1.0x | Confirm: Admin enters, Branch Manager approves, examples only |
| BR-40a/b | Sales uncapped, Operations capped 150% | Keep; cap configurable? |
| BR-41 | Unit < 70% → individual × 0.8 | Keep? Configurable? |
| BR-48 | Contribution = Σ(Actual/Target × Multiplier) | Confirm with auto-split targets |
| BR-39, BR-42 | Missing | Removed or renumber? |
