# Department view

Status: backend implemented (view, tree, create/update/status/soft delete; SCR-PRF-02/03 fields, `compare`, `sort` and
the Excel export added 2026-11). Frontend typed client implemented in
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

Serves SCR-PRF-02 (department performance, department manager) and, called with a `BRANCH` node id, SCR-PRF-03
(branch performance, branch manager; see `BRANCH_DASHBOARD_CONTRACT.md`).

Query:

| Param | Type | Notes |
| --- | --- | --- |
| `periodId` | GUID, optional | Without it the default period is the narrowest non-`DAY` period containing today (`MONTH` before `QUARTER`, `HALF_YEAR`, `YEAR`); if none contains today, the most recently ended non-`DAY` period; if there is none, `period` is `null`. Unknown → 404; a `DAY` period → 400 `VALIDATION_ERROR`. |
| `compare` | `PREVIOUS` \| `SAME_LAST_YEAR`, optional | Default `PREVIOUS`. Case-insensitive; any other value → 400 `VALIDATION_ERROR`. Picks `comparePeriod` (rules below). |

**Compare period** (shared by overview, members and export):

- `PREVIOUS` = the latest period of the same `periodType` whose `endDate` is before the selected period's `startDate`
  (month → previous month, quarter → previous quarter, year → previous year).
- `SAME_LAST_YEAR` = the period of the same `periodType` whose `startDate` is the selected `startDate` minus one year.
- None found (or `period` is `null`) → `comparePeriod` is `null` and every `previous*` / `change*` / `declined*` /
  `compared*` field is `null`. The client then hides comparison columns and bars; it never invents a label such as "T9":
  the label is `comparePeriod.name`.

**Thresholds** are system parameters, never hard-coded (section `SystemParameters` of `appsettings.json` / env
`SystemParameters__<Key>` until GB-878 moves them into `SystemParameterCatalog`). Proposed values pending team sign-off:

| Key | Meaning | Default |
| --- | --- | --- |
| `PerfAttentionDropPoints` (`PERF_ATTENTION_DROP_POINTS`) | a drop of at least this many points → `SCORE_DROP` | 1.5 |
| `PerfAttentionLowScore` (`PERF_ATTENTION_LOW_SCORE`) | a total below this → `LOW_SCORE` | 80 |
| `PerfAttentionTopN` (`PERF_ATTENTION_TOP_N`) | max rows of `attentionMembers` | 5 |
| `PerfWeakGroupScore` (`PERF_WEAK_GROUP_SCORE`) | a group score below this is weak (`isWeak`, `weakestProfileItemId`) | 85 |
| `PerfTrendPeriodCount` (`PERF_TREND_PERIOD_COUNT`) | number of periods in `performance.trend` | 6 |

`data`: `DepartmentOverview`.

```json
{
  "department": { "...": "DepartmentDetail, see above" },
  "period": {
    "id": "5eed0000-0000-0000-000f-000000000004",
    "code": "2026-10", "name": "Tháng 10/2026", "periodType": "MONTH",
    "startDate": "2026-10-01", "endDate": "2026-10-31"
  },
  "comparePeriod": {
    "id": "5eed0000-0000-0000-000f-000000000005",
    "code": "2026-09", "name": "Tháng 9/2026", "periodType": "MONTH",
    "startDate": "2026-09-01", "endDate": "2026-09-30"
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
    "pendingReviewCount": 0,
    "declinedEmployeeCount": 1,
    "comparedEmployeeCount": 3,
    "declinedChildUnitCount": null
  },
  "performance": {
    "average": 80.5,
    "previousAverage": 80.0,
    "target": null,
    "changeFromPrevious": 0.5,
    "evaluatedEmployeeCount": 4,
    "kpis": [
      {
        "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
        "code": "HDV", "name": "Huy động vốn", "unit": "tỷ VND", "direction": "HIGHER_BETTER",
        "targetValue": 900, "actualValue": 450, "achievementPercent": 50
      }
    ],
    "trend": [
      { "periodId": "5eed0000-0000-0000-000f-000000000005", "periodName": "Tháng 9/2026", "average": 80.0 },
      { "periodId": "5eed0000-0000-0000-000f-000000000004", "periodName": "Tháng 10/2026", "average": 80.5 }
    ],
    "kpiGroups": [
      {
        "name": "Kinh doanh",
        "displayOrder": 1,
        "weights": [
          { "jobPositionId": "5eed0000-0000-0000-0006-000000000001", "jobPositionTitle": "Sales Officer", "weightPercent": 70 }
        ],
        "kpis": [
          {
            "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
            "code": "HDV", "name": "Huy động vốn", "unit": "tỷ VND", "direction": "HIGHER_BETTER",
            "targetValue": 900, "actualValue": 450, "achievementPercent": 50,
            "previousAchievementPercent": 55, "achievementChange": -5, "campaignActualValue": 120
          }
        ]
      }
    ],
    "positions": [
      {
        "jobPositionId": "5eed0000-0000-0000-0006-000000000001",
        "jobPositionTitle": "Sales Officer",
        "employeeCount": 3,
        "evaluatedEmployeeCount": 3,
        "average": 81.1, "previousAverage": 84.1, "changeFromPrevious": -3.0,
        "groups": [
          { "profileItemId": "5eed0000-0000-0000-0011-000000000001", "name": "Kinh doanh", "displayOrder": 1,
            "weightPercent": 70, "average": 77.0, "isWeak": true }
        ]
      }
    ]
  },
  "workMix": [
    { "workTypeId": "5eed0000-0000-0000-000c-000000000002", "code": "HUY_DONG", "name": "Huy động tiền gửi", "count": 2 }
  ],
  "operational": {
    "waiting": 1, "inProgress": 2, "overdue": 0, "escalated": null, "completedInPeriod": 0, "pendingReview": 0
  },
  "attentionMembers": [
    {
      "employeeId": "5eed0000-0000-0000-0007-000000000004",
      "employeeCode": "NV004", "fullName": "Lê Minh Châu", "jobPositionTitle": "Sales Officer",
      "totalScore": 72.0, "previousTotalScore": 78.5, "changeFromPrevious": -6.5,
      "weakestGroup": { "name": "Kinh doanh", "score": 65.0 },
      "overdueWorkCount": 1,
      "reasons": ["SCORE_DROP", "LOW_SCORE", "OVERDUE_WORK"]
    }
  ],
  "childUnits": [],
  "generatedAt": "2026-10-18T08:30:00+00:00"
}
```

Field definitions:

- `period` nullable; `availablePeriods` lists all non-`DAY` periods, newest `startDate` first. `comparePeriod` see above.
- `metrics.employeeCount` excludes `TERMINATED`; `activeEmployeeCount` = `ACTIVE`; `onLeaveEmployeeCount` = `ON_LEAVE`.
- **Open work** = work whose assignee belongs to the subtree and whose status is `NEW`, `IN_PROGRESS`,
  `HANDED_OVER`, `SUBMITTED`, `REJECTED` or `OVERDUE`. `attentionWorkCount` = open works that are overdue
  (status `OVERDUE`, or `plannedEndDate` before today) or `REJECTED`.
- `metrics.pendingReviewCount` = result submissions in `SUBMITTED` or `UNDER_REVIEW` whose work is assigned in the subtree.
- **Scored evaluation** = an evaluation of a non-`TERMINATED` subtree employee with a non-null `total_score` and status
  other than `DRAFT` and `DISPUTED` (BR-31: a disputed score is left out of every average, count and attention list).
- `metrics.comparedEmployeeCount` = employees with a scored evaluation in both `period` and `comparePeriod`;
  `metrics.declinedEmployeeCount` = those of them whose score in `period` is lower. Both `null` without `comparePeriod`.
  The client shows them as `declined/compared` ("Cán bộ giảm điểm").
- `metrics.declinedChildUnitCount` = `childUnits` whose `changeFromPrevious` is below 0; `null` when there are no
  child units or no `comparePeriod`.
- `performance.average` = mean of the scored evaluations of the period; `null` when none. `evaluatedEmployeeCount` is
  the number averaged. `previousAverage` = the same over `comparePeriod`; `changeFromPrevious` = `average - previousAverage`
  (`null` if either side is `null`).
- `performance.target` has **no data source yet** and is always `null`.
- `performance.kpis` (kept for older clients): one row per KPI having an `ACTIVE` target assignment on this exact node
  and period (highest `version` wins). `targetValue` is the stored target. `actualValue` aggregates valid
  `actual_progress` rows of subtree employees with `progressDate` inside the period:
  `SUM` -> sum; `AVG` -> mean; `RATIO` -> sum(value)/sum(denominator), multiplied by 100 when the unit is `%`;
  `NO_AGGREGATION` -> `null`. `null` when there are no rows (or a zero ratio denominator).
  `achievementPercent` (nullable, never negative): `HIGHER_BETTER` -> actual/target*100;
  `LOWER_BETTER` -> target/actual*100 (an actual of 0 is the best outcome and yields the cap below);
  `ON_TARGET` -> 100 - |actual-target|/target*100. Never below 0. Capped by the KPI's
  `maxAchievementPercent` when set (for `LOWER_BETTER` with actual 0 and no maximum the value is 100).
  `null` if `targetValue` is 0 or `actualValue` is `null`.
- `performance.kpiGroups`: the same KPI rows, grouped by the **root node of the evaluation profile** (`evaluation_profile_item`
  without parent). Profiles considered: for each job position held by a non-`TERMINATED` subtree employee, its `ACTIVE`
  profile effective in the period (`effectiveFrom <= period.endDate` and `effectiveTo` null or `>= period.startDate`;
  highest `version` wins). Roots of different profiles are the same group when their `name` is equal (the seeded profiles
  share root names and differ only in weights). A KPI joins the group whose root holds it as a leaf (first match by job
  position title); a KPI under no root goes to a last group with `name: null` (client label "Khác" / "Other").
  Groups are ordered by `displayOrder` (the lowest of the merged roots), then name; KPIs inside by code.
  `weights[]` = one entry per profile owning that root: `jobPositionId`, `jobPositionTitle`, the root's `weightPercent`,
  ordered by title; empty for the `null` group.
  Each KPI adds `previousAchievementPercent` (same formula on `comparePeriod`, target assignment of that period; `null`
  without `comparePeriod` or data), `achievementChange` (= `achievementPercent - previousAchievementPercent`, `null` if
  either is `null`) and `campaignActualValue` (BR-26): the part of `actualValue` that came from campaign
  work, i.e. the sum of `work_result_item.raw_value` of `VALID` verified results whose work has a `business_campaign_id`,
  linked to the same progress rows. Only defined for `SUM` KPIs (`null` for `AVG`/`RATIO`/`NO_AGGREGATION` and when
  `actualValue` is `null`). BAU = `actualValue - campaignActualValue`.
- `performance.trend`: up to `PerfTrendPeriodCount` periods of the selected `periodType` ending with the selected one
  (`startDate` not after it), oldest first; `average` as above per period, `null` when nobody is scored. Empty without `period`.
- `performance.positions`: one entry per job position held by a non-`TERMINATED` subtree employee, ordered by title.
  `employeeCount`, `evaluatedEmployeeCount` (scored in `period`), `average`, `previousAverage`, `changeFromPrevious`
  as above restricted to the position. `groups[]` = the root nodes of the position's effective profile (rule above) in
  `displayOrder`: `profileItemId`, `name`, `displayOrder`, `weightPercent` (from the profile, so editing the profile changes
  the label), `average` = mean over the position's scored employees of their **group score** (`null` when none) and
  `isWeak` = `average < PerfWeakGroupScore`. A group score is computed exactly like `groups[].score` of
  `GET /performance-dashboard/employees/{id}` (`EvaluationProfileTree.ScoreNode` over the stored KPI contributions of the
  employee's evaluation); an employee's group is matched to the position's group by root name. Positions without an
  effective profile have `groups: []`.
- `workMix`: open works grouped by work type, count descending then name. Works without a work type are one
  entry with `workTypeId`, `code` and `name` all `null`.
- `operational.waiting` = open works in `NEW`; **`inProgress` = `IN_PROGRESS` + `HANDED_OVER` that are not overdue**
  (changed 2026-11: it no longer includes overdue work, so `completedInPeriod`, `inProgress`, `waiting` and `overdue` are
  disjoint parts of the progress bar); `overdue` = open works in status `OVERDUE` or past `plannedEndDate`;
  `completedInPeriod` = `DONE`/`VERIFIED` works whose `updatedAt` falls inside the period (`null` when `period` is `null`);
  `pendingReview` = `metrics.pendingReviewCount` (repeated for the progress card).
- `operational.escalated` has **no data source yet** and is always `null`; the client **hides** the "Leo thang" tile and
  leaves it out of "Việc cần tôi xử lý" while it is `null` (decision for open question 3 of the plan).
- `attentionMembers`: subtree employees with a scored evaluation in `period` meeting at least one rule:
  `changeFromPrevious <= -PerfAttentionDropPoints` → `SCORE_DROP` (needs a scored evaluation in `comparePeriod`);
  `totalScore < PerfAttentionLowScore` → `LOW_SCORE`; `overdueWorkCount > 0` → `OVERDUE_WORK` (overdue open works
  assigned to the employee, same rule as `operational.overdue`). Ordered by the number of reasons descending, then
  `totalScore` ascending, then employee code; at most `PerfAttentionTopN` rows. `weakestGroup` = the employee's lowest
  group score (`null` when no group has one). `reasons` keep the order `SCORE_DROP`, `LOW_SCORE`, `OVERDUE_WORK`.
- `childUnits`: direct non-`ARCHIVED` children of the node, ordered by code; empty when it has none. See
  `BRANCH_DASHBOARD_CONTRACT.md` for the fields.
- `generatedAt` is the server time of the response.

### `GET /departments/{id}/members`

Employees of the subtree. Query:

| Param | Notes |
| --- | --- |
| `keyword` | employee code/name/email, case-insensitive contains |
| `jobPositionId` | GUID |
| `status` | `ACTIVE` \| `INACTIVE` \| `ON_LEAVE` \| `TERMINATED` (default: everything but `TERMINATED`) |
| `periodId`, `compare` | same defaults and validation as the overview |
| `sort` | `CHANGE_DESC` \| `TOTAL_ASC` \| `CODE`, optional; any other value → 400 `VALIDATION_ERROR`. SCR-PRF-02 sends `CHANGE_DESC` by default |
| `page`, `pageSize` | default 1 / 20, `pageSize` clamped 1..100 |

Without `sort` the rows keep the original order (employee code), which the other screens using this endpoint rely on.
With `sort`, rows are **grouped by job position** (title ascending, so the client can draw one header row per position
from `overview.performance.positions`) and `sort` applies inside each position:
`CHANGE_DESC` = `changeFromPrevious` descending, rows without one (not scored, disputed, no compare period) last;
`TOTAL_ASC` = `performanceScore` ascending, rows without one last; `CODE` = employee code. Ties break by employee code.

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
      "jobPositionTitle": "Sales Officer",
      "status": "ACTIVE",
      "openWorkCount": 2,
      "performanceScore": 72.0,
      "evaluationStatus": "APPROVED",
      "previousPerformanceScore": 78.5,
      "changeFromPrevious": -6.5,
      "groups": [
        { "profileItemId": "5eed0000-0000-0000-0011-000000000001", "name": "Kinh doanh", "displayOrder": 1,
          "weightPercent": 70, "score": 65.0 }
      ],
      "weakestProfileItemId": "5eed0000-0000-0000-0011-000000000001",
      "overdueWorkCount": 1,
      "pendingReviewCount": 1
    }
  ],
  "totalCount": 1, "page": 1, "pageSize": 20
}
```

- `openWorkCount` uses the open-work definition above; `overdueWorkCount` and `pendingReviewCount` use the definitions of
  `operational.overdue` and `metrics.pendingReviewCount`, restricted to works assigned to the member.
- `performanceScore` = the member's scored evaluation for the period (`null` when absent, draft or disputed);
  `previousPerformanceScore` the same for `comparePeriod`; `changeFromPrevious` their difference (`null` if either is `null`).
- `evaluationStatus` = status of the member's evaluation for the period whatever it is (`DRAFT`, `CALCULATED`,
  `IN_REVIEW`, `APPROVED`, `FINAL`, `DISPUTED`, ...); `null` when there is none. The client shows `DISPUTED` as
  "Đang khiếu nại" and every score as `—`.
- `groups` = the root groups of the member's position profile (same list and order as
  `performance.positions[].groups`), `score` = the member's group score (`null` unless the member is scored).
- `weakestProfileItemId` = the group with the lowest score when that score is below `PerfWeakGroupScore`; `null` otherwise.

### `GET /departments/{id}/overview/export`

Excel export of SCR-PRF-02 / SCR-PRF-03 for the same node, `periodId` and `compare` (same validation and authorization as
the overview). Returns `200` with
`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` and
`Content-Disposition: attachment; filename="hieu-suat-<code>-<periodCode>.xlsx"` (no `ApiResponse` envelope; errors still
use the JSON envelope). The file is built on the server from the overview and the full member list; numbers are the same
values the API returns (`null` cells stay empty). Layout (decision for open question 5 of the plan, to be confirmed by
the team):

| Sheet | Rows |
| --- | --- |
| `Tong quan` | node code/name, period, compare period, average, previous average, change, evaluated count, declined/compared employees, pending review, overdue, generated at |
| `KPI` | group, KPI code, name, unit, target, actual, campaign part, achievement %, previous achievement % |
| `Can bo` | code, name, position, status, evaluation status, one column per root group, total, previous total, change, open works, overdue, pending review (every member, `sort=CHANGE_DESC`) |
| `Don vi` | only when the node has child units: code, name, employees, evaluated, average, previous average, change |

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
| 400 | `VALIDATION_ERROR` | invalid enum filter (`status`, `levelType`, `compare`, `sort`), `DAY` period, malformed GUID/number |
| 401 | none (framework) | missing/invalid token; may have no JSON body |
| 403 | none (framework) or `FORBIDDEN` | lacks `organization:read` (framework), or department outside granted scope (`FORBIDDEN`) |
| 404 | `NOT_FOUND` | unknown department, parent or period |
| 409 | `CONFLICT` | duplicate code; stopping/deleting a department that still has live children or employees |
| 500 | `INTERNAL_ERROR` | unexpected |

The client shows real errors and empty states; it never substitutes sample data.
