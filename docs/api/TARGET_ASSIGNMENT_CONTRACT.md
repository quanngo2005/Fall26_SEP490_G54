# Target assignment (Branch / Department / Employee)

Status: backend implemented (`POST /target-assignments`, `POST /target-assignments/{id}/allocations`,
`GET /target-assignments/{id}`). Frontend client: **planned, not implemented**. Routes use the backend origin with no
API prefix; all responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`). Enums are UPPER_SNAKE strings matching
PostgreSQL names.

A target assignment gives one KPI a target value for one evaluation period and one owner. The owner is a
**department node** (`BRANCH`, `DEPARTMENT` or `TEAM` - there is no separate branch resource) or a single
**employee**. Targets form a tree: an administrator assigns the root target of a KPI to a branch, then each owner
splits its target across the level directly below it (`splitDimension = ORG`).

Not part of this revision (planned): listing, editing a value (new version), closing/superseding, `PERIOD` splits,
re-splitting an already split target.

## Authorization

`[HasPermission("target", "manage", "Dept")]` on the controller. Seeded roles: `ADMIN` has scope `All`, `MANAGER`
has scope `Department`. The BLL re-checks the caller's scope (and that the account is active and unlocked):

| Operation | Required |
| --- | --- |
| Create a **root** target | scope `All` (admin) only |
| Split a target | the parent target's owning department must be visible to the caller |
| Read one target | the owning department (or the employee's department) must be visible to the caller |

Visible departments (same model as `DEPARTMENT_CONTRACT.md`, using the caller's `target:manage` scope):

| Granted scope | Visible departments |
| --- | --- |
| `All` / `System` / full access | every department |
| `Branch` | subtree of the nearest `BRANCH` ancestor-or-self of the caller's department |
| `Department` / `Dept` | subtree of the caller's own department |

Outside the visible set: 403 `FORBIDDEN`.

## Common rules

- Targets are created **ACTIVE** at once (no draft/approval step): `version = 1`, `assignedAt = approvedAt = now`.
- The KPI must be `ACTIVE` and the evaluation period `OPEN`.
- `targetValue`: required, `>= 0` (0 is valid), at most 4 decimal places and 14 integer digits.
- **One target per owner**: if an assignment (any status) already exists for the same KPI, period and owner the
  request is 409. Changing a value is a later "new version" endpoint.

## `POST /target-assignments` (root target)

Assigns the root target of a KPI and period to a **branch**. Returns **201** with a `Location` header pointing at
`GET /target-assignments/{id}`; the envelope `status` is 201. `data`: `TargetAssignment`.

| Field | Type | Rules |
| --- | --- | --- |
| `kpiDefinitionId` | GUID | required |
| `evaluationPeriodId` | GUID | required |
| `departmentId` | GUID | required; must be an `ACTIVE` department of level `BRANCH` |
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

Splits the ACTIVE target `{id}` across the level directly below its owner, creating **every child in one
transaction** (all or nothing). Returns **201** with a `Location` header pointing at `GET /target-assignments/{id}`
(the parent); the envelope `status` is 201. `data`: `{ "parent": TargetAssignment, "children": TargetAssignment[] }`
(`children` in request order, `parent` re-read so `allocatedValue` / `remainingValue` are current).

| Field | Type | Rules |
| --- | --- | --- |
| `allocations` | array | required, 1 to 200 rows, each owner at most once |
| `allocations[].departmentId` | GUID or null | child department. Exactly one of `departmentId` / `employeeId` per row |
| `allocations[].employeeId` | GUID or null | child employee (`ACTIVE` or `ON_LEAVE`) |
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

### Hierarchy rules

The parent must be owned by a department (a target owned by an employee cannot be split). Each child is the level
**directly below** the parent owner; skipping a level is 400.

| Parent owner | Allowed child owners |
| --- | --- |
| `BRANCH` | departments whose `parentDepartmentId` is the branch |
| `DEPARTMENT` | teams whose `parentDepartmentId` is that department, **and/or** employees whose `departmentId` is that department |
| `TEAM` | employees whose `departmentId` is that team |

A branch cannot give an employee a target directly; it must split to departments first. Children keep the parent's KPI
and period, get `splitDimension = ORG` and inherit `businessObjectiveId`.

### Sum rule

When the KPI `aggregationType` is `SUM` the children must **total the parent's `targetValue` exactly**, otherwise 422
`TARGET_ALLOCATION_SUM_MISMATCH` and nothing is created. For `RATIO`, `AVG` and `NO_AGGREGATION` KPIs the sum is not
checked (each child usually carries the same level). The database enforces the same sum rule, so a target is split in
a single request. A target can be split **once**: a second request is 409 (re-splitting is planned).
The parent row is locked while splitting, so concurrent requests on the same parent cannot both succeed.

## `GET /target-assignments/{id}`

`id` is the assignment id (GUID). `data`: `TargetAssignment`, 200.

## `TargetAssignment`

```json
{
  "id": "5eed0000-0000-0000-0015-000000000002",
  "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
  "kpiCode": "KPI-REV",
  "kpiName": "Doanh thu",
  "kpiUnit": "VND",
  "kpiAggregationType": "SUM",
  "evaluationPeriodId": "5eed0000-0000-0000-0004-000000000001",
  "periodCode": "2026-Q4",
  "periodName": "Quý 4/2026",
  "ownerType": "DEPARTMENT",
  "departmentId": "5eed0000-0000-0000-0001-000000000002",
  "departmentCode": "CN-HN-KD",
  "departmentName": "Phòng Kinh doanh",
  "departmentLevelType": "DEPARTMENT",
  "employeeId": null,
  "employeeCode": null,
  "employeeName": null,
  "parentAssignmentId": "5eed0000-0000-0000-0015-000000000001",
  "splitDimension": "ORG",
  "version": 1,
  "targetValue": 600000,
  "allocatedValue": 0,
  "remainingValue": 600000,
  "status": "ACTIVE",
  "assignedAt": "2026-10-23T08:00:00+00:00",
  "approvedAt": "2026-10-23T08:00:00+00:00",
  "changeReason": "Q4 split",
  "createdAt": "2026-10-23T08:00:00+00:00",
  "updatedAt": "2026-10-23T08:00:00+00:00"
}
```

- `ownerType`: `DEPARTMENT` or `EMPLOYEE`; the fields of the other owner type are `null`.
- `splitDimension`: `ORG` for a child, `null` for a root. `status`: `DRAFT | ACTIVE | SUPERSEDED | CLOSED`.
- `allocatedValue` / `remainingValue`: sum of the `ACTIVE` children and `targetValue - allocatedValue`. Both are
  `null` when the owner is an employee or the KPI is not `SUM`.
- Decimals are numbers with up to 4 decimal places. IDs are GUID strings; timestamps are ISO 8601 with offset.

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | malformed body; missing/negative/over-precise value; KPI not `ACTIVE`; owner not `ACTIVE`; wrong level or skipped level; root not a branch; no/both owner on a row; duplicate or more than 200 rows; splitting an employee-owned target |
| 401 | `UNAUTHORIZED` | missing or invalid token |
| 403 | `FORBIDDEN` | missing `target:manage`, locked/inactive account, root target without scope `All`, target or parent outside the visible departments |
| 404 | `NOT_FOUND` | unknown KPI, period, department, employee or assignment id |
| 409 | `CONFLICT` | period not `OPEN`; parent not `ACTIVE`; parent already split; an assignment already exists for the KPI/period/owner |
| 422 | `TARGET_ALLOCATION_SUM_MISMATCH` | `SUM` KPI: the children do not total the parent target |
