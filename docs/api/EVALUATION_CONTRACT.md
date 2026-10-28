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
| `quantitativeScore` | number or null | weighted mean of the achievement scores of the profile groups that own active KPIs; null when the employee has no profile or no scorable KPI |
| `qualitativeScore` | number or null | mean of the qualitative scores saved on the evaluation; null when none |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or an appeal on it is `OPEN`/`UNDER_REVIEW` |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW`, submitted inside the period |
| `canEvaluate` | bool | caller holds `evaluation:submit` covering the row's own department, period is `OPEN`/`IN_REVIEW`, the row is not the caller, has no open dispute and is not yet `IN_REVIEW` or later |
| `updatedAt` | timestamp or null | last update of the evaluation row |

Notes:

- The list screen maps `NOT_STARTED` to "not evaluated", `DRAFT` to "draft" and every later status to
  "completed". "Needs attention" is `pendingSubmissionCount > 0` or `hasOpenDispute`.
- "Missing import data" has no data source yet, so the list does not show it.
- The quantitative score is computed per request from target assignments and valid actual progress; it can
  differ from the stored `totalScore`, which includes qualitative input and risk penalties.
- The "Evaluate" / "View" buttons and "Go to calibration" / "Save filter" have no action in this scope.
- Errors: 403 `FORBIDDEN` (department outside scope).

## Evaluation form (SCR-EVL-01-D1)

The "Evaluate" / "View" buttons of the list open `/evaluations/form?employeeId=<GUID>&periodId=<GUID>`.

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
| `criteriaGroups[]` | array | one entry per profile group, in display order: `kpiGroupId`, `groupCode`, `groupName`, `weightPercent`, `groupScore`, `kpiItems[]`, `qualitativeScore`, `qualitativeNotes`, `isQualitative` |
| `criteriaGroups[].isQualitative` | bool | the group owns no active KPI; its score is the qualitative score the reviewer enters (`kpiGroupId` is the key of the submit input) |
| `riskEvents[]` | array | risk events of the period that were not dismissed, with `penaltyPoints` |
| `summary` | object | `weightedScore` (weighted group scores before the penalty), `totalRiskPenalty`, `totalScore` (stored total when saved, otherwise the calculated one), `proposedTier`, `businessScore`, `serviceQualityScore`, `operationsScore` |
| `currentEvaluation` | object | stored state: `id`, `totalScore`, `status` (`DRAFT / CALCULATED / IN_REVIEW / APPROVED / DISPUTED / FINAL`, `DRAFT` when nothing is stored), `comment`, `coachingPlan`, `coachingRequired` |
| `canEdit` | bool | period is `OPEN`/`IN_REVIEW`, the evaluation is not yet `IN_REVIEW` or later, the caller is not the employee, there is no open dispute, and the caller holds `evaluation:submit` covering the department |
| `pendingSubmissionCount` | number | result submissions of the employee in `SUBMITTED`/`UNDER_REVIEW` inside the period |
| `openAppealCount` | number | appeals on the stored evaluation in `OPEN`/`UNDER_REVIEW` |
| `hasOpenDispute` | bool | evaluation is `DISPUTED` or `openAppealCount > 0` |
| `previousPeriod` | object or null | latest earlier period of the same type with a scored evaluation: `periodCode`, `totalScore`, `tier`, `scoreDifference` (current total minus previous); null = no comparison (N/A) |

### `POST /evaluations/submit`

Body: `employeeId`, `periodId`, `comment`, `coachingPlan`, `coachingRequired`, `qualitativeScores[]`
(`kpiGroupId`, `score`, `notes`), `isDraft`. `data`: `evaluationId`, `employeeId`, `periodId`, `totalScore`,
`status` (`DRAFT` when `isDraft`, otherwise `CALCULATED`), `proposedTier`, `calculatedAt`, `message`.

The backend does not validate the length of the free text or the range of a qualitative score, so the frontend checks them before sending. Errors: 400 `MAKER_CANNOT_BE_CHECKER`; 403 `FORBIDDEN`; 404; 409 `CONFLICT` (period `LOCKED`/`CLOSED`); 409 `EVALUATION_DISPUTE_PENDING` (score under dispute).

Notes:

- The qualitative score entered on the form belongs to a group with `isQualitative = true`. When the profile has
  no such group the form has nothing to enter and the reviewer only writes the comment.
- "Quality ratio" and the "History" link have no data source or target screen yet (waiting for confirmation);
  the form does not show them. The transaction-trace links (SCR-PRF-05) are not part of this scope.
- Whether HR holds `evaluation:read` depends on the role grants in the database; this task does not change them.
