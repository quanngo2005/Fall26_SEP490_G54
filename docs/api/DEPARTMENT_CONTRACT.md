# Department view

Status: backend implemented (read-only). Frontend typed client implemented in
`frontend/src/app/core/department.service.ts`; the department page uses this real client.
Routes use the backend origin, no API prefix. All responses use `ApiResponse<T>`.
Write operations (create/update/status/delete a department) are **planned, not implemented**.

## Authorization

Every endpoint requires permission `evaluation:read` with scope `Dept` or higher
(`[HasPermission("evaluation", "read", "Dept")]`). Seeded roles: `ADMIN` (All) and `MANAGER`
(Department) pass; `EMPLOYEE` (Own) and `VERIFIER` (no grant) receive 403.

The BLL validates an active, unlocked database account and additionally restricts data to the caller's granted scope:

| Granted scope | Visible departments |
| --- | --- |
| `All` / `System` / full access | every department |
| `Branch` | subtree of the nearest `BRANCH` ancestor-or-self of the caller's department (root-most ancestor when none) |
| `Department` / `Dept` | subtree of the caller's own department |

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
  Query filters accept the same wire values, case-insensitively; an unknown value is 400 `VALIDATION_ERROR`.
- IDs are GUID strings. Timestamps are ISO 8601 with offset. Date-only values are `YYYY-MM-DD`.
- Decimals are rounded to 2 places. "Today" and period boundaries use UTC.

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
        "childDepartmentCount": 2
      }
    ],
    "totalCount": 1, "page": 1, "pageSize": 20
  }
}
```

`employeeCount` is the subtree count. `childDepartmentCount` counts direct children.
`parentDepartmentId` / `parentDepartmentName` are `null` for roots.

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
  `HANDED_OVER`, `SUBMITTED` or `REJECTED`. `attentionWorkCount` = open works that are overdue
  (`plannedEndDate` before today) or `REJECTED`.
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
- `operational.waiting` = open works in `NEW`; `inProgress` = `IN_PROGRESS` + `HANDED_OVER`;
  `overdue` = open works past `plannedEndDate`; `completedInPeriod` = `DONE`/`VERIFIED` works whose
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

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | invalid enum filter, `DAY` period, malformed GUID/number |
| 401 | none (framework) | missing/invalid token; may have no JSON body |
| 403 | none (framework) or `FORBIDDEN` | lacks `evaluation:read` (framework), or department outside granted scope (`FORBIDDEN`) |
| 404 | `NOT_FOUND` | unknown department or period |
| 500 | `INTERNAL_ERROR` | unexpected |

The client shows real errors and empty states; it never substitutes sample data.
