# Branch performance dashboard (SCR-PRF-03) and department performance (SCR-PRF-02)

Status: backend and frontend implemented 2026-11 (`DepartmentViewComponent` reworked for SCR-PRF-02,
`BranchPerformanceComponent` new for SCR-PRF-03). Routes use the backend origin, no API prefix; JSON responses use
`ApiResponse<T>`. Mockups: `SCR-PRF-02_v2_DM.svg`, `SCR-PRF-03_v2_BM.svg` (their numbers are illustrative only).

No new data endpoint is introduced for the branch screen. It is assembled from:

| Card | Endpoint | Contract |
| --- | --- | --- |
| Header, metric cards, KPI cards and tables, trend, child units | `GET /departments/{branchId}/overview?periodId=&compare=` | `DEPARTMENT_CONTRACT.md` + section 1 |
| Evaluation period card | `GET /evaluations/branch-periods?year=` | `BRANCH_EVALUATION_PERIODS_CONTRACT.md` + section 2 |
| Running campaigns, "Campaign chậm tiến độ" | `GET /campaigns?status=RUNNING&page=1&pageSize=` | `CAMPAIGN_CONTRACT.md` + section 3 |
| Excel | `GET /departments/{branchId}/overview/export?periodId=&compare=` | `DEPARTMENT_CONTRACT.md` |

The branch is the caller's `BRANCH` node: the client takes the first `BRANCH` entry of `GET /departments?levelType=BRANCH`
(the list is already limited to the caller's scope). A branch manager (`organization:read` scope `Branch`) sees the
branch and every department below it; anything else returns 403 `FORBIDDEN`, an unknown id 404.

## 1. Overview fields used by SCR-PRF-03

Everything is defined in `DEPARTMENT_CONTRACT.md`; the branch-specific parts are:

```json
{
  "childUnits": [
    {
      "id": "5eed0000-0000-0000-0001-000000000004",
      "code": "KHCN", "name": "Phòng Khách hàng cá nhân", "levelType": "DEPARTMENT",
      "employeeCount": 14, "evaluatedEmployeeCount": 13,
      "average": 84.9, "previousAverage": 84.8, "changeFromPrevious": 0.1
    }
  ],
  "metrics": { "declinedChildUnitCount": 1 },
  "performance": {
    "kpiGroups": [ { "kpis": [ { "campaignActualValue": 120.0 } ] } ]
  }
}
```

- `childUnits`: direct non-`ARCHIVED` children ordered by code. `employeeCount` = non-`TERMINATED` employees of the
  child's subtree; `evaluatedEmployeeCount` = those scored in `period` (scored = not `DRAFT`, not `DISPUTED`, score not
  null); `average` / `previousAverage` = mean scored total in `period` / `comparePeriod`; `changeFromPrevious` = difference.
  Every number is `null` when it cannot be computed. Empty list for a node without children.
- `metrics.declinedChildUnitCount` = child units with `changeFromPrevious < 0` (`null` without child units or
  `comparePeriod`). The client shows `declinedChildUnitCount / childUnits.length`.
- `kpiGroups[].kpis[].campaignActualValue` (BR-26) = the campaign part of `actualValue` (definition in
  `DEPARTMENT_CONTRACT.md`); `null` → `—`.
- `kpiGroups[].weights[]` = union of the effective profiles of the positions present in the branch subtree.
- "KPI đạt kế hoạch" = number of `kpiGroups[].kpis` with `achievementPercent >= 100` over the number of KPIs; the client
  counts the API rows, it does not recompute a percentage.

## 2. Evaluation period card

`GET /evaluations/branch-periods?year=<year of the selected period>` (permission `evaluation:read`, scope `Branch`).
The client shows the newest period (`startDate` descending) whose `displayStatus` is not `LOCKED`; when every period is
locked or the list is empty it shows an empty state. Shown fields: `name`, `displayStatus`,
`submittedDepartmentCount / totalDepartmentCount`, `approvedDepartmentCount`. The lifecycle steps are the `displayStatus`
enum in its documented order (`IN_PROGRESS`, `EVALUATING`, `IN_REVIEW`, `PUBLISHED`, `LOCKED`) with localized labels; the
current one is the period's `displayStatus`. "Mở Kỳ đánh giá" opens `/branch-performance/evaluations`. A 403 on this call
only hides the card content behind an error state; it does not fail the page.

## 3. Campaign list additions (`GET /campaigns`)

Each `campaigns.items[]` row gains:

| Field | Type | Definition |
| --- | --- | --- |
| `progressPercent` | number | Completed works / generated works × 100, rounded to 1 decimal; `0` without works. Same value as `metrics.progressPercent` of `GET /campaigns/{id}`. |
| `timeElapsedPercent` | number | `(today - startDate + 1) / (endDate - startDate + 1) × 100`, clamped 0..100, rounded to 1 decimal. "Today" is the Vietnam calendar day (UTC+7) like every other date-only rule of the API (BR-11). |
| `paceGap` | number | `progressPercent - timeElapsedPercent`, rounded to 1 decimal. Negative = behind schedule. |
| `kpiCode`, `kpiName`, `targetUnit` | string \| null | The KPI the campaign's work types are measured by (`work_type_kpi` effective today, required first, then earliest). `null` unless every work type of the campaign maps to the **same** KPI. |
| `targetValue` | number \| null | Sum of `work.target_value` of the campaign's works; `null` when `kpiName` is `null` or no work has a target. |
| `actualValue` | number \| null | Sum of `work_result_item.raw_value` of `VALID` verified results of the campaign's works for that KPI; `null` when `kpiName` is `null`. |

`metrics` gains `slowCampaignCount`: campaigns matching the request's filters (all pages, not just the current one) whose
`paceGap < 0`. The client shows `slowCampaignCount / campaigns.totalCount` on the "Campaign chậm tiến độ" card, after
requesting `status=RUNNING`.

## 4. Navigation

- New frontend route `/branch-performance` → `BranchPerformanceComponent` (guard: `organizationGuard`, like
  `/department-performance`). The sidebar item `branchPerformance` now opens it; the evaluation period list stays at
  `/branch-performance/evaluations` and is opened from the card.
- Department managers keep `teamPerformance` → `/evaluations` for scoring and get the item `departmentPerformance`
  (`/department-performance`, SCR-PRF-02) in their default menu (decision for open question 1 of the plan).
- Department links from SCR-PRF-03 open `/department-performance?department=<code>&periodId=<id>&compare=<mode>`; the
  page reads `department` (code) or the legacy `departmentId`, plus `periodId` and `compare`, and writes the current
  selection back to the query string.
- `SidebarCatalog.Routes` (backend) and `NavigationService.routeOptions` (frontend) list `/branch-performance`.

## 5. Decisions on the plan's open questions

| # | Question | Choice in this release |
| --- | --- | --- |
| 1 | Department manager menu | Keep `/evaluations` for scoring; add `departmentPerformance` → SCR-PRF-02 |
| 2 | Threshold values | System parameters with the proposed defaults (1.5 / 80 / 5 / 85 / 6), in `appsettings.json` `SystemParameters` until GB-878 lands |
| 3 | Source of `escalated` | None yet: always `null`; the client hides the "Leo thang" tile and leaves it out of the total |
| 4 | `SAME_LAST_YEAR` | Implemented with `PREVIOUS`; returns `comparePeriod: null` when the period does not exist |
| 5 | Excel layout | Sheets `Tong quan`, `KPI`, `Can bo`, `Don vi` (see `DEPARTMENT_CONTRACT.md`), pending team confirmation |

## Errors

Same as `DEPARTMENT_CONTRACT.md`, `CAMPAIGN_CONTRACT.md` and `BRANCH_EVALUATION_PERIODS_CONTRACT.md`. The client shows a
loading state, an error state with "Thử lại" and empty states; it never substitutes sample data when a call fails.
