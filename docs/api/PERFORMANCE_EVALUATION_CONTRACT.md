# Performance Evaluation — Calculate KPI Score

Status: backend implemented (`POST /performance-evaluations/{id}/calculate`). Frontend client: **planned, not implemented**.
Routes use the backend origin with no API prefix. Responses use `ApiResponse<T>`
(see `API_CONVENTIONS.md`). Enums are UPPER_SNAKE strings matching PostgreSQL names.

## Authorization

Requires permission `evaluation:calculate` with scope `Dept` or higher
(`[HasPermission("evaluation", "calculate", "Dept")]`). The permission is not part of the seed data:
`ADMIN` passes (full access); any other role passes only once the permission has been granted to it.

The BLL additionally restricts the target evaluation to the caller's granted scope, based on the
department of the evaluated employee:

| Granted scope | Employees the caller may calculate |
| --- | --- |
| `All` / `System` / full access | every employee |
| `Branch` | employees in the subtree of the nearest `BRANCH` ancestor-or-self of the caller's department (root-most ancestor when none) |
| `Department` / `Dept` | employees in the subtree of the caller's own department |

An employee outside that set returns 403 `FORBIDDEN`.

## Endpoint

### `POST /performance-evaluations/{id}/calculate`

Path: `id` — `performance_evaluation.id` (GUID). No request body.

Recalculation is allowed: calling the endpoint again refreshes the same contribution rows
(no duplicates) and never creates a second evaluation.

### Processing

1. Load the evaluation, its period and the employee; check scope and state.
2. Convert every approved, still `VALID` verified work result item of the evaluated employee whose
   verification date falls inside the evaluation period into an `actual_progress` row. The conversion
   is idempotent: a work result item contributes at most once (`source_item_id` is unique). Items that
   cannot be converted are skipped and logged: a `RATIO` KPI without a positive denominator, a negative
   value, or a verification date without a `DAY` evaluation period.
3. Resolve the target of every `ACTIVE` KPI of the profile's groups: an employee-level `ACTIVE` target of the
   evaluation period wins over a department-level target of the employee's own department; within a level the
   highest `version` wins. Targets of other statuses are ignored.
4. Aggregate the `VALID` actual progress of the period per KPI by `aggregationType`:
   `SUM` = sum of values, `AVG` = average of values, `RATIO` = sum of values (`actualValue`) over
   sum of denominators (`actualDenominator`). The `AVG` actual is rounded to 4 decimals with banker's
   rounding. A KPI without data is not stored (`null` is N/A, never `0`).
5. Write one group-level `performance_contribution` per evaluation profile item (weight snapshot) and one
   KPI-level `performance_contribution` per KPI with a target and data (target and actual snapshots).
6. Score through `IPerformanceScoreFormula` (KPI → group → total) and round every score to 4 decimals with
   banker's rounding (`MidpointRounding.ToEven`).

**The score formula is waiting for confirmation.** Until it is confirmed, the formula returns
`null` for every score: contributions are persisted with their target/actual snapshots,
`achievementPercentRaw`, `achievementPercentCapped` and `componentScore` are `null`, and the evaluation
has `totalScore = null`, `calculatedAt = null` and `status = DRAFT`. With a defined total the
evaluation becomes `CALCULATED` with `calculatedAt` set. The stored score always reflects the latest
calculation: when a recalculation cannot determine a total, a previous score is cleared and the status
returns to `DRAFT`.

Actual progress is not linked to contributions: `actual_progress` is stored at `DAY` level while a
contribution is stored at the evaluation period level, which the database rejects for non-`DAY` periods.

### Success — 200

`data`: `PerformanceEvaluationDto`.

```json
{
  "success": true,
  "status": 200,
  "message": "Tính điểm KPI thành công.",
  "data": {
    "id": "7c0d3f0e-6f0a-4f6e-9d57-2a2b5b2f0a11",
    "employeeId": "5eed0000-0000-0000-0007-000000000004",
    "evaluationPeriodId": "5eed0000-0000-0000-000f-000000000003",
    "evaluationProfileId": "5eed0000-0000-0000-0002-000000000001",
    "totalScore": null,
    "status": "DRAFT",
    "calculatedAt": null,
    "updatedAt": "2026-10-21T04:10:00+00:00",
    "groups": [
      {
        "contributionId": "0b8c8f58-0d44-4e0a-8c56-9a5c1f3f2a01",
        "evaluationProfileItemId": "5eed0000-0000-0000-0004-000000000001",
        "kpiGroupId": "5eed0000-0000-0000-0003-000000000001",
        "kpiGroupCode": "KD",
        "kpiGroupName": "Kinh doanh",
        "weightPercent": 70.0,
        "componentScore": null,
        "kpis": [
          {
            "contributionId": "3d1c52b8-2a7b-43f4-8a8f-0e7c2c6d9b02",
            "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
            "kpiCode": "HDV",
            "kpiName": "Huy động vốn",
            "unit": "tỷ VND",
            "targetAssignmentId": "5eed0000-0000-0000-0015-000000000008",
            "targetValue": 225.0,
            "targetVersion": 1,
            "actualValue": 40.0,
            "actualDenominator": null,
            "achievementPercentRaw": null,
            "achievementPercentCapped": null,
            "componentScore": null
          }
        ]
      }
    ]
  },
  "traceId": "request-trace-id"
}
```

Field rules: IDs are GUID strings; timestamps are ISO 8601 with offset; decimals have at most 4 places;
`groups` follow the profile `displayOrder`; `kpis` are ordered by `kpiCode` (ordinal). A group without any KPI
data has an empty `kpis` array.

### Errors

Errors use the standard envelope; clients branch on `errorCode`.

| Situation | HTTP | errorCode |
| --- | --- | --- |
| Missing, expired or malformed token | 401 | `UNAUTHORIZED` |
| Missing `evaluation:calculate` permission, unknown/locked/inactive account, or employee outside the caller's scope | 403 | `FORBIDDEN` |
| Evaluation does not exist | 404 | `NOT_FOUND` |
| Evaluation status is `IN_REVIEW`, `APPROVED`, `DISPUTED` or `FINAL` | 409 | `PERFORMANCE_EVALUATION_INVALID_STATE` |
| Evaluation period status is `LOCKED` or `CLOSED` | 409 | `PERFORMANCE_EVALUATION_INVALID_STATE` |
| Another calculation of the same evaluation committed first (unique violation) | 409 | `CONFLICT` |
| Unexpected failure | 500 | `INTERNAL_ERROR` |

`PERFORMANCE_EVALUATION_INVALID_STATE` is defined in `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs`.
