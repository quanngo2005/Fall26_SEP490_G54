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

The BLL restricts the target department to the caller's granted scope. `All` bypasses the check; `Branch`
covers the nearest branch subtree; `Dept` the caller's own department subtree. A department outside that set
returns 403 `FORBIDDEN`.

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
| `evaluationStatus` | string | `NOT_STARTED` (no row) or `DRAFT / CALCULATED / IN_REVIEW / APPROVED / DISPUTED / FINAL` |
| `proposedTier` | string or null | tier of the stored total |
| `calculatedAt` | timestamp or null | last calculation |
| `quantitativeScore` | number or null | weighted mean of the scores of the root groups of the profile tree that own active KPIs, from the same calculation as the form and `POST /performance-evaluations/{id}/calculate` (a KPI without a target or progress is left out of its group); null when the employee has no profile or no scorable KPI |
| `qualitativeScore` | number or null | the single qualitative score (`generalQualitativeScore`) saved on the evaluation; when none was entered, the mean of the per-group qualitative scores saved on it; null when there is neither |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or an appeal on it is `OPEN`/`UNDER_REVIEW` |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW`, submitted inside the period |
| `canEvaluate` | bool | caller holds `evaluation:submit` covering the row's own department, period is `OPEN`/`IN_REVIEW`, the row is not the caller, has no open dispute and is not yet `IN_REVIEW` or later |
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

The "Evaluate" / "View" buttons of the list open `/evaluations/form/{periodCode}/{employeeCode}` (for example
`/evaluations/form/2026-Q4/NV004`); no GUID appears in the address. The page resolves the employee code with
`GET /employees/by-code/{code}` and the period code from `GET /evaluations/periods`, then calls
`GET /evaluations/form` with their ids (the API itself still takes `employeeId` and `periodId`). An unknown
code is shown as "not found". The old `?employeeId=&periodId=` address is no longer read.

| Endpoint | Permission | Scope |
| --- | --- | --- |
| `GET /evaluations/form` | `evaluation:read` | `Own` or higher |
| `POST /evaluations/submit` | `evaluation:submit` | `Dept` or higher |

A caller reads their own form with `Own`; any other employee needs `evaluation:read` covering the employee's
department (403 `FORBIDDEN` otherwise). Submitting needs `evaluation:submit` covering the department, and the
reviewer cannot evaluate themselves (400 `MAKER_CANNOT_BE_CHECKER`). Unknown employee, period or profile: 404.

### `GET /evaluations/form`

Query: `employeeId`, `periodId` (GUID, required). `data` (`EvaluationFormDto`):

| Field | Type | Meaning |
| --- | --- | --- |
| `employee`, `period`, `profile` | object | identity of the evaluated employee, the period and the evaluation profile |
| `criteriaGroups[]` | array | one entry per root group of the profile tree, in display order: `evaluationProfileItemId`, `groupCode`, `groupName`, `weightPercent`, `groupScore`, `kpiItems[]`, `qualitativeScore`, `qualitativeNotes`, `isQualitative`. `groupScore` is null only when the group has nothing to score (no active KPI and no qualitative score); a KPI group whose KPIs have no target or progress scores 0, not N/A (awaiting BA confirmation). `evaluationProfileItemId` is the tree node; `groupName` is its name; `groupCode` is the code of the catalog KPI group of its KPIs (empty when it has none); `kpiItems[]` lists the active KPI leaves of the group and of its sub-groups |
| `criteriaGroups[].kpiItems[]` | array | `kpiDefinitionId`, `kpiCode`, `kpiName`, `unit`, `weightPercent`, `targetValue`, `actualValue`, `achievementPercent`, `componentScore`. `weightPercent` is the KPI's share of its root group in percent (its weight among its siblings times the weights of its parent groups), normalised at every level over the siblings that still own an active KPI, so the active KPIs of a group add up to 100 and a KPI that is not active leaves the shares of the others in proportion; it comes from the configured profile weights and does not change when a KPI has no target. `targetValue` is null when the KPI has no assigned target; `actualValue`, `achievementPercent` and `componentScore` are null when the KPI has no progress in the period or no target to score against (shown as N/A, never as a stored 0). In the group score such a KPI counts as 0 and keeps its share (awaiting BA confirmation); only an inactive KPI leaves its share to the others |
| `criteriaGroups[].isQualitative` | bool | the group owns no active KPI (none at all, or all inactive); its score is the qualitative score the reviewer enters (`evaluationProfileItemId` is the key of the submit input) and counts as 0 until one is saved. A group with an active KPI is scored from its KPIs only; a qualitative score stored for it earlier is kept but ignored |
| `riskEvents[]` | array | risk events of the period that were not dismissed, with `penaltyPoints` |
| `summary` | object | `weightedScore` (weighted group scores before the penalty), `totalRiskPenalty`, `totalScore`, `proposedTier`, `businessScore`, `serviceQualityScore`, `operationsScore`. The scores are produced by the same calculation as `POST /performance-evaluations/{id}/calculate` (see `PERFORMANCE_EVALUATION_CONTRACT.md`), so the group scores add up to the total. For an evaluation that is `DRAFT` or `CALCULATED` (or not stored yet) they are calculated on every request; from `IN_REVIEW` on, `totalScore` (and the `proposedTier` derived from it) is the stored score, which is never replaced by a recalculation, while the group scores, `weightedScore` and the Business / Service / Operations scores are still calculated from the current data, so for such an evaluation they may not add up to `totalScore` if the targets or progress changed after it was stored. `totalScore`, `proposedTier` and `weightedScore` are null only when no group has a score (a profile without any root group); a KPI group without target or progress scores 0 |
| `currentEvaluation` | object | stored state: `id`, `totalScore`, `status` (`DRAFT / CALCULATED / IN_REVIEW / APPROVED / DISPUTED / FINAL`, `DRAFT` when nothing is stored), `comment`, `coachingPlan`, `coachingRequired` |
| `canEdit` | bool | period is `OPEN`/`IN_REVIEW`, the evaluation is not yet `IN_REVIEW` or later, the caller is not the employee, there is no open dispute, and the caller holds `evaluation:submit` covering the department |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW` inside the period |
| `openAppealCount` | number | appeals on the stored evaluation in `OPEN`/`UNDER_REVIEW` |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or `openAppealCount > 0` |
| `qualitativeScoreMax` | number | highest value of the single qualitative score (`generalQualitativeScore`); it is `SystemParameters:QualitativeScoreMax` in appsettings (required, must be greater than 0, checked at startup) |
| `qualitativeGroupScoreMax` | number | highest value of the qualitative score of one criteria group (`qualitativeScores[].score`); it is `SystemParameters:QualitativeGroupScoreMax` in appsettings (required, must be greater than 0, checked at startup) |
| `currentEvaluation.generalQualitativeScore` | number or null | the single qualitative score saved with the evaluation, null when none was entered |
| `previousPeriod` | object or null | latest earlier period of the same type with a scored evaluation that is not `DRAFT`: `periodCode`, `totalScore`, `tier` (the tier stored by calibration; recalculated only when none was stored), `scoreDifference` (current total minus previous; null when the current total is not available); null = no comparison (N/A). `totalScore` is the stored score and is never recalculated, so a score saved before the group score became a weighted average still follows the old formula and `scoreDifference` is then indicative only |

### `POST /evaluations/submit`

Body: `employeeId`, `periodId`, `comment`, `coachingPlan`, `coachingRequired`, `qualitativeScores[]`
(`evaluationProfileItemId`, `score`, `notes`), `isDraft`, `generalQualitativeScore` (optional number from 0 to
`SystemParameters:QualitativeScoreMax`, otherwise 400 `VALIDATION_ERROR`). `generalQualitativeScore` is stored with the
evaluation (in the same column as the comment, as a `GENERAL_QUALITATIVE_SCORE` marker) and returned by
`GET /evaluations/form`; it is a record of the reviewer's score only and does not take part in the total score yet
(formula awaiting BA confirmation). `data`: `evaluationId`, `employeeId`, `periodId`, `totalScore`,
`status` (`DRAFT` when `isDraft`, otherwise `CALCULATED`; saving as a draft an evaluation that is already `CALCULATED` keeps it `CALCULATED`), `proposedTier`, `calculatedAt`, `message`. `totalScore`, `proposedTier` and `calculatedAt` are null only when no group has a score (a profile without any root group; only possible for a draft).

Saving runs the same calculation as `POST /performance-evaluations/{id}/calculate` and stores its result together with the evaluation in one transaction: the total score and the KPI detail rows (`performance_contribution`) that the calculate endpoint writes, so a saved form has the same KPI breakdown as a calculated one and the form, the save and the calculate endpoint give the same score for the same data. The caller needs only `evaluation:submit` (it does not need `evaluation:calculate`). The total score is the weighted group scores minus the risk penalty (never below 0) plus the appeal adjustments of the period, rounded to 4 decimals.

The backend validates the free text and the qualitative scores (the frontend checks the same limits first so the reviewer sees the problem before sending): comment, coachingPlan and each qualitativeScores[].notes may not be longer than the length allowed by the validation attribute of the request field (`SubmitEvaluationRequestDto` and `QualitativeScoreInputDto`; a longer value is 400 `VALIDATION_ERROR` before the service runs); comment and coachingPlan may not contain `<!--` or `-->` (the column that stores them also holds system markers, so reviewer text is never read back as data); each qualitativeScores[].score must be from 0 to `SystemParameters:QualitativeGroupScoreMax` (also returned as `qualitativeGroupScoreMax` by the form). Each `qualitativeScores[].evaluationProfileItemId` must be the id of a root group of the employee's profile and appear at most once; an empty id (a client still sending the old `kpiGroupId`), a duplicate, a non-root id, or the id of a group that owns an active KPI is rejected with 400 `VALIDATION_ERROR` before anything is saved. A group without an active KPI that has no qualitative score counts as 0 in a draft (awaiting BA confirmation); completing the evaluation (`isDraft = false`) while one has no score is rejected with 400 `VALIDATION_ERROR` (the message lists the groups). Completing while no group has a score at all (a profile without any root group) is rejected with 400 `VALIDATION_ERROR`; a draft is saved with a null total. An employee whose active KPIs have no target or progress is not in that case: those KPIs count as 0, so the evaluation can be completed with a low total. Qualitative scores saved before the profile became a tree were keyed by the catalog group id and no longer apply: they are skipped (with a warning in the log) when the form is read, so the reviewer enters them again. The skipped entries (and an unreadable qualitative JSON) stay stored unchanged: calibration, calibration rejection, and period finalization only replace their own markers (`CALIBRATION_JSON`, `RECALIBRATION_REASON`, `APPROVAL_COMMENTS`) and never rewrite the qualitative data; only saving the evaluation form replaces it. An evaluation can be saved again only while it is `DRAFT` or `CALCULATED`; one that is `IN_REVIEW`, `APPROVED` or `FINAL` is not overwritten (409 `PERFORMANCE_EVALUATION_INVALID_STATE`, nothing is saved; the form's `canEdit` reports the same). When two requests save the first evaluation of the same employee and period at the same time, the second one is refused with 409 `CONFLICT`; the same applies to two requests that save the KPI detail rows of the same evaluation at the same time (a unique index allows one row per anchoring progress row), and to a request whose KPI rows were changed or removed by another request meanwhile, but not when the progress changes between the two requests, and two requests saving an evaluation that already exists can still overwrite each other (the last one wins; the evaluation has no concurrency token). Errors: 400 `VALIDATION_ERROR`; 400 `MAKER_CANNOT_BE_CHECKER`; 403 `FORBIDDEN`; 404; 409 `CONFLICT` (period `LOCKED`/`CLOSED`, or a concurrent first save); 409 `PERFORMANCE_EVALUATION_INVALID_STATE` (evaluation already `IN_REVIEW` or later); 409 `EVALUATION_DISPUTE_PENDING` (score under dispute).

Notes:

- The qualitative score entered on the form belongs to a group with `isQualitative = true`. When the profile has
  no such group the form has nothing to enter and the reviewer only writes the comment.
- "Quality ratio" and the "History" link have no data source or target screen yet (waiting for confirmation);
  the form does not show them. The transaction-trace links (SCR-PRF-05) are not part of this scope.
- Whether HR holds `evaluation:read` depends on the role grants in the database; this task does not change them.
