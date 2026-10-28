# Performance Dashboard (Personal / Department / Branch)

Status: backend implemented (read-only). Frontend client: **planned, not implemented**.
Routes use the backend origin with no API prefix. All responses use `ApiResponse<T>`
(see `API_CONVENTIONS.md`). Enums are UPPER_SNAKE strings matching PostgreSQL names.

The dashboard only **reads** results that were already calculated (`performance_evaluation` and the
`performance_contribution` rows written by `POST /performance-evaluations/{id}/calculate`). It never
recalculates a score.

## Endpoints

| Level | Route | Policy | Extra rule applied by the BLL |
| --- | --- | --- | --- |
| Personal (self) | `GET /performance-dashboard/me` | `evaluation:read`, scope `Own` | the caller's own data |
| Personal (drill-down) | `GET /performance-dashboard/employees/{employeeId}` | `evaluation:read`, scope `Own` | own id always allowed; another employee needs scope `Dept` or higher and the employee's department must be visible to the caller |
| Department | `GET /performance-dashboard/departments/{departmentId}` | `evaluation:read`, scope `Dept` | the department must be visible to the caller |
| Branch | `GET /performance-dashboard/branches/{branchId}` | `evaluation:read`, scope `Dept` | the node must be a `BRANCH` and visible to the caller |

All endpoints take the optional query `periodId` (GUID).

A **branch is a department whose `levelType` is `BRANCH`**; there is no separate branch resource. An id that exists
but is not a `BRANCH` returns 404 on the branch route.

The employee route exists so that a manager can drill from a department to one employee's KPIs. It does not let an
employee see a peer: with scope `Own` any id other than the caller's own returns 403.

## Authorization

Visible departments follow `DEPARTMENT_CONTRACT.md`:

| Granted scope | Visible departments |
| --- | --- |
| `All` / `System` / full access | every department |
| `Branch` | subtree of the nearest `BRANCH` ancestor-or-self of the caller's department (root-most ancestor when none) |
| `Department` / `Dept` | subtree of the caller's own department |
| `Own` | none (personal data only) |

The BLL also requires an active, unlocked account. A department or employee outside the visible set returns
403 `FORBIDDEN`; an unknown id returns 404 `NOT_FOUND`.

## Conventions

- "Subtree" = the department plus all descendants through `parentDepartmentId`. `TERMINATED` employees and
  `ARCHIVED` departments are left out of counts, averages and the child list.
- **Period:** `periodId` must be a non-`DAY` period. Without it the period containing today is used (the narrowest
  type wins), otherwise the latest period that already ended. A `DAY` period is 400 `VALIDATION_ERROR`; an unknown
  period is 404 `NOT_FOUND`. `period` is `null` when no period exists, and every figure is then `null`/empty.
  `availablePeriods` lists all non-`DAY` periods, newest `startDate` first.
- **Previous period:** the latest earlier period of the same `periodType` (`endDate` before this period's `startDate`).
  `changeFromPrevious` = `score` − `previousScore` of the already rounded values; `null` when either side is `null`.
- **Scored evaluation:** an evaluation counts only when its status is not `DRAFT` and its `totalScore` is not `null`.
  A `DRAFT` evaluation is treated as not evaluated: no score, no groups, no KPIs.
- **Disputed (BR-31):** on the department and branch routes an evaluation with status `DISPUTED` (Disputed_Pending, see
  `APPEAL_CONTRACT.md` 2.5) is not scored: it is left out of `evaluatedEmployeeCount`, `averageScore`,
  `previousAverageScore`, `groups`, `kpis` and `childUnits`. In `members` it keeps `evaluationStatus: "DISPUTED"` and
  `totalScore: null`. The personal routes still return the caller's own stored score.
- **N/A rule:** a value with no data is `null` (never `0`). Counts are `0` when there is genuinely nothing to count.
- **No ranking:** the dashboard never returns a rank or a position among peers.
- Decimals are rounded to 2 places (away from zero). IDs are GUID strings. Timestamps are ISO 8601 with offset;
  date-only values are `YYYY-MM-DD`. "Today" and period boundaries use UTC.
- Contributions linked to a risk event are not part of the KPI rows. A KPI row is identified through the progress it
  was calculated from.

## Personal

`GET /performance-dashboard/me` and `GET /performance-dashboard/employees/{employeeId}` return the same shape.

`data`: `PersonalPerformanceDashboard`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "employee": {
      "id": "5eed0000-0000-0000-0007-000000000004",
      "code": "NV004",
      "fullName": "Lê Minh Châu",
      "departmentId": "5eed0000-0000-0000-0001-000000000004",
      "departmentName": "Nhóm Kinh doanh 1",
      "jobPositionTitle": "Chuyên viên Kinh doanh"
    },
    "period": { "id": "5eed0000-0000-0000-000f-000000000003", "code": "2026-Q4", "name": "Quý 4/2026", "periodType": "QUARTER", "startDate": "2026-10-01", "endDate": "2026-12-31" },
    "availablePeriods": [],
    "evaluationStatus": "CALCULATED",
    "totalScore": 98.75,
    "previousTotalScore": 95.5,
    "changeFromPrevious": 3.25,
    "calculatedAt": "2026-10-21T04:10:00+00:00",
    "groups": [
      {
        "kpiGroupId": "5eed0000-0000-0000-0003-000000000001",
        "code": "KD",
        "name": "Kinh doanh",
        "weightPercent": 70.0,
        "score": 102.5,
        "kpis": [
          {
            "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
            "code": "HDV",
            "name": "Huy động vốn",
            "unit": "tỷ VND",
            "direction": "HIGHER_BETTER",
            "targetValue": 225.0,
            "actualValue": 180.0,
            "achievementPercentRaw": 80.0,
            "achievementPercentCapped": 80.0,
            "score": 80.0
          }
        ]
      }
    ],
    "generatedAt": "2026-10-22T07:00:00+00:00"
  },
  "traceId": "request-trace-id"
}
```

Field definitions:

- `evaluationStatus`: the evaluation's status, `null` when the employee has no evaluation for the period.
- `totalScore`: the stored `performance_evaluation.total_score`; `null` when not scored.
- `groups`: one entry per item of the **evaluation's own profile**, ordered by `displayOrder`; `weightPercent` is the
  item's weight. `score` is the mean of the non-null KPI scores of the group, `null` when there is none.
- `kpis`: the group's KPIs that have a calculated contribution, ordered by `code` (ordinal). `targetValue` and
  `actualValue` are the snapshots stored by the calculation. `achievementPercentRaw`, `achievementPercentCapped` and
  `score` are the stored values. A KPI of a group that is not part of the profile is not listed.
- An employee without an evaluation, or with a `DRAFT` one, has `groups: []` and `totalScore: null`.

## Department and Branch

`GET /performance-dashboard/departments/{departmentId}` and `GET /performance-dashboard/branches/{branchId}`
return the same shape for the subtree of the node.

`data`: `UnitPerformanceDashboard`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "orgUnit": { "id": "5eed0000-0000-0000-0001-000000000002", "code": "CN-HN-KD", "name": "Phòng Kinh doanh", "levelType": "DEPARTMENT" },
    "period": { "id": "5eed0000-0000-0000-000f-000000000003", "code": "2026-Q4", "name": "Quý 4/2026", "periodType": "QUARTER", "startDate": "2026-10-01", "endDate": "2026-12-31" },
    "availablePeriods": [],
    "employeeCount": 5,
    "evaluatedEmployeeCount": 4,
    "averageScore": 91.44,
    "previousAverageScore": 88.0,
    "changeFromPrevious": 3.44,
    "groups": [
      { "kpiGroupId": "5eed0000-0000-0000-0003-000000000001", "code": "KD", "name": "Kinh doanh", "averageScore": 96.3, "employeeCount": 4 }
    ],
    "kpis": [
      {
        "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
        "code": "HDV",
        "name": "Huy động vốn",
        "unit": "tỷ VND",
        "direction": "HIGHER_BETTER",
        "employeeCount": 4,
        "averageAchievementPercent": 88.5,
        "averageScore": 88.5
      }
    ],
    "childUnits": [
      { "id": "5eed0000-0000-0000-0001-000000000004", "code": "CN-HN-KD-N1", "name": "Nhóm Kinh doanh 1", "levelType": "TEAM", "employeeCount": 2, "evaluatedEmployeeCount": 2, "averageScore": 94.38 }
    ],
    "members": [
      { "employeeId": "5eed0000-0000-0000-0007-000000000002", "employeeCode": "NV002", "fullName": "Trần Thị Bình", "jobPositionTitle": "Chuyên viên Kinh doanh", "evaluationStatus": "CALCULATED", "totalScore": 92.0 }
    ],
    "generatedAt": "2026-10-22T07:00:00+00:00"
  },
  "traceId": "request-trace-id"
}
```

Field definitions:

- `orgUnit`: the requested node. `levelType` is `BRANCH`, `DEPARTMENT` or `TEAM`.
- `employeeCount`: non-`TERMINATED` employees of the subtree. `evaluatedEmployeeCount`: those with a scored evaluation.
- `averageScore`: mean of the scored evaluations' `totalScore` over the subtree; `null` when none is scored.
  `previousAverageScore` is the same figure for the previous period.
- `groups`: KPI groups found in the scored employees' contributions, ordered by `code`. `averageScore` is the mean, over
  the employees that have one, of each employee's group score (mean of that employee's non-null KPI scores in the
  group); `employeeCount` is the number of employees averaged. Weights are not returned because profiles differ per
  position.
- `kpis`: KPIs found in the scored employees' contributions, ordered by `code`. `employeeCount` is the number of
  distinct employees with the KPI; `averageAchievementPercent` is the mean of `achievementPercentCapped` and
  `averageScore` the mean of the KPI score, each over the rows that have a value.
- `childUnits`: the direct, non-`ARCHIVED` child departments, ordered by `code`; each figure covers that child's
  subtree. These are the next drill-down level: call the department route with the child's `id`.
- `members`: employees attached **directly** to this node (not to a child), ordered by `employeeCode`.
  `evaluationStatus` is `null` without an evaluation and `totalScore` is `null` unless the evaluation is scored. Call
  the employee route with `employeeId` for the KPIs.

## Drill-down path

```
branch  ->  childUnits (departments)  ->  childUnits (teams)  ->  members  ->  employee  ->  groups / kpis
```

The drill-down stops at the KPI. It does not return works, submissions or evidence.

## Errors

Errors use the standard envelope; clients branch on `errorCode`.

| Situation | HTTP | errorCode |
| --- | --- | --- |
| Missing, expired or malformed token | 401 | `UNAUTHORIZED` |
| Missing `evaluation:read` permission, unknown/locked/inactive account, or a department/employee outside the caller's visible set | 403 | `FORBIDDEN` |
| Unknown department, employee or period; or a non-`BRANCH` id on the branch route | 404 | `NOT_FOUND` |
| `periodId` is a `DAY` period, or a malformed GUID | 400 | `VALIDATION_ERROR` |
| Unexpected failure | 500 | `INTERNAL_ERROR` |

## Limits

- Scores come from the stored evaluations; two writers currently exist (the calculate endpoint and the evaluation
  form), so the dashboard shows whichever wrote last.
- KPI and group figures exist only for evaluations that went through the calculate endpoint; an evaluation scored
  only through the evaluation form shows `groups: []` and no KPI rows.
- Risk-event penalties are not shown.
- `members` and `childUnits` are not paged.
