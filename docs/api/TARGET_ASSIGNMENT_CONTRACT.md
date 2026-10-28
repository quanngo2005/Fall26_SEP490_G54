# Target assignment (Branch / Department / Employee)

Status: backend implemented (`POST /target-assignments`, `POST /target-assignments/{id}/allocations`,
`GET /target-assignments/{id}`, `POST /target-assignments/{id}/period-allocations`,
`GET /target-assignments`). Frontend client: **planned, not implemented**. Routes use the backend origin with no
API prefix; all responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`). Enums are UPPER_SNAKE strings matching
PostgreSQL names.

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

Otherwise 403 `FORBIDDEN`.

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
  "updatedAt": "2026-10-23T08:00:00+00:00"
}
```

- Exactly one of `departmentId` / `employeeId` is set; the other is `null`.
- `splitDimension`: `ORG` for a child, `null` for a root. `status`: `DRAFT | ACTIVE | SUPERSEDED | CLOSED`.
- Decimals are numbers with up to 4 decimal places. IDs are GUID strings; timestamps are ISO 8601 with offset.

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | malformed body; missing required field; `changeReason` too long; empty `allocations`; duplicate `evaluationPeriodId` in period-allocations; `evaluationPeriodId` is not a descendant of the parent period; `status` not UPPER_SNAKE |
| 401 | `UNAUTHORIZED` | missing or invalid token |
| 403 | `FORBIDDEN` | missing `target:manage`, root target without scope `All`, target outside the caller's department; `departmentId` outside the caller's accessible scope in GET list |
| 404 | `NOT_FOUND` | unknown assignment id; unknown `departmentId` in GET list; unknown `evaluationPeriodId` in period-allocations |
| 500 | `INTERNAL_ERROR` | a database rule was violated (hierarchy, `SUM` total, duplicate, value range, unknown KPI/period/department/employee) |
