# Department view

Status: backend implemented (view, tree, create/update/status/soft delete). Frontend typed client implemented in
`frontend/src/app/core/department.service.ts` (read endpoints only; the management endpoints have no UI yet).
Routes use the backend origin, no API prefix. All responses use `ApiResponse<T>`.

## Authorization

Every endpoint requires the permission `organization:read` with scope `Dept` or higher
(`[HasPermission("organization", "read", "Dept")]`; code `ORGANIZATION_READ`). The migration `SeedOrganizationReadAccess`
grants it to `SYSTEM_ADMIN` and `ADMIN` (`All`), `BRANCH_MANAGER` (`Branch`) and `DEPT_MANAGER` / `MANAGER` (`Dept`) only.
Every other role - employee, verifier, HR, QA - has no grant and receives 403 `FORBIDDEN`; holding `evaluation:read` (which
this screen used to borrow) no longer opens it. A missing or invalid token receives 401. The same permission guards
`/job-positions` (`JOB_POSITION_CONTRACT.md`).
The BLL validates an active, unlocked database account and additionally restricts data to the scope granted for `organization:read`:

| Granted scope | Visible departments |
| --- | --- |
| `All` / `System` / full access | every department |
| `Branch` | subtree of the nearest `BRANCH` ancestor-or-self of the caller's department (root-most ancestor when none) |
| `Department` / `Dept` | subtree of the caller's own department |

Write endpoints (`POST`, `PUT`, `PATCH .../status`, `DELETE`) additionally require `department:manage` with scope `All`
(`ADMIN` via full access) and are not limited to the caller's subtree.

A department outside that set returns 403 `FORBIDDEN`; an unknown id returns 404 `NOT_FOUND`.
List results contain only visible departments.

## Conventions

- "Subtree" = the department plus all descendants through `parentDepartmentId`.
  Employee counts, works, reviews and performance aggregate over the whole subtree.
- Employees with status `TERMINATED` are excluded from counts, members and performance
  unless the members endpoint is explicitly filtered with `status=TERMINATED`.
- **N/A rule:** a value with no data source or no data yet is `null` (never `0`, never a sample
  value). The client renders `null` as N/A. Counts are `0` when there is genuinely nothing to count.
- Enums are UPPER_SNAKE strings matching PostgreSQL names (`DEPARTMENT`, `ON_LEAVE`, `HIGHER_BETTER`).
  Query filters accept the same API values, case-insensitively; an unknown value is 400 `VALIDATION_ERROR`.
- IDs are GUID strings. Timestamps are ISO 8601 with offset. Date-only values are `YYYY-MM-DD`.
- Decimals are rounded to 2 places. "Today" and period boundaries use the Vietnam calendar day (UTC+7).

## Endpoints

### `GET /departments`

Query: `keyword` (code/name, case-insensitive contains), `status` (`ACTIVE|INACTIVE|ARCHIVED`),
`levelType` (`BRANCH|DEPARTMENT|TEAM`), `parentDepartmentId` (GUID, direct children only),
`page` (default 1), `pageSize` (default 20, clamped 1..100). Ordered by `code`.

`data`: `PagedResult<DepartmentSummary>`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "5eed0000-0000-0000-0001-000000000002",
        "code": "CN-HN-KD",
        "name": "Phòng Kinh doanh",
        "levelType": "DEPARTMENT",
        "status": "ACTIVE",
        "parentDepartmentId": "5eed0000-0000-0000-0001-000000000001",
        "parentDepartmentName": "Chi nhánh Hà Nội",
        "employeeCount": 5,
        "childDepartmentCount": 2,
        "managerName": "Trần Thị Bình",
        "tellerCount": 2,
        "salesCount": 3,
        "lastSyncAt": "2026-10-01T00:00:00+00:00"
      }
    ],
    "totalCount": 1, "page": 1, "pageSize": 20
  }
}
```

`employeeCount` is the subtree count. `childDepartmentCount` counts direct children.
`parentDepartmentId` / `parentDepartmentName` are `null` for roots.
`ARCHIVED` (soft-deleted) departments are omitted unless `status=ARCHIVED` is requested.

Per-row summary fields (computed over the department subtree, `ACTIVE` employees only):

- `managerName` - full name of the derived manager, using step 1 of the `DepartmentDetail.manager` rule: the active
  employee whose own manager is outside the subtree and who has the most direct reports inside (tie: lowest
  `employeeCode`). `null` when there is no such employee; the list does not fall back to a manager outside the
  subtree (the detail endpoint does).
- `tellerCount` / `salesCount` - active employees whose `jobPosition.code` is `TELLER` / `SALES`. Matching is by code
  only, never by title text. `0` when nobody matches, never `null`. On-leave and terminated employees are not
  counted, so `tellerCount + salesCount` can be lower than `employeeCount` (which keeps the non-`TERMINATED` rule).
- `lastSyncAt` - ISO 8601 time the department was last synchronised from the source system. Never `null`.

### `GET /departments/tree`

The departments visible to the caller as a forest, children ordered by `code`; `ARCHIVED` ones are omitted.
A node is a root when it has no parent or its parent is outside the caller's scope.

```json
[
  {
    "id": "5eed0000-0000-0000-0001-000000000001", "code": "CN-HN", "name": "Chi nhánh Hà Nội",
    "levelType": "BRANCH", "status": "ACTIVE",
    "directEmployeeCount": 0, "employeeCount": 5,
    "children": [ { "id": "…", "code": "CN-HN-KD", "…": "same shape, recursive", "children": [] } ]
  }
]
```

`directEmployeeCount` counts only the node itself, `employeeCount` the whole subtree (both exclude `TERMINATED`).

### `GET /departments/{id}`

`data`: `DepartmentDetail`. 404 unknown id, 403 outside scope.

```json
{
  "id": "5eed0000-0000-0000-0001-000000000002",
  "code": "CN-HN-KD",
  "name": "Phòng Kinh doanh",
  "description": "Khối bán hàng (Sales)",
  "levelType": "DEPARTMENT",
  "status": "ACTIVE",
  "parentDepartmentId": "5eed0000-0000-0000-0001-000000000001",
  "ancestors": [{ "id": "5eed0000-0000-0000-0001-000000000001", "code": "CN-HN", "name": "Chi nhánh Hà Nội" }],
  "manager": {
    "id": "5eed0000-0000-0000-0007-000000000002",
    "employeeCode": "NV002",
    "fullName": "Trần Thị Bình",
    "jobPositionTitle": "Chuyên viên Kinh doanh"
  },
  "directEmployeeCount": 1,
  "employeeCount": 5,
  "childDepartmentCount": 2,
  "lastSyncAt": "2026-10-01T00:00:00+00:00",
  "updatedAt": "2026-10-01T00:00:00+00:00"
}
```

`ancestors` is ordered root first and excludes the department itself. `description` is nullable.

`manager` is derived (the `department` table has no manager column) and is `null` when it cannot
be determined. "Inside" means among the `ACTIVE` employees of the subtree. Rule:

1. Candidates are those employees whose own manager is not inside (or is absent).
   The candidate with the most direct reports inside (tie: lowest `employeeCode`)
   is the manager when that count is greater than 0.
2. Otherwise the most frequent non-`TERMINATED` manager outside the subtree among those
   employees is used (tie: lowest `employeeCode`).
3. Otherwise `null`.

`ancestors` (names and codes only) are returned even when an ancestor is outside the caller's scope,
so breadcrumbs can render.

### `GET /departments/{id}/overview`

Query: `periodId` (GUID, optional). Without it the default period is the narrowest non-`DAY`
period containing today (`MONTH` before `QUARTER`, `HALF_YEAR`, `YEAR`); if none contains today,
the most recently ended non-`DAY` period; if there is none, `period` is `null`.
Unknown `periodId` is 404; a `DAY` period is 400 `VALIDATION_ERROR`.

`data`: `DepartmentOverview`.

```json
{
  "department": { "...": "DepartmentDetail, see above" },
  "period": {
    "id": "5eed0000-0000-0000-000f-000000000004",
    "code": "2026-10", "name": "Tháng 10/2026", "periodType": "MONTH",
    "startDate": "2026-10-01", "endDate": "2026-10-31"
  },
  "availablePeriods": [
    { "id": "5eed0000-0000-0000-000f-000000000003", "code": "2026-Q4", "name": "Quý 4/2026",
      "periodType": "QUARTER", "startDate": "2026-10-01", "endDate": "2026-12-31" }
  ],
  "metrics": {
    "employeeCount": 5,
    "activeEmployeeCount": 5,
    "onLeaveEmployeeCount": 0,
    "openWorkCount": 3,
    "attentionWorkCount": 1,
    "pendingReviewCount": 0
  },
  "performance": {
    "average": null,
    "target": null,
    "changeFromPrevious": null,
    "evaluatedEmployeeCount": 0,
    "kpis": [
      {
        "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
        "code": "HDV", "name": "Huy động vốn", "unit": "tỷ VND", "direction": "HIGHER_BETTER",
        "targetValue": 900, "actualValue": null, "achievementPercent": null
      }
    ]
  },
  "workMix": [
    { "workTypeId": "5eed0000-0000-0000-000c-000000000002", "code": "HUY_DONG", "name": "Huy động tiền gửi", "count": 2 }
  ],
  "operational": {
    "waiting": 1, "inProgress": 2, "overdue": 0, "escalated": null, "completedInPeriod": 0
  },
  "generatedAt": "2026-10-18T08:30:00+00:00"
}
```

Field definitions:

- `period` nullable; `availablePeriods` lists all non-`DAY` periods, newest `startDate` first.
- `metrics.employeeCount` excludes `TERMINATED`; `activeEmployeeCount` = `ACTIVE`; `onLeaveEmployeeCount` = `ON_LEAVE`.
- **Open work** = work whose assignee belongs to the subtree and whose status is `NEW`, `IN_PROGRESS`,
  `HANDED_OVER`, `SUBMITTED`, `REJECTED` or `OVERDUE`. `attentionWorkCount` = open works that are overdue
  (status `OVERDUE`, or `plannedEndDate` before today) or `REJECTED`.
- `metrics.pendingReviewCount` = result submissions in `SUBMITTED` or `UNDER_REVIEW` whose work is assigned in the subtree.
- `performance.average` = mean of `performance_evaluation.total_score` for the period, over subtree
  employees, evaluations with a non-null score and status other than `DRAFT`. Scale is the stored
  score (assumed 0..100). `null` when no evaluation is scored. `evaluatedEmployeeCount` is the number averaged.
- `performance.changeFromPrevious` = `average` minus the average of the latest earlier period of the same
  `periodType` (`endDate` before this period's `startDate`); `null` if either side is `null`.
- `performance.target` has **no data source yet** and is always `null`.
- `performance.kpis`: one row per KPI having an `ACTIVE` target assignment on this exact department and
  period (highest `version` wins). `targetValue` is the stored target. `actualValue` aggregates valid
  `actual_progress` rows of subtree employees with `progressDate` inside the period:
  `SUM` -> sum; `AVG` -> mean; `RATIO` -> sum(value)/sum(denominator), multiplied by 100 when the unit is `%`;
  `NO_AGGREGATION` -> `null`. `null` when there are no rows (or a zero ratio denominator).
  `achievementPercent` (nullable, never negative): `HIGHER_BETTER` -> actual/target*100;
  `LOWER_BETTER` -> target/actual*100 (an actual of 0 is the best outcome and yields the cap below);
  `ON_TARGET` -> 100 - |actual-target|/target*100. Never below 0. Capped by the KPI's
  `maxAchievementPercent` when set (for `LOWER_BETTER` with actual 0 and no maximum the value is 100).
  `null` if `targetValue` is 0 or `actualValue` is `null`.
- `workMix`: open works grouped by work type, count descending then name. Works without a work type are one
  entry with `workTypeId`, `code` and `name` all `null`.
- `operational.waiting` = open works in `NEW`; `inProgress` = `IN_PROGRESS` + `HANDED_OVER` + `OVERDUE`;
  `overdue` = open works in status `OVERDUE` or past `plannedEndDate`; `completedInPeriod` = `DONE`/`VERIFIED` works whose
  `updatedAt` falls inside the period (`null` when `period` is `null`).
- `operational.escalated` has **no data source yet** and is always `null`.
- `generatedAt` is the server time of the response.

### `GET /departments/{id}/members`

Employees of the subtree. Query: `keyword` (employee code/name/email, case-insensitive contains),
`jobPositionId` (GUID), `status` (`ACTIVE|INACTIVE|ON_LEAVE|TERMINATED`), `periodId` (same default as
overview), `page` (default 1), `pageSize` (default 20, clamped 1..100). Ordered by `employeeCode`.

`data`: `PagedResult<DepartmentMember>`.

```json
{
  "items": [
    {
      "id": "5eed0000-0000-0000-0007-000000000004",
      "employeeCode": "NV004",
      "fullName": "Lê Minh Châu",
      "departmentId": "5eed0000-0000-0000-0001-000000000004",
      "departmentName": "Nhóm Kinh doanh 1",
      "jobPositionId": "5eed0000-0000-0000-0006-000000000001",
      "jobPositionTitle": "Chuyên viên Kinh doanh",
      "status": "ACTIVE",
      "openWorkCount": 2,
      "performanceScore": null
    }
  ],
  "totalCount": 1, "page": 1, "pageSize": 20
}
```

`openWorkCount` uses the open-work definition above. `performanceScore` is the member's scored
evaluation for the period (same rules as `performance.average`), `null` when absent.

## Management

Bodies are camelCase JSON. Enums are the UPPER_SNAKE API values (case-insensitive on input).

### `POST /departments` → 201 `DepartmentDetail`

```json
{ "code": "CN-HN-KT", "name": "Phòng Kế toán", "description": "Tuỳ chọn", "levelType": "DEPARTMENT", "parentDepartmentId": "5eed0000-0000-0000-0001-000000000001" }
```

`PUT /departments/{id}` takes the same body and returns 200 `DepartmentDetail`; the status is not part of it.
A new department is always `ACTIVE`.

Rules (400 `VALIDATION_ERROR` unless stated):

- `code`: required, ≤ 50, letters/digits/`-`/`_` only (URL-safe, it is used as `?department=<code>`); trimmed and stored
  **upper-case**; unique, case-insensitively → 409 `CONFLICT`. An archived department keeps its code reserved.
- `name`: required, ≤ 200. `description`: optional, ≤ 1000; blank becomes `null`. `levelType`: required.
- Hierarchy: `BRANCH` is a root (`parentDepartmentId` must be omitted). `DEPARTMENT` and `TEAM` require a parent of a
  **strictly higher** level (`BRANCH` > `DEPARTMENT` > `TEAM`). Unknown parent → 404.
- On update the parent may not be the department itself or one of its descendants (no cycles; the database also
  enforces this), and the new `levelType` must stay above the levels of its existing children.
- The parent must be `ACTIVE` when a department is created or moved under it (keeping an inactive parent is fine).

### `PATCH /departments/{id}/status` → 200 `DepartmentDetail`

Body `{ "status": "ACTIVE" | "INACTIVE" | "ARCHIVED" }`. Setting the current value is a no-op.

- Leaving `ACTIVE` (stop operating) → 409 while the department has `ACTIVE` children or non-`TERMINATED` employees; the
  message gives the count. Employees cannot be assigned to a non-`ACTIVE` department (employee API rule).
- Returning to `ACTIVE` → 400 when its parent is not `ACTIVE`.

### `DELETE /departments/{id}` → 200 (soft delete)

The row is **kept** and set to `ARCHIVED`, so employees' history, targets and works stay intact; it disappears from
`GET /departments` (unless `status=ARCHIVED`) and from the tree. 409 while it has non-`ARCHIVED` children or
non-`TERMINATED` employees. Deleting an already archived department succeeds without change; unknown id → 404.
There is no hard delete and no restore endpoint; `PATCH .../status` with `ACTIVE`/`INACTIVE` restores one.

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | invalid enum filter, `DAY` period, malformed GUID/number |
| 401 | none (framework) | missing/invalid token; may have no JSON body |
| 403 | none (framework) or `FORBIDDEN` | lacks `organization:read` (framework), or department outside granted scope (`FORBIDDEN`) |
| 404 | `NOT_FOUND` | unknown department, parent or period |
| 409 | `CONFLICT` | duplicate code; stopping/deleting a department that still has live children or employees |
| 500 | `INTERNAL_ERROR` | unexpected |

The client shows real errors and empty states; it never substitutes sample data.
