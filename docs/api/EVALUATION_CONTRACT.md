# Employee Evaluation List (SCR-EVL-01)

Status: backend implemented. Typed client: `frontend/src/app/core/evaluation.service.ts`.
Routes use the backend origin, no API prefix. Responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`).
Enums are UPPER_SNAKE strings matching the PostgreSQL names. IDs are GUID strings. Calibration and approval
endpoints of the same controller are described in `CALIBRATION_APPROVAL_CONTRACT.md`. The evaluation form
(SCR-EVL-01-D1) is described in the last section of this contract.

## Authorization

| Endpoint | Permission | Scope |
| --- | --- | --- |
| `GET /evaluations/periods` | `evaluation:read` | `Own` or higher |
| `GET /evaluations/department-employees` | `evaluation:read` | `Dept` or higher |
| `GET /evaluations/department-dashboard` | `evaluation:read` | `Dept` only (Department Manager) |
| `GET /evaluations/department-dashboard/export` | `evaluation:read` | `Dept` only (Department Manager) |

The BLL restricts the target department to the caller's granted scope. `All` bypasses the check on `/department-employees`; `Branch`
covers the nearest branch subtree; `Dept` the caller's own department subtree. A department outside that set
returns 403 `FORBIDDEN`. For `/department-dashboard` and `/department-dashboard/export`, only a Department Manager (`evaluation:read` at `Dept` scope without `Branch` or `All` scope) is allowed to access their own department; `BRANCH_MANAGER` and `ADMIN` / `SYSTEM_ADMIN` receive 403 `FORBIDDEN`.

The seed grants `evaluation:read`/`submit` to `MANAGER`, `DEPT_MANAGER`, `BRANCH_MANAGER` and `ADMIN`, and
`evaluation:read` at `Own` to `EMPLOYEE`. No role `HR` exists in the seed: HR cannot call these endpoints
until the permission is granted to it (not part of this task). `ADMIN` passes the backend check; the frontend
only hides the menu entry for admin and QA (no route guard), so the screen can still be opened by URL.

## `GET /evaluations/periods`

`data`: array of `EvaluationPeriodOptionDto`: `id`, `code`, `name`, `startDate` (`YYYY-MM-DD`), `endDate`,
`periodType`, `status` (`OPEN | IN_REVIEW | LOCKED | CLOSED`), `isActive` (status is `OPEN` or `IN_REVIEW`).
Periods of type `DAY` are excluded; ordered by `startDate` descending. The list screen takes the deadline
from the selected period's `endDate`; there is no separate evaluation deadline column.
The evaluation list screen drills through the periods with three selects (year, quarter, month) built from
`periodType` `YEAR`/`QUARTER`/`MONTH` and each period's `startDate`; "whole year" / "whole quarter" select the
`YEAR` / `QUARTER` period itself and are offered only when that period exists. Other period types are not offered there.

## `GET /evaluations/department-employees`

Query: `periodId` (GUID, required), `departmentId` (GUID, optional; defaults to the caller's department).
`data`: array of `DepartmentEmployeeEvaluationSummaryDto` for the **active** employees of that department and
of every department beneath it (the whole subtree, the same reach as the employee list), ordered by full name.
The response is not paged. `departmentId` / `departmentName` of a row are the employee's own department.

| Field | Type | Meaning |
| --- | --- | --- |
| `employeeId`, `employeeCode`, `fullName` | string | employee |
| `jobPositionTitle`, `jobPositionId` | string | position |
| `departmentId`, `departmentName` | string | department |
| `evaluationId` | GUID or null | null when no evaluation row exists |
| `totalScore` | number or null | stored total; null = N/A |
| `evaluationStatus` | string | `NOT_STARTED` (no row) or `DRAFT / DISPUTED / CALCULATED / SUBMITTED / FINAL` |
| `proposedTier` | string or null | tier of the stored total |
| `calculatedAt` | timestamp or null | last calculation |
| `quantitativeScore` | number or null | weighted mean of the scores of the root groups of the profile tree that own active KPIs, from the same calculation as the form and `POST /performance-evaluations/{id}/calculate` (a KPI without a target or progress is left out of its group); null when the employee has no profile or no scorable KPI |
| `qualitativeScore` | number or null | the single qualitative score (`generalQualitativeScore`) saved on the evaluation; when none was entered, the mean of the per-group qualitative scores saved on it; null when there is neither |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or an appeal on it is `OPEN`/`UNDER_REVIEW` |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW`, submitted inside the period |
| `canEvaluate` | bool | caller holds `evaluation:submit` covering the row's own department, period is `OPEN`/`IN_REVIEW`, the row is not the caller, has no open dispute and is `DRAFT` or `CALCULATED` (or has no row yet) |
| `updatedAt` | timestamp or null | last update of the evaluation row |
| `quantitativeScoreChange` | number or null | current `quantitativeScore` minus the quantitative score of the latest earlier period of the same type (`endDate` before the selected period's `startDate`), both from the shared score calculation and rounded to 2 decimals; null when there is no earlier period or either score is null (N/A, never 0) |

Notes:

- The list screen maps `NOT_STARTED` to "not evaluated", `DRAFT` to "draft" and every later status to
  "completed". "Needs attention" is `pendingSubmissionCount > 0` or `hasOpenDispute`.
- "Missing import data" has no data source yet, so the list does not show it.
- The quantitative score is computed per request by the shared score calculation from target assignments and valid
  actual progress; it can differ from the stored `totalScore`, which includes qualitative input, risk penalties and
  appeal adjustments.
- The list filters by the stat cards and the keyword only (no position or attention dropdown, no "Save filter").
  The quantitative cell shows `quantitativeScoreChange` next to the score: green with a leading `+` when positive,
  red when negative, neutral when zero, nothing when null.
- The "Evaluate" / "View" buttons and "Go to calibration" have no action in this scope.
- Errors: 403 `FORBIDDEN` (department outside scope).

## Evaluation form (SCR-EVL-01-D1)

The "Evaluate" / "View" buttons of the list open `/evaluations/form`; the address names neither the employee nor the
period. The list keeps the employee id and the period id of the row in the browser tab (session storage), and the form
reads them and calls `GET /evaluations/form` with them. The previous / next buttons replace the stored employee and load
that form without changing the address. A refresh keeps the form of the stored employee; another tab, or an address
typed in a tab that never opened a form, has nothing stored and shows "not found". No API call is added or changed.

| Endpoint | Permission | Scope |
| --- | --- | --- |
| `GET /evaluations/form` | `evaluation:read` | `Own` or higher |
| `GET /evaluations/form/export` | `evaluation:read` | `Own` or higher |
| `POST /evaluations/submit` | `evaluation:submit` | `Dept` or higher |

A caller reads their own form with `Own`; any other employee needs `evaluation:read` covering the employee's
department (403 `FORBIDDEN` otherwise). Submitting needs `evaluation:submit` covering the department, and the
reviewer cannot evaluate themselves (400 `MAKER_CANNOT_BE_CHECKER`). Unknown employee, period or profile: 404.

### `GET /evaluations/form`

Query: `employeeId`, `periodId` (GUID, required), `comparePeriodId` (GUID, optional). `data` (`EvaluationFormDto`):

| Field | Type | Meaning |
| --- | --- | --- |
| `employee`, `period`, `profile` | object | identity of the evaluated employee, the period and the evaluation profile |
| `criteriaGroups[]` | array | quantitative root groups of the profile tree (`!isQualitative`) in display order: `evaluationProfileItemId`, `groupCode`, `groupName`, `weightPercent` (re-normalised over quantitative groups to add up to 100), `groupScore`, `kpiItems[]`. Qualitative groups without active KPIs are excluded from the form and do not affect the KPI total |
| `criteriaGroups[].kpiItems[]` | array | `kpiDefinitionId`, `kpiCode`, `kpiName`, `unit`, `weightPercent`, `targetValue`, `actualValue`, `achievementPercent`, `componentScore`. `weightPercent` is the KPI's share of its root group in percent, normalised over active KPIs |
| `riskEvents[]` | array | risk events of the period that were not dismissed, with `penaltyPoints` |
| `summary` | object | `weightedScore` (quantitative KPI weighted mean before penalty), `totalRiskPenalty`, `totalScore` (`max(0, weightedScore - totalRiskPenalty)` for `DRAFT` / `CALCULATED`, or the stored score from `DISPUTED` / `SUBMITTED` / `FINAL` on), `proposedTier` (only the `CalibratedTier` assigned on the Ranking/Calibration screen; `null` until calibrated), `businessScore`, `serviceQualityScore`, `operationsScore` |
| `currentEvaluation` | object | stored state: `id`, `totalScore`, `status` (`DRAFT / DISPUTED / CALCULATED / SUBMITTED / FINAL`, `DRAFT` when nothing is stored), `comment`, `coachingPlan`, `coachingRequired` |
| `canEdit` | bool | period is `OPEN`/`IN_REVIEW`, the evaluation is `DRAFT` or `CALCULATED`, the caller is not the employee, there is no open dispute, and the caller holds `evaluation:submit` covering the department |
| `canComplete` | bool | `canEdit` and the period is locked for ranking (`rankingLockDay` reached, `IsRankingLocked`). Only then can the DM press "Hoàn tất" (`isDraft = false`); saving a draft is never blocked |
| `canAppeal` | bool | the employee can file an appeal: remaining appeal days > 0 and the evaluation is `CALCULATED`, `SUBMITTED` or `FINAL` |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW` inside the period |
| `openAppealCount` | number | appeals on the stored evaluation in `OPEN`/`UNDER_REVIEW` |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or `openAppealCount > 0` |
| `previousPeriod` | object or null | the employee's score in the selected compare period (non-`DRAFT`, with a score): `periodCode`, `totalScore`, `tier`, `scoreDifference`; null = no compare period or no scored evaluation in it (N/A) |
| `comparePeriod` | object or null | selected compare period (`EvaluationPeriodOption`), same rules as `/evaluations`: same `periodType` and starting before the period; defaults to the latest one when `comparePeriodId` is absent; null = none available |
| `compareOptions[]` | array | every period that may be compared with `periodId` (`EvaluationPeriodOption[]`), newest first |
| `criteriaSummary` | object | `achievedCount`, `nearCount`, `belowCount`, `totalCount`, `achievedRatePercent` classified against `evalKpiMetThresholdPercent` and `evalKpiNearThresholdPercent` from `system_parameters` |
| `slaSummary` | object | `onTimeCompletedCount`, `overdueCount`, `inProgressCount`, `onTimeRatePercent` computed for the employee in the period using the same `Work` (`PlannedEndDate != null`) rule as `GET /evaluations/department-dashboard` |
| `formSettings` | object | `maxCommentLength` (`evalCommentMaxLength`), `kpiMetThresholdPercent` (`evalKpiMetThresholdPercent`), `kpiNearThresholdPercent` (`evalKpiNearThresholdPercent`) read from `system_parameters` |

### `GET /evaluations/form/export`

Query: `employeeId`, `periodId` (GUID, required), `comparePeriodId` (GUID, optional; same rules as `GET /evaluations/form`).
Response: raw `.xlsx` workbook (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, no `ApiResponse` envelope) named `danh-gia-nhan-vien-<employeeCode>-<periodCode>.xlsx`, built from `GetEvaluationFormAsync(employeeId, periodId, currentUserId)` with three sheets:

- `Tong quan`: employee code/name/department/position, period code/name/date range/status, profile code/name/version, evaluation status & reviewer, total score, weighted quantitative score, risk penalty, qualitative score, proposed tier & rule explanation, previous period comparison, pending work submissions & open appeals, coaching requirement/plan, evaluation comment, and generated timestamp.
- `Tieu chi & KPI`: criteria groups (`groupName`, `weightPercent`, `groupScore`, qualitative/quantitative type) and each active KPI (`kpiCode`, `kpiName`, `unit`, `weightPercent`, `targetValue`, `actualValue`, `achievementPercent`, `componentScore`, notes).
- `Su kien rui ro`: non-dismissed risk events in the period (`code`, `eventType`, `description`, `severity`, `penaltyPoints`, `occurredAt`).

### `POST /evaluations/submit`

Body: `employeeId`, `periodId`, `comment`, `coachingPlan`, `coachingRequired`, `isDraft`. `data`: `evaluationId`, `employeeId`, `periodId`, `totalScore`, `status` (`DRAFT` when `isDraft`, otherwise `CALCULATED`; saving as a draft an evaluation that is already `CALCULATED` keeps it `CALCULATED`). Completing (`isDraft = false`) is allowed only from `rankingLockDay` on; before that the call returns 409 `PERIOD_NOT_LOCKED_YET`. The first transition to `CALCULATED` sends an `EVALUATION_CALCULATED` notification to the employee, `proposedTier` (only the `CalibratedTier` assigned on the Ranking/Calibration screen; `null` until calibrated), `calculatedAt`, `message`. `totalScore` and `calculatedAt` are null only when the profile has no quantitative KPI group to score (only possible for a draft).

Saving scores quantitative KPI groups only (`max(0, quantitativeScore - riskPenalty)`, rounded to 4 decimals) and writes `performance_contribution` rows in one transaction. Qualitative groups without active KPIs are excluded from `/evaluations/form`, never block completion, and do not affect the saved KPI total score. The caller needs only `evaluation:submit` (it does not need `evaluation:calculate`).

The backend validates the free text dynamically: `comment` and `coachingPlan` may not exceed `evalCommentMaxLength` from `system_parameters` (400 `VALIDATION_ERROR` in `EvaluationService.ValidateSubmitText`) and may not contain `<!--` or `-->` (reserved for stored system markers). Completing an evaluation (`isDraft = false`) requires a non-empty `comment` (400 `VALIDATION_ERROR` when null/blank) and at least one quantitative KPI group on the profile (400 `VALIDATION_ERROR` when none; a draft saves with `totalScore = null`). An employee whose active KPIs have no target or progress is not in that case: those KPIs count as 0, so the evaluation can be completed with a low total. Calibration, calibration rejection, and period finalization markers (`CALIBRATION_JSON`, `RECALIBRATION_REASON`, `APPROVAL_COMMENTS`) remain preserved on save. An evaluation can be saved again only while it is `DRAFT` or `CALCULATED`; one that is `DISPUTED`, `SUBMITTED` or `FINAL` is not overwritten (409 `PERFORMANCE_EVALUATION_INVALID_STATE`, nothing is saved; the form's `canEdit` reports the same). Concurrent first saves return 409 `CONFLICT`. Errors: 400 `VALIDATION_ERROR`; 400 `MAKER_CANNOT_BE_CHECKER`; 403 `FORBIDDEN`; 404; 409 `CONFLICT` (period `LOCKED`/`CLOSED`, or a concurrent first save); 409 `PERIOD_NOT_LOCKED_YET` (completing before `rankingLockDay`); 409 `PERFORMANCE_EVALUATION_INVALID_STATE` (evaluation already `SUBMITTED` or later, or `DISPUTED`); 409 `EVALUATION_DISPUTE_PENDING` (score under dispute).

Notes:

- Qualitative evaluation is excluded from `/evaluations/form`; the form only displays quantitative KPI groups and reviewer comments.
- "Quality ratio" and the "History" link have no data source or target screen yet (waiting for confirmation);
  the form does not show them. The transaction-trace links (SCR-PRF-05) are not part of this scope.
- Whether HR holds `evaluation:read` depends on the role grants in the database; this task does not change them.

## My performance: criterion ledger (`/my-evaluation`)

### `GET /evaluations/personal/ledger`

Permission: any signed-in employee with a personal evaluation (own data only). Query: `periodId`, `criteriaId` (GUID, required; `criteriaId` is the KPI definition id or the target assignment id). `data` is an array (no paging) of the caller's valid progress entries of the criterion inside the period, newest first:

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | GUID | work result item id (or the progress id when there is none) |
| `txCode` | string | transaction code; `VR-xxxxxxxx` / `AP-xxxxxxxx` generated when the entry has no work transaction |
| `date` | string | `dd/MM/yyyy` display date |
| `occurredOn` | string | same date as `yyyy-MM-dd`, used by the date filter |
| `amount` | string | formatted transaction amount |
| `kpiContribution` | number | value counted for the KPI |
| `status` | string | work transaction status `MATCHED` / `IN_REVIEW` / `NOT_MATCHED`; `VALID` when there is no work transaction |
| `workId`, `workCode`, `workTitle` | GUID / string / string, nullable | the work behind the entry: the work of the work transaction, else the work of the verified submission; null when neither exists |

The modal searches (transaction code, work code or title), filters by status and by date range in the browser on this array. A row with `workId` opens `/my-work?keyword=<workCode>&workId=<workId>`: the work board on the caller's own work, searched by the work code, with the detail drawer of that work open. Errors: 404 unknown period or employee.

## Department Evaluation & Performance Dashboard (`/evaluations`)

### `GET /evaluations/department-dashboard`

Permission: `evaluation:read` at `Dept` scope (Department Manager only; `BRANCH_MANAGER` and `ADMIN` / `SYSTEM_ADMIN` receive 403 `FORBIDDEN`).
Query: `periodId` (GUID, optional; defaults to the latest open or active evaluation period), `comparePeriodId` (GUID, optional; defaults to the latest earlier period of the same `periodType`).

Response `data` (`DepartmentEvaluationDashboardDto`):

| Field | Type | Meaning |
| --- | --- | --- |
| `department` | object | `id`, `code`, `name`, `managerName` |
| `period` | object or null | `id`, `code`, `name`, `startDate`, `endDate`, `periodType`, `status`, `isActive` |
| `comparePeriod` | object or null | selected or default compare period of the same `periodType` (`startDate < period.startDate`) |
| `availablePeriods` | array | selectable periods (`periodType != DAY`), newest first |
| `compareOptions` | array | earlier periods of the same `periodType` as `period`, newest first |
| `performance` | object | `average`, `previousAverage`, `changeFromPrevious`, `evaluatedEmployeeCount`, `totalEmployeeCount`, `unscoredEmployeeCount`, `pipCount`, `appealCount`, `kpiGroups[]`, `trend[]` |
| `operations` | object | `onTimeCompletedCount`, `overdueCount`, `inProgressCount`, `onTimeRatePercent`, `loadPerStaff`. SLA counts only `Work` items where `PlannedEndDate != null`: `onTimeCompletedCount` = completed (`DONE` / `VERIFIED`) in period with completion business date `<= PlannedEndDate`; `overdueCount` = open overdue (`Status == OVERDUE` or `PlannedEndDate < today`) plus late-completed in period (completion business date `> PlannedEndDate`); `onTimeRatePercent = onTimeCompletedCount / (onTimeCompletedCount + overdueCount) * 100` (null when denominator is 0) |
| `tierDistribution` | object | `tierACapPercent` (20), `tiers[]` (`tierCode`, `rankOrder`, `minScore`, `maxScore`, `count`, `percent` for all 5 tiers `A1`, `A2`, `A3`, `B`, `C` from `ranking_tier_threshold`), `actionAlerts[]` (`kind`: `PIP` \| `APPEAL`, `employeeId`, `employeeCode`, `fullName`, `tierCode`, `totalScore`, `metaText`) |
| `profileGroups` | array | ordered root groups of the department's evaluation profiles (`name`, `displayOrder`, `weightPercent`) |
| `staffRows` | array | `employeeId`, `employeeCode`, `fullName`, `jobPositionId`, `jobPositionTitle`, `groupScores[]` (`groupName`, `displayOrder`, `weightPercent`, `score`, `previousScore`), `totalScore`, `previousTotalScore`, `scoreDelta`, `tierCode`, `onTimeSlaPercent`, `previousOnTimeSlaPercent` (SLA of the compare period, `null` when none), `status` (`FINAL` \| `APPEAL` \| `PIP` \| `PENDING`), `evaluationStatus`, `canOpenForm` |

### `GET /evaluations/department-dashboard/export`

Query: `periodId` (GUID, optional), `comparePeriodId` (GUID, optional).
Response: raw `.xlsx` workbook (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, no `ApiResponse` envelope) named `danh-gia-phong-<deptCode>-<periodCode>.xlsx`, built from `GetDepartmentEvaluationDashboardAsync(periodId, comparePeriodId, currentUserId)` using the same ClosedXML structure as `DepartmentService.Export.cs` and customized to include every section reported on `/evaluations`:

- `Tong quan`: department code/name, manager, period name/code/status, compare period, average score / previous / delta, KPI met rate % / met count / total count / notice count, SLA on-time % / on-time count / overdue count / in-progress count / load per staff, evaluated / total / unscored / PIP / appeal staff counts, Tier A cap %, and generated timestamp.
- `KPI`: weighted KPI group name, weight %, group average score, KPI code, name, unit, target, actual, achievement %, compare achievement %, and delta %.
- `Xu huong`: 6-period department score trend (`periodCode`, `periodName`, `average`).
- `Xep loai`: 5 ranking tiers (`A1`, `A2`, `A3`, `B`, `C` with min/max score, count, percent) followed by manager action alerts (`PIP`, `APPEAL` with employee code/name, tier, score, and detail).
- `Can bo`: staff evaluation rows (`employeeCode`, `fullName`, `jobPositionTitle`, dynamic profile group columns, `totalScore`, `previousTotalScore`, `scoreDelta`, `tierCode`, `onTimeSlaPercent`, localized `status`, and `evaluationStatus`).

A failed export shows an inline error banner and saves nothing.
