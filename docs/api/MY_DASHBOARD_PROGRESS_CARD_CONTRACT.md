# My Dashboard Progress Card Contract (SCR-HOME-01)

Status: **IMPLEMENTED** (2026-11-10) — supports `SCR-HOME-01 · My Dashboard` (Teller & Sales Officer).

Provides the operational monthly and quarterly performance progress card widget for employees on the Home Dashboard (`SCR-HOME-01`), replacing the legacy evaluation-dependent score read from GB-348.

## Overview & Role Differences

The progress card visualizes current achievement against active target assignments (`TargetAssignment`) and verified actual progress (`ActualProgress`), enriched with pacing expectations based on working days (`IWorkingDayCalendar`).

| Role | Metric Focus | Cap Rule | Pacing Subtext | Comparison |
| :--- | :--- | :--- | :--- | :--- |
| **TELLER** | Overall weighted KPI score | Capped (`MaxAchievementPercent`) | `Kỳ vọng hôm nay {expectedRate}%` | MoM (`▲ 6% so với T10`) / QoQ (`▲ 5% so với Q3`) |
| **SALES** | Total sales volume / achievement | Uncapped (`KPI Sales không trần`) | `KPI Sales không trần` (or expected pacing if &lt; 100%) | MoM (`▲ 12% MoM`) / QoQ (`▲ 15% QoQ`) |
| **OTHER** | Standard KPI achievement | Standard cap | `Kỳ vọng hôm nay {expectedRate}%` | MoM / QoQ |

## Authorization & Permissions

- `[Authorize]`: Any authenticated employee account.
- The caller's `EmployeeId` and `JobPosition` are derived server-side from JWT claim `sub` (never trusted from request).
- An account not linked to an active employee returns `403 FORBIDDEN`.

## Endpoints

| Method | Route | Description |
| :--- | :--- | :--- |
| `GET` | `/my-dashboard/progress-card` | Get progress card for current employee (month or quarter) |

### GET `/my-dashboard/progress-card`

#### Query Parameters

| Parameter | Type | Required | Default | Description |
| :--- | :---: | :---: | :---: | :--- |
| `periodType` | `string` | No | `MONTH` | `MONTH` or `QUARTER`. Ignored if `periodId` is specified. |
| `periodId` | `uuid` | No | `null` | Explicit evaluation period ID. Must be `MONTH` or `QUARTER`. `DAY` returns `400 VALIDATION_ERROR`. |

#### Response `200 OK`

```json
{
  "success": true,
  "status": 200,
  "data": {
    "role": "TELLER",
    "cardTitle": "Hiệu suất tháng 11",
    "period": {
      "id": "5eed0000-0000-0000-000f-000000000005",
      "code": "2026-11",
      "name": "Tháng 11/2026",
      "periodType": "MONTH",
      "startDate": "2026-11-01",
      "endDate": "2026-11-30"
    },
    "currentRate": 82.0,
    "pacingStatus": "ON_PACE",
    "pacingStatusLabel": "Đạt nhịp",
    "expectedRateToday": 30.0,
    "expectedRateTodayText": "Kỳ vọng hôm nay 30%",
    "policyNote": null,
    "previousPeriodComparison": {
      "comparisonType": "MoM",
      "direction": "UP",
      "delta": 6.0,
      "comparedPeriodName": "T10",
      "text": "▲ 6% so với T10"
    },
    "workingDays": {
      "totalWorkingDays": 21,
      "elapsedWorkingDays": 6
    },
    "actionUrl": "/my-targets"
  },
  "traceId": "request-trace-id"
}
```

#### Sales Officer Response Example (Quarterly):

```json
{
  "success": true,
  "status": 200,
  "data": {
    "role": "SALES",
    "cardTitle": "Doanh số Quý 4/2026",
    "period": {
      "id": "5eed0000-0000-0000-000f-000000000003",
      "code": "2026-Q4",
      "name": "Quý 4/2026",
      "periodType": "QUARTER",
      "startDate": "2026-10-01",
      "endDate": "2026-12-31"
    },
    "currentRate": 112.0,
    "pacingStatus": "AHEAD",
    "pacingStatusLabel": "Vượt",
    "expectedRateToday": 42.0,
    "expectedRateTodayText": null,
    "policyNote": "KPI Sales không trần",
    "previousPeriodComparison": {
      "comparisonType": "QoQ",
      "direction": "UP",
      "delta": 15.0,
      "comparedPeriodName": "Q3",
      "text": "▲ 15% QoQ"
    },
    "workingDays": {
      "totalWorkingDays": 66,
      "elapsedWorkingDays": 28
    },
    "actionUrl": "/my-targets"
  },
  "traceId": "request-trace-id"
}
```

## Calculation Rules

1. **Target Assignment**:
   - Matches active targets for the employee in the resolved period (`period.Id`).
   - If a `MONTH` period is requested and no direct monthly target is found, active targets for the parent `QUARTER` period are prorated by working day ratio using `IWorkingDayCalendar`.
2. **Actual Progress**:
   - Aggregates `ActualProgress` records where `Status == Valid`, linked to the employee's target assignment and with `ProgressDate >= period.StartDate && ProgressDate <= period.EndDate`.
3. **Current Rate**:
   - For `TELLER`: Calculates weighted score across KPIs using `EvaluationProfileItem` weights and capped by `MaxAchievementPercent`.
   - For `SALES`: Calculates total sales volume achievement without capping (`KPI Sales không trần`), allowing values &gt; 100%.
4. **Pacing & Working Days**:
   - `expectedRateToday = Math.Round((decimal)elapsedWorkingDays / totalWorkingDays * 100m, 0)`.
   - Status:
     - `currentRate >= 100m` -> `AHEAD` (`Vượt`).
     - `currentRate >= expectedRateToday` -> `ON_PACE` (`Đạt nhịp`).
     - `currentRate < expectedRateToday` -> `BEHIND` (`Chậm nhịp`).
5. **Comparison**:
   - Compares with the immediate previous period of the same type (`MONTH` -> MoM, `QUARTER` -> QoQ).
   - Formatted with `▲` (increase), `▼` (decrease), or `-` (flat).

