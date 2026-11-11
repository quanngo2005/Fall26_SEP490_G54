# Target assignment (Branch / Department / Employee)

Status: backend implemented (`POST /target-assignments`, `POST /target-assignments/{id}/allocations`,
`GET /target-assignments/{id}`, `PUT /target-assignments/{id}/allocations`, `POST /target-assignments/{id}/revisions`,
`POST /target-assignments/{id}/period-allocations`,
`GET /target-assignments`, `GET /target-assignments/{id}/breakdown`, `GET /target-assignments/mine`,
`POST /target-assignments/{id}/feedback/confirm`, `POST /target-assignments/{id}/feedback/comments`,
`GET /target-assignments/{id}/feedback`). Frontend client: **planned, not implemented**. Routes use the backend
origin with no API prefix; all responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`). Enums are UPPER_SNAKE
strings matching PostgreSQL names.

Employee-facing routes (`mine`, `{id}/breakdown`, `{id}/feedback/confirm`, `{id}/feedback/comments`, `{id}/feedback`) are served by a
second controller, `MyTargetAssignmentsController`, mounted on the same `target-assignments` route but requiring
only `[Authorize]` (any authenticated account), not `target:manage`. The caller's `employeeId` is always derived
server-side from the authenticated account (never trusted from the request). This avoids inventing/seeding a new
resource-scoped permission for Teller/SO employees to read and respond to their own targets; see "Authorization".

A target assignment gives one KPI a target value for one evaluation period and one owner. The owner is a
**department node** (`BRANCH`, `DEPARTMENT` or `TEAM`) or a single **employee**. Targets form a tree: an administrator
assigns the root target of a KPI to a branch, then each owner splits its target across the level below it
(`splitDimension = ORG`).

## Division of responsibility

The backend is a thin writer. It does **not** re-implement rules the database already enforces, and it does not return
data the frontend already has (names, codes, units, aggregation type, allocated/remaining totals).

| Concern | Owner |
| --- | --- |
| Who may call (`target:manage`), root = admin, parent owned by the caller's department | backend |
| Required fields, `changeReason` <= 1000 chars | backend (model validation, 400) |
| Hierarchy (child inside the parent's organisation, no cycle, same KPI/period), `SUM` children total the parent, one ACTIVE target per KPI/period/owner, `targetValue >= 0`, 18/4 decimal precision | database (constraints and triggers) |
| KPI is ACTIVE, period is OPEN, owner is a branch / ACTIVE, one level below, employee is working, no duplicate rows, row limits, `SUM` preview, decimal places | frontend |
| Names, codes, units, `allocatedValue` / `remainingValue` | frontend (from the KPI, period, department and employee lookups it already loads) |

A database rule violation surfaces as an unhandled database error (HTTP 500); the frontend must validate first. There
is no request cancellation: the frontend blocks repeated requests.

## Authorization

`[HasPermission("target", "manage", "Dept")]` on the controller. Seeded roles: `ADMIN` has scope `All`, `MANAGER`
has scope `Department`. Beyond that, the BLL checks:

| Operation | Required |
| --- | --- |
| Create a **root** target | `target:manage` with scope `All` (admin) |
| Split a target | admin, or the caller's department is the parent target's owning department **or one of its parents** (a department manager can split the targets of the teams below, e.g. the team's employees) |
| Read one target | admin, or the owning department (for an employee target: the employee's department) is the caller's department or one below it |
| Allocate to employees (row has `employeeId`) | additionally: every employee exists, is `ACTIVE`, and is inside the parent target's department tree (see "Allocation validation") |
| `GET /target-assignments/{id}/breakdown` | admin; a caller with `target:manage` (scope `Dept`) whose department is the owning department or one of its parents (e.g. a DM over a team); **or** the caller is the target's owning employee. Other employees of the same department get 403 (they must not see colleagues' targets) |
| `GET /target-assignments/employees/{employeeId}/suggested-daily` | same as the breakdown, decided on the employee: admin; a `target:manage` (scope `Dept`) caller whose department is the employee's department or a parent of it; or the employee themselves |
| `GET /target-assignments/mine`, `POST .../feedback/confirm`, `POST .../feedback/comments` | any authenticated account with an employee record; the target must be owned by that employee (403 otherwise) |
| `GET /target-assignments/{id}/feedback` | same as breakdown: admin, a `target:manage` (Dept) caller of the owning department or a parent of it, or the owning employee |

Otherwise 403 `FORBIDDEN`.

## Allocation validation (`POST /target-assignments/{id}/allocations`)

Beyond the hierarchy/`SUM`/uniqueness rules the database enforces, the BLL validates each row **before** any
database call (so a bad row is `400`/`404`, not an unhandled `500`):

| Rule | Error |
| --- | --- |
| Each row has exactly one of `departmentId` / `employeeId` | 400 `VALIDATION_ERROR` |
| No duplicate `departmentId` across rows | 400 `VALIDATION_ERROR` |
| No duplicate `employeeId` across rows | 400 `VALIDATION_ERROR` |
| An `employeeId` row requires the parent target to be department-owned | 400 `VALIDATION_ERROR` |
| Every `employeeId` must reference an existing employee | 404 `NOT_FOUND` |
| The employee's `status` must be `ACTIVE` | 400 `VALIDATION_ERROR` |
| The employee's department must be the parent's department or a descendant of it | 400 `VALIDATION_ERROR` |

The `SUM`-equals-parent check, value range and uniqueness constraints remain database-enforced (500 on violation,
unchanged from the existing behavior).

## Common rules

- Targets are created **ACTIVE** at once (no draft/approval step): `version = 1`, `assignedAt = approvedAt = now`.
- Changing a value goes through `PUT /target-assignments/{id}/allocations` (new version); closing is not part of this revision.

## `POST /target-assignments` (root target)

Assigns the root target of a KPI and period to a **branch**. Returns **201** with a `Location` header pointing at
`GET /target-assignments/{id}`; the envelope `status` is 201. `data`: `TargetAssignment`.

| Field | Type | Rules |
| --- | --- | --- |
| `kpiDefinitionId` | GUID | required |
| `evaluationPeriodId` | GUID | required |
| `departmentId` | GUID | required; the branch |
| `targetValue` | decimal | required |
| `changeReason` | string or null | optional, at most 1000 characters |

```json
{
  "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
  "evaluationPeriodId": "5eed0000-0000-0000-0004-000000000001",
  "departmentId": "5eed0000-0000-0000-0001-000000000001",
  "targetValue": 1000000,
  "changeReason": "Q4 plan"
}
```

## `POST /target-assignments/{id}/allocations` (split)

Splits the target `{id}` by creating **every child in one transaction** (all or nothing). Returns **201** with a
`Location` header pointing at `GET /target-assignments/{id}` (the parent); the envelope `status` is 201.
`data`: `TargetAssignment[]` in request order.

| Field | Type | Rules |
| --- | --- | --- |
| `allocations` | array | required, at least 1 row |
| `allocations[].departmentId` | GUID or null | child department; exactly one of `departmentId` / `employeeId` per row (the database rejects both or neither) |
| `allocations[].employeeId` | GUID or null | child employee |
| `allocations[].targetValue` | decimal | required |
| `changeReason` | string or null | optional, at most 1000 characters, stored on every child |

```json
{
  "allocations": [
    { "departmentId": "5eed0000-0000-0000-0001-000000000002", "employeeId": null, "targetValue": 600000 },
    { "departmentId": "5eed0000-0000-0000-0001-000000000003", "employeeId": null, "targetValue": 400000 }
  ],
  "changeReason": "Q4 split"
}
```

Children keep the parent's KPI and period, get `splitDimension = ORG` and inherit `businessObjectiveId`. An owner that already held a share of the same KPI and period (now superseded) gets the next `version`, since the database allows each version once per owner. For a `SUM`
KPI the database requires the children to total the parent's `targetValue` exactly (checked when the transaction
commits). The database accepts any descendant department or an employee inside the parent's organisation; the frontend
offers only the level directly below (branch to departments, department to teams and employees, team to employees).

## `GET /target-assignments/{id}`

`id` is the assignment id (GUID). `data`: `TargetAssignment`, 200.

## `PUT /target-assignments/{id}/allocations` (adjust a split)

Changes the values of the children that `POST .../allocations` created, for example after an employee's comment. Returns
**200**, `data`: `TargetAssignment[]` holding only the **new versions**, in request order.

| Field | Type | Rules |
| --- | --- | --- |
| `allocations` | array | required; must list **every** owner (department / employee) that already has an ACTIVE child of `{id}`, each once, with its new `targetValue`; owners not yet allocated may be added and are validated like in the split |
| `allocations[].departmentId` / `employeeId` | GUID or null | exactly one per row, as in the split |
| `changeReason` | string | **required**, at most 1000 characters |

- A child whose value is unchanged is left alone and keeps its feedback (an employee who confirmed stays confirmed). Every changed child becomes `SUPERSEDED` and is replaced by an ACTIVE row with
  `Version + 1` and the new `changeReason` (R4); all of it happens in one transaction.
- A changed child that was itself split (a team whose employees already have shares) takes its ACTIVE descendants with it: they are superseded too and must be split again under the new version.
- The new row has no feedback yet, so an employee-owned row comes back with `feedbackStatus = NOT_VIEWED` and the employee
  confirms or comments again. Feedback on the superseded version stays under its own id.
- Authorization and period lock are the same as for the split (the caller's department owns `{id}`; `PERIOD_LOCKED` otherwise).
- 400 `VALIDATION_ERROR` when an allocated owner is left out, a row has both or neither owner, an owner repeats, or
  `changeReason` is missing. As for the split, a `SUM` KPI must still total the parent: the database enforces it (500 on a
  violation) and the frontend validates it first.

```json
{
  "allocations": [
    { "employeeId": "5eed0000-0000-0000-0007-000000000004", "targetValue": 350000 },
    { "employeeId": "5eed0000-0000-0000-0007-000000000005", "targetValue": 250000 }
  ],
  "changeReason": "Adjusted after the employee's comment"
}
```

## `POST /target-assignments/{id}/revisions` (mid-period target change, BR-15)

Changes the **value** of the ACTIVE target `{id}` partway through its period, instead of overwriting it: the current
version is frozen (`SUPERSEDED`, `effectiveTo = effectiveDate - 1`) and a new version (`Version + 1`) takes over
`ACTIVE` from `effectiveDate`. Returns **201** with a `Location` header pointing at `GET /target-assignments/{id}`
using the **new** version's id; the envelope `status` is 201. `data`: `TargetAssignment[]`, the new version first,
then its re-created direct children (if any), in request order.

| Field | Type | Rules |
| --- | --- | --- |
| `targetValue` | decimal | required, `>= 0` |
| `effectiveDate` | date (`yyyy-MM-dd`) | required; must be strictly after the current version's `effectiveFrom` and at most the period's `endDate` |
| `reason` | string | required, at most 500 characters |
| `childAllocations` | array or absent | required (non-empty) when `{id}` has ACTIVE children; must be empty/absent otherwise |
| `childAllocations[].departmentId` / `employeeId` | GUID or null | exactly one per row, as in the split |
| `childAllocations[].targetValue` | decimal | required |

```json
{
  "targetValue": 120,
  "effectiveDate": "2026-11-15",
  "reason": "Nghỉ phép",
  "childAllocations": [
    { "employeeId": "5eed0000-0000-0000-0007-000000000004", "targetValue": 60 },
    { "employeeId": "5eed0000-0000-0000-0007-000000000005", "targetValue": 60 }
  ]
}
```

- Authorization and period lock are the same as the split/adjust (`target:manage`, caller's department owns `{id}`'s
  department / the employee's department; `PERIOD_LOCKED` when the period is `LOCKED`/`CLOSED`).
- **Hard lock (BR-29)**: when the evaluation period's month-granularity watermark (`hardLockedThrough`) is set and
  `effectiveDate` falls in or before that watermark's month, the request is rejected with 409 `MONTH_HARD_LOCKED`
  regardless of the period's own status.
- `childAllocations` must list **every** owner that already has an ACTIVE child of `{id}`, each once, with its new
  `targetValue` (owners not yet allocated may be added, validated like a split); the total must equal `targetValue`
  exactly (BR-24), else 400 `VALIDATION_ERROR` / `TARGET_ALLOCATION_NOT_100_PERCENT`.
- Every ACTIVE descendant of `{id}` (children **and everything below them**) is frozen the same way (`SUPERSEDED`,
  `effectiveTo = effectiveDate - 1`). Only the **direct children** are re-created as new versions (`PreviousVersionId`
  pointing at the child they replace, same owner, same `effectiveFrom`); grandchildren and below are **not**
  re-created — whoever owns them must revise their own target again afterward.
- The new version's `ChangedByEmployeeId` is the caller's own employee (null if the account has none); `ChangeReason`
  is `reason` on both the new version and its re-created children.
- A transaction dated **before** `effectiveDate` (e.g. a late-approved result) is still recorded against the version
  that was in effect on that date, not the new one — see `VerifiedResultRecorder.FindActiveTargetAsync`.
- After saving, every `employeeId` of the new version and of its new children is notified
  (`NotificationType.TargetRevised`, `relatedType = TARGET`, `relatedId` = the new version's id); a department-owned
  row is not translated to its manager (no such lookup exists yet).
- 400 `VALIDATION_ERROR` when `effectiveDate` is not after the current `effectiveFrom` or past the period's
  `endDate`, `reason` is missing/too long, `targetValue < 0`, `childAllocations` is missing/non-empty when it
  shouldn't be, an allocated owner is left out, a row has both or neither owner, an owner repeats, or the total
  does not equal `targetValue`. 409 `TARGET_INVALID_STATE` when `{id}` is not `ACTIVE`.

## `POST /target-assignments/{id}/period-allocations` (split by period)

Splits the target `{id}` across evaluation periods by creating **every child in one transaction** (all or nothing).
Returns **201** with a `Location` header pointing at `GET /target-assignments/{id}` (the parent); the envelope
`status` is 201. `data`: `TargetAssignment[]` in request order.

| Field | Type | Rules |
| --- | --- | --- |
| `allocations` | array | required, at least 1 row |
| `allocations[].evaluationPeriodId` | GUID | required; must be distinct within request; must exist and be a descendant of the parent's period |
| `allocations[].targetValue` | decimal | required |
| `changeReason` | string or null | optional, at most 1000 characters, stored on every child |

```json
{
  "allocations": [
    { "evaluationPeriodId": "5eed0000-0000-0000-000f-000000000008", "targetValue": 300 },
    { "evaluationPeriodId": "5eed0000-0000-0000-000f-000000000009", "targetValue": 300 },
    { "evaluationPeriodId": "5eed0000-0000-0000-000f-00000000000a", "targetValue": 300 },
    { "evaluationPeriodId": "5eed0000-0000-0000-000f-00000000000b", "targetValue": 300 }
  ],
  "changeReason": "Split year into quarters"
}
```

Children keep the parent's KPI and organisation owner (department/employee), get `splitDimension = PERIOD` and
inherit `businessObjectiveId`. For a `SUM` KPI the database requires the children to total the parent's
`targetValue` exactly (checked when the transaction commits). The caller must be admin or own the parent target's
owning department.

## `GET /target-assignments`

Lists target assignments with optional filters. Returns **200** with `data`: `TargetAssignment[]`, sorted by
`evaluationPeriodId`, then `parentAssignmentId` (null first), then `createdAt`.

| Query parameter | Type | Rules |
| --- | --- | --- |
| `kpiDefinitionId` | GUID or absent | filter by KPI; absent = all KPIs |
| `evaluationPeriodId` | GUID or absent | filter by period; absent = all periods |
| `departmentId` | GUID or absent | filter by department owner; absent = all departments accessible to caller |
| `status` | UPPER_SNAKE or absent | filter by status (e.g., `ACTIVE`, `DRAFT`, `CLOSED`); absent = all statuses |

**Scope**:
- Admin (`target:manage` scope `All`): sees all targets.
- Non-admin: sees only targets where the owning department (for an employee target: the employee's department) is
  the caller's department or a descendant thereof.
- If `departmentId` is supplied and outside the caller's accessible scope, return 403 `FORBIDDEN`.
- If `departmentId` is supplied and does not exist, return 404 `NOT_FOUND`.

```json
{
  "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
  "evaluationPeriodId": "5eed0000-0000-0000-000f-000000000001",
  "departmentId": "5eed0000-0000-0000-0001-000000000001",
  "status": "ACTIVE"
}
```

## `GET /target-assignments/{id}/breakdown` (working-day split)

Read-only, **computed on the fly, not stored**. Splits the target's `targetValue` across calendar months in
proportion to each month's working days (SCR-STRAT-03-D1), then splits each month evenly across its working days
(SCR-STRAT-05). Leftover units go to the earliest months, then to the earliest days of each month. The day split is
then aggregated into the requested granularity (weeks are sums of days and may span two months).

| Query parameter | Type | Rules |
| --- | --- | --- |
| `granularity` | `MONTH` \| `WEEK` \| `DAY`, optional | default `MONTH`; any other value is 400 `VALIDATION_ERROR` |

```json
{
  "targetAssignmentId": "5eed0000-0000-0000-0015-000000000002",
  "periodStart": "2026-10-01",
  "periodEnd": "2026-12-31",
  "totalValue": 6400000000,
  "workingDays": 64,
  "granularity": "MONTH",
  "items": [
    { "start": "2026-10-01", "end": "2026-10-30", "workingDays": 22, "value": 2200000000 },
    { "start": "2026-11-02", "end": "2026-11-30", "workingDays": 21, "value": 2100000000 },
    { "start": "2026-12-01", "end": "2026-12-31", "workingDays": 21, "value": 2100000000 }
  ]
}
```

- `items[].start` / `items[].end` are the first/last **working day** actually present in that bucket (not the raw
  calendar month/week boundary), so the first bucket can start mid-month/mid-week when the period itself does.
- Weeks are ISO weeks (Monday start).
- Whole-number KPIs (contracts, customers, ...) split in integer units. Example: 30 contracts over Q4/2026
  (22 + 21 + 23 weekdays) -> months 11 / 9 / 10; inside October the 11 contracts go 1 per day on the first 11
  working days and 0 on the other 11.
  Other KPIs (VNĐ, %, ...) are rounded to 4 decimal places and the 0.0001 remainder goes the same way. See
  "Whole-number KPI detection" below for how this is decided — there is no explicit KPI field for it today.
- The period total is always preserved exactly (sum of `items[].value` == `totalValue`).

### Whole-number KPI detection (documented assumption)

`KpiDefinition` has no explicit "decimal places" or "is integer" field. The backend treats a KPI as whole-number
when its `unit` (case-insensitive, Vietnamese or English) is one of: "Hợp đồng", "Khách hàng", "Giao dịch",
"Contract(s)", "Customer(s)", "Cái", "Lần" (see `TargetAssignmentService.WholeNumberUnits`). Any other unit (e.g.
"VNĐ", "%") is treated as decimal, rounded to 4 dp. **This is an assumption**, not a confirmed business rule; if a
dedicated KPI field is added later (e.g. `decimalPlaces` or `isWholeNumber`), switch to it instead.

### Working days / holidays / leave

Working days are computed as **Monday-Friday within the period only**, via `IWorkingDayCalendar`
(`WeekdayWorkingDayCalendar`). **Public holidays are NOT subtracted**, so a month with a holiday has more working
days here than in a design that already removes them (Q4/2026 has 22 + 21 + 23 = 66 weekdays). Approved leave does
**not** change a target or its split (BR-30, `docs/BPMS_AI_Agent_Context.md`): the earlier idea of redistributing the
not-yet-achieved remainder over the remaining days (MSG25) and a `leaveDays` field is dropped, not planned.
A holiday calendar, if added later, replaces `WeekdayWorkingDayCalendar` behind the same interface with no caller change.

## `GET /target-assignments/employees/{employeeId}/suggested-daily` (planned per-month / per-week / per-day target)

Read-only, **computed on the fly, not stored**. For one employee and one evaluation period it returns every `ACTIVE`
target the employee owns in that period, each split exactly like `GET /target-assignments/{id}/breakdown` (same
calculator, same working days, same whole-number rule), plus a per-day and per-week figure for display. It does **not**
use actual progress: it is the plan, not what is left to do (SCR-STRAT-05 right panel, read only).

| Parameter | Type | Rules |
| --- | --- | --- |
| `employeeId` (path) | GUID | 404 `NOT_FOUND` if the employee does not exist |
| `evaluationPeriodId` (query) | GUID, required | missing or malformed is 400 `VALIDATION_ERROR`; 404 `NOT_FOUND` if the period does not exist |

Authorization: same as the breakdown (admin; a caller with `target:manage` scope `Dept` whose department is the
employee's department or one of its parents; or the employee themselves). Another employee of the same department,
or a manager of another department, gets 403 `FORBIDDEN`. A `LOCKED` / `CLOSED` period is still readable.

```json
{
  "employeeId": "5eed0000-0000-0000-0007-000000000004",
  "evaluationPeriodId": "5eed0000-0000-0000-000f-000000000003",
  "periodStart": "2026-10-01",
  "periodEnd": "2026-12-31",
  "workingDays": 66,
  "items": [
    {
      "targetAssignmentId": "5eed0000-0000-0000-0015-000000000008",
      "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
      "kpiCode": "HDV",
      "unit": "VNĐ",
      "isWholeNumber": false,
      "targetValue": 6600,
      "plannedDaily": 100,
      "plannedWeekly": 500,
      "months": [
        { "month": "2026-10", "workingDays": 22, "value": 2200, "daily": 100, "weekly": 500 },
        { "month": "2026-11", "workingDays": 21, "value": 2100, "daily": 100, "weekly": 500 },
        { "month": "2026-12", "workingDays": 23, "value": 2300, "daily": 100, "weekly": 500 }
      ]
    }
  ]
}
```

- `items` is sorted by `kpiCode`. It is `[]` (200, not 404) when the employee has no `ACTIVE` target in the period.
  Only targets owned directly by the employee for exactly that period are returned: a quarter and its month rows are
  different periods, so ask for the period you want to see.
- `months[].value` is the same split as `GET .../breakdown?granularity=MONTH` (their sum is `targetValue`).
- `daily` = `value / workingDays` and `weekly` = `daily x 5`, rounded to 4 decimal places. They are **display
  figures**: the stored split is in whole units, so a whole-number KPI can show a fraction (for example 17 over 22 days).
- `plannedDaily` = `targetValue / workingDays` of the whole period, `plannedWeekly` = `plannedDaily x 5`.
- Only a `SUM` KPI where more is better is spread over the days. For any other KPI (`AVG`, `RATIO`, `NO_AGGREGATION`,
  `LOWER_BETTER`, `ON_TARGET`) `plannedDaily` and `plannedWeekly` are `null` and `months` is `[]`; `targetValue` is still
  returned, so the screen shows N/A instead of a made-up number.
- Dates are `YYYY-MM-DD`; decimal values have at most 4 decimal places.

## `GET /target-assignments/mine` (employee's own targets)

Returns the caller's own employee-owned target assignments (ACTIVE only), each with `feedbackStatus`. 403 if the
calling account has no employee record.

| Query parameter | Type | Rules |
| --- | --- | --- |
| `evaluationPeriodId` | GUID or absent | filter by period |
| `kpiDefinitionId` | GUID or absent | filter by KPI |

`data`: `TargetAssignment[]` (see below), each with `feedbackStatus` set (never `null`, since every row here is
employee-owned).

## `POST /target-assignments/{id}/feedback/confirm`

The owning employee confirms the target. Returns **201**, `data`: `TargetAssignmentFeedback`. No request body.

- 403 `FORBIDDEN` if the caller is not the target's owning employee.
- 409 `CONFLICT` if a `CONFIRM` feedback row already exists that is newer than or equal to the target's
  `updatedAt` (i.e. already confirmed since the last change). Confirming again after the target itself changes
  (its `updatedAt` moves forward) is allowed.

## `POST /target-assignments/{id}/feedback/comments`

The owning employee leaves a comment. Returns **201**, `data`: `TargetAssignmentFeedback`.

| Field | Type | Rules |
| --- | --- | --- |
| `comment` | string | required, 1-500 characters |

```json
{ "comment": "Số liệu quý trước chưa khớp, cần kiểm tra lại." }
```

- 403 `FORBIDDEN` if the caller is not the target's owning employee.
- 400 `VALIDATION_ERROR` if `comment` is missing, blank, or longer than 500 characters.

## `GET /target-assignments/{id}/feedback`

Feedback history for a target, newest first. `data`: `TargetAssignmentFeedback[]`.

## `TargetAssignmentFeedback`

```json
{
  "id": "5eed0000-0000-0000-0016-000000000001",
  "targetAssignmentId": "5eed0000-0000-0000-0015-000000000002",
  "employeeId": "5eed0000-0000-0000-0007-000000000004",
  "type": "CONFIRM",
  "comment": null,
  "createdAt": "2026-10-23T08:00:00+00:00"
}
```

- `type`: `CONFIRM` | `COMMENT`. `comment` is set only for `COMMENT` rows (otherwise `null`).

## `TargetAssignment`

```json
{
  "id": "5eed0000-0000-0000-0015-000000000002",
  "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
  "evaluationPeriodId": "5eed0000-0000-0000-0004-000000000001",
  "departmentId": "5eed0000-0000-0000-0001-000000000002",
  "employeeId": null,
  "parentAssignmentId": "5eed0000-0000-0000-0015-000000000001",
  "splitDimension": "ORG",
  "version": 1,
  "targetValue": 600000,
  "status": "ACTIVE",
  "assignedAt": "2026-10-23T08:00:00+00:00",
  "approvedAt": "2026-10-23T08:00:00+00:00",
  "changeReason": "Q4 split",
  "createdAt": "2026-10-23T08:00:00+00:00",
  "updatedAt": "2026-10-23T08:00:00+00:00",
  "feedbackStatus": null,
  "feedbackAt": null,
  "effectiveFrom": "2026-10-01",
  "effectiveTo": null,
  "previousVersionId": null,
  "changedByEmployeeId": null
}
```

- Exactly one of `departmentId` / `employeeId` is set; the other is `null`.
- `splitDimension`: `ORG` for a child, `null` for a root. `status`: `DRAFT | ACTIVE | SUPERSEDED | CLOSED`.
- Decimals are numbers with up to 4 decimal places. IDs are GUID strings; timestamps are ISO 8601 with offset.
- `feedbackStatus`: `NOT_VIEWED | CONFIRMED | COMMENTED` for employee-owned rows (latest feedback wins; `CONFIRMED`
  only when that confirmation is newer than `updatedAt`), always `null` for department-owned rows. Populated by
  `GET /target-assignments` (so the DM grid can show per-row status) and `GET /target-assignments/mine`.
- `feedbackAt`: ISO 8601 time of the latest feedback, set only when `feedbackStatus` is `CONFIRMED` or `COMMENTED`; `null` otherwise (`NOT_VIEWED`, a confirmation that predates the last update, and department-owned rows). Populated by the same endpoints as `feedbackStatus`.
- `effectiveFrom` (date, BR-15): first day this version applies; `period.startDate` unless it was created by a mid-period revision.
- `effectiveTo` (date or null): last day this version applied; `null` while `ACTIVE`, or on a row `SUPERSEDED` before
  BR-15 existed (such a row is never used for scoring — see `FindActiveTargetAsync` / `LoadTargetsAsync`).
- `previousVersionId` (GUID or null): the version this one replaced mid-period, set by `POST .../revisions`; otherwise `null`. Not a database foreign key (see the entity's doc comment for why).
- `changedByEmployeeId` (GUID or null): the employee who requested the mid-period change; `null` for every row except one created by `POST .../revisions`.

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | malformed body; missing required field; `changeReason` too long; empty `allocations`; duplicate `evaluationPeriodId` in period-allocations; `evaluationPeriodId` is not a descendant of the parent period; `status` not UPPER_SNAKE; `POST .../revisions`: `effectiveDate` not after the current `effectiveFrom` or past the period end, `reason` missing/too long, `targetValue < 0`, `childAllocations` wrong shape for whether `{id}` has ACTIVE children, or its total does not equal `targetValue` |
| 401 | `UNAUTHORIZED` | missing or invalid token |
| 403 | `FORBIDDEN` | missing `target:manage`, root target without scope `All`, target outside the caller's department; `departmentId` outside the caller's accessible scope in GET list |
| 404 | `NOT_FOUND` | unknown assignment id; unknown `departmentId` in GET list; unknown `evaluationPeriodId` in period-allocations |
| 409 | `PERIOD_LOCKED` | the target's period (create, split, revise), the parent's period or any `evaluationPeriodId` in period-allocations has status `LOCKED` or `CLOSED` (see `PERIOD_LOCK_CONTRACT.md`) |
| 409 | `MONTH_HARD_LOCKED` | `POST .../revisions`: `effectiveDate` falls in or before the period's hard-lock watermark month (BR-29) |
| 409 | `TARGET_INVALID_STATE` | `POST .../revisions`: `{id}` is not `ACTIVE` |
| 409 | `CONFLICT` | allocation to a department or employee that already has an ACTIVE target for the same KPI and period; confirming a target already confirmed since its last change |
| 500 | `INTERNAL_ERROR` | a database rule was violated (hierarchy, `SUM` total, value range, unknown KPI/period/department) |
