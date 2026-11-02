# Target assignment (Branch / Department / Employee)

Status: backend implemented (`POST /target-assignments`, `POST /target-assignments/{id}/allocations`,
`GET /target-assignments/{id}`, `POST /target-assignments/{id}/period-allocations`,
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
| Split a target | admin, or the caller's department is the parent target's owning department |
| Read one target | admin, or the caller's department owns the target (for an employee target: the employee's department) |
| Allocate to employees (row has `employeeId`) | additionally: every employee exists, is `ACTIVE`, and is inside the parent target's department tree (see "Allocation validation") |
| `GET /target-assignments/{id}/breakdown` | admin; a caller with `target:manage` (scope `Dept`) whose department is the owning department or one of its parents (e.g. a DM over a team); **or** the caller is the target's owning employee. Other employees of the same department get 403 (they must not see colleagues' targets) |
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
- Changing a value is a later "new version" endpoint (not part of this revision, nor are closing).

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

Children keep the parent's KPI and period, get `splitDimension = ORG` and inherit `businessObjectiveId`. For a `SUM`
KPI the database requires the children to total the parent's `targetValue` exactly (checked when the transaction
commits). The database accepts any descendant department or an employee inside the parent's organisation; the frontend
offers only the level directly below (branch to departments, department to teams and employees, team to employees).

## `GET /target-assignments/{id}`

`id` is the assignment id (GUID). `data`: `TargetAssignment`, 200.

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

### Working days / holidays / leave (limitation — not built in this change)

Working days are computed as **Monday-Friday within the period only**, via `IWorkingDayCalendar`
(`WeekdayWorkingDayCalendar`). **Public holidays and employee leave are NOT subtracted.** The Figma design
(SCR-STRAT-05 "Nghỉ phép giữa kỳ — MSG25", SCR-STRAT-03-D1 "16 (phép 5)", "... đã trừ lễ, cuối tuần, phép", BR-30)
expects both; until a holiday/leave calendar exists, breakdown values will run higher than the design's examples
on months containing a holiday or approved leave. Future work (separate change): `public_holidays` (date, name,
branch nullable) and `employee_leaves` (employee_id, from, to, status `APPROVED`) tables + migration; a
`DbWorkingDayCalendar` replacing `WeekdayWorkingDayCalendar` behind the same `IWorkingDayCalendar` interface (no
caller changes needed); the MSG25 rule (leave mid-period redistributes the not-yet-achieved remainder over the
remaining working days, period total unchanged); and a `leaveDays` field in the breakdown response.

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
  "feedbackStatus": null
}
```

- Exactly one of `departmentId` / `employeeId` is set; the other is `null`.
- `splitDimension`: `ORG` for a child, `null` for a root. `status`: `DRAFT | ACTIVE | SUPERSEDED | CLOSED`.
- Decimals are numbers with up to 4 decimal places. IDs are GUID strings; timestamps are ISO 8601 with offset.
- `feedbackStatus`: `NOT_VIEWED | CONFIRMED | COMMENTED` for employee-owned rows (latest feedback wins; `CONFIRMED`
  only when that confirmation is newer than `updatedAt`), always `null` for department-owned rows. Populated by
  `GET /target-assignments` (so the DM grid can show per-row status) and `GET /target-assignments/mine`.

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | malformed body; missing required field; `changeReason` too long; empty `allocations`; duplicate `evaluationPeriodId` in period-allocations; `evaluationPeriodId` is not a descendant of the parent period; `status` not UPPER_SNAKE |
| 401 | `UNAUTHORIZED` | missing or invalid token |
| 403 | `FORBIDDEN` | missing `target:manage`, root target without scope `All`, target outside the caller's department; `departmentId` outside the caller's accessible scope in GET list |
| 404 | `NOT_FOUND` | unknown assignment id; unknown `departmentId` in GET list; unknown `evaluationPeriodId` in period-allocations |
| 409 | `PERIOD_LOCKED` | the target's period (create, split), the parent's period or any `evaluationPeriodId` in period-allocations has status `LOCKED` or `CLOSED` (see `PERIOD_LOCK_CONTRACT.md`) |
| 409 | `CONFLICT` | allocation to a department or employee that already has an ACTIVE target for the same KPI and period; confirming a target already confirmed since its last change |
| 500 | `INTERNAL_ERROR` | a database rule was violated (hierarchy, `SUM` total, value range, unknown KPI/period/department) |
