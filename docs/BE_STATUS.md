# BPMS – Backend Status (as of 25/10/2026)

Compared against [`BPMS_AI_Agent_Context.md`](BPMS_AI_Agent_Context.md) (25/10, BR decisions). Legend: ✅ done · 🟡 partial · ❌ missing · ⚠ built but conflicts with the 25/10 decisions.

Quality gate: `scripts/codereview-backend.ps1` → **ALL CHECKS PASSED** (build, tests, format, 0 Critical, 0 High).

## 1. Core chain

| # | Step | Status | Backend today | Gap vs 25/10 context |
|---|---|---|---|---|
| 1 | Business Objective | 🟡 | Entities `BusinessObjective`, `ObjectiveKpi`; used inside `CampaignService` | No Objective CRUD API for Branch Manager |
| 2 | KPI Definition | 🟡 | `KpiDefinitionController`, `KpiMappingController`, `WorkTypeController` | Check approval flow: Admin creates → Branch Manager approves |
| 3 | Target Allocation | ✅ | `TargetAssignmentController`, `TargetAllocationService` | Unit-tested; not yet run against Postgres |
| 4 | Work | 🟡 | `WorksController`, `WorkOverdueBackgroundService`, campaign batch-generate work | Handover API missing; non-WorkType work approval (BR-02 maker ≠ checker) to confirm |
| 5 | Result (transaction / CIF code) | ⚠ | `ResultSubmissionsController` + `EvidenceController` (file evidence) | Submission should carry a transaction/CIF code, not evidence files |
| 6 | Reconciliation with imported data | ❌ | — | No Matched / Not Matched / In Review; no auto-approve (BR-09) |
| 7 | KPI Contribution ledger | ⚠ | `VerificationService` writes `WorkResultItem` → `ActualProgress` on **manual** approval | Must be driven by reconciliation; ledger must be immutable (reversal entries, BR-16) |
| 8 | Performance Evaluation | 🟡 | `PerformanceEvaluationsController` (calculate), `PerformanceScoreFormula`, `EvaluationProfilesController` | Product multipliers, quality ratio per period, caps (BR-40/41), Dept Manager personal KPI (BR-34) missing |
| 9 | Ranking | 🟡 | `EvaluationController` calibration / approvals / finalize | Forced-distribution tiers, dept+position grouping, Publish → Lock snapshot to verify |
| 10 | HR export | ❌ | — | No xlsx/PDF export of locked rankings |

## 2. Supporting areas

| Area | Status | Notes |
|---|---|---|
| Auth, roles, permission matrix (Resource×Action×Scope) | ✅ | `AuthController`, `PermissionService`, `RolePermission.Scope` |
| Employee / Department (view-only) | 🟡 | `EmployeeController`, `DepartmentsController`; context says employee changes **only via import** — check update endpoints |
| Performance dashboard | ✅ | Personal / Department / Branch + drill-down |
| Sidebar configuration, My profile | ✅ | |
| Excel import: employee, transaction, service rating | ❌ | No import service or xlsx library. Blocks steps 6–7, BR-07, BR-17/18, BR-22 |
| System parameters (e.g. BR-01 handover threshold) | ❌ | |
| Product multipliers, quality ratio | ❌ | |
| Risk events (QA) | 🟡 | `POST /risk-events`, `GET /risk-events/{id}`, `/confirm` (writes a negative `performance_contribution`), `/dismiss`, `/resolve` (docs/api/RISK_EVENT_CONTRACT.md). Missing: list/search API, evidence upload, `risk_event:*` permission seed for a QA role |
| QA 5% random sampling (BR-32) | ❌ | |
| Appeals, 2-level (Dept → Branch), withdraw | ❌ | Entity `Appeal` only |
| Period pre-close / Publish / Lock | 🟡 | `EvaluationPeriod` + finalize; pre-close carry-over missing |
| Audit log | 🟡 | `AuthAuditLog` for auth only; Admin audit log missing |

## 3. Conflicts to fix (built code vs 25/10 decisions)

1. **BR-07 referral credit** – `ReferralPointSplit` + `VerificationService.SplitHandoverContributions` split one KPI 30/70 based on an `ACCEPTED` `TaskHandover`. New rule: Teller and Sales Officer get credit under **two different KPI items**, identified by the **Refer ID** column of the imported transaction file; missing Refer ID → processor only. The comment in `ReferralPointSplit.cs` still says "credit officer".
2. **Handover approval** – `HandoverStatus` has `PENDING / ACCEPTED / REJECTED`. New rule: no approval, no SLA (BR-21 deleted).
3. **Manual verification** – `VerificationController` / `review-verify` approve every result. New rule: only work outside the WorkType list is approved manually (SCR-REV-04).
4. **Submission states** – `SubmissionStatus` / `VerificationDecision` lack `MATCHED / NOT_MATCHED / IN_REVIEW`.
5. **Idempotency (BR-08)** – `VerificationService.cs:210` checks for existing `WorkResultItems`; skipped contributions (no active target) and concurrent approvals bypass it. Add a unique constraint per Work.

## 4. Suggested order

1. Excel import infrastructure (employee → transaction → service rating) – unblocks most gaps.
2. Target Allocation API with auto month/week/day split.
3. Reconciliation service + new submission states; restrict manual verification to non-WorkType work.
4. Rewrite BR-07 on Refer ID with two KPI items; drop handover approval.
5. Multipliers, quality ratio, ranking tiers, Publish/Lock, HR export.
6. Appeals, risk events, QA sampling, system parameters, audit log.
