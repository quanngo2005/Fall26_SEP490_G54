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

The same calculation also runs when the evaluation form is saved (`POST /evaluations/submit`, see
`EVALUATION_CONTRACT.md`): the form saves the evaluation and these KPI rows in one transaction, checking only
`evaluation:submit` over the employee's department, so the form, the save and this endpoint score the same data
identically. The permission check of this endpoint applies only to calls made through it.

### Processing

1. Load the evaluation, its period and the employee; check scope and state.
2. Read the `VALID` `actual_progress` rows recorded against the evaluated employee's own target assignments
   whose `progress_date` falls inside the evaluation period. Calculation never writes progress: rows are
   created when a result is verified (see the verification flow).
3. Resolve the target of every `ACTIVE` KPI leaf of the profile's weight tree: an employee-level `ACTIVE` target of the
   evaluation period wins over a department-level target of the employee's own department; within a level the
   highest `version` wins. Targets of other statuses are ignored.
4. Aggregate the `VALID` actual progress of the period per KPI by `aggregationType`:
   `SUM` = sum of values, `AVG` = average of values, `RATIO` = sum of values (`actualValue`) over
   sum of denominators (`actualDenominator`). The `AVG` actual is rounded to 4 decimals with banker's
   rounding. A KPI without data is not stored (`null` is N/A, never `0`).
   `actualValue` is the real amount (e.g. real card count): the plain aggregate of the progress rows, never multiplied.
   For a `SUM` KPI the calculation also builds `weightedActualValue`: the sum of `work_result_item.value`
   (real amount × the product multiplier approved with it, BR-48) of the `VALID` verified results behind those
   rows. Progress rows without such items count at their own value. Other aggregations (`AVG`, `RATIO`) are not
   multiplied: `weightedActualValue` equals `actualValue`.
5. Write one `performance_contribution` per KPI leaf with a target and data (target, actual and the leaf's
   weight among its siblings as `weight_snapshot`), linked to the KPI's latest progress row through `actual_progress_id`. Group scores are
   computed on every calculation and are not stored. Contributions linked to a risk event are left untouched.
6. Score through `IPerformanceScoreFormula` (KPI → group → total) and round every score to 4 decimals with
   banker's rounding (`MidpointRounding.ToEven`).
7. A root group that owns no active KPI is scored by the reviewer: it takes the qualitative score saved with the
   evaluation (the form save stores it in the comment), and counts as 0 until one is saved (awaiting BA confirmation).
   Groups with an active KPI are scored from their KPIs only.

### Score formula

For one KPI, with `target` and the `weightedActualValue` from step 4 as the actual (for a `RATIO` KPI the actual is
`actualValue / actualDenominator`, multiplied by 100 when the unit is `%`):

| `direction` | `achievementPercentRaw` |
| --- | --- |
| `HIGHER_BETTER` | `actual / target × 100` |
| `LOWER_BETTER` | `target / actual × 100`; with an actual of 0 it is the KPI's `maxAchievementPercent`, or 100 when there is none |
| `ON_TARGET` | `100 − |actual − target| / target × 100` |

- The raw value is never below 0.
- `achievementPercentCapped` is the raw value limited by the KPI's `maxAchievementPercent` when it is set,
  otherwise it equals the raw value. It is never above the raw value.
- The KPI's `componentScore` is `achievementPercentCapped`.
- A KPI whose target is 0, or a `RATIO` KPI without a positive denominator, has no score (`null`, shown as N/A).
- The profile is a tree: root nodes are groups, a group has KPI leaves and/or sub-groups (at most 3 levels), and the
  `weightPercent` of a node is relative to its siblings (siblings add up to 100 on an `ACTIVE` profile).
- An **active** KPI that cannot be scored (no target, no progress, target 0, or a `RATIO` without a denominator)
  **counts as 0 and keeps its weight** in its group (awaiting BA confirmation); its detail row is still not stored and
  it is shown as N/A. An **inactive** KPI is not part of the profile: it has no score and leaves its share to its
  siblings.
- A group's score is the mean of the scores of its parts (KPI leaves and sub-groups) weighted by `weightPercent`
  and normalised over the parts that have a score: `Σ(score × weight) / Σ(weight of parts with a score)`. Because
  an active KPI always has a score (0 when it cannot be scored), only a group without any active KPI has none.
- `totalScore` is the sum over root groups of `group score × weightPercent / 100` (the weighted score), minus the
  risk penalty (never below 0), plus the appeal adjustments of the period (`score_entry`). Every root group has a score
  (a KPI group scores 0 when none of its KPIs can be scored, a qualitative group the reviewer has not scored counts as 0,
  the same rule as above), so the total is `null` only when the profile has no root group.
- The risk penalty is the sum of the penalty of every risk event of the employee inside the period that was not
  dismissed, by severity (`EvaluationService.MapRiskPenalty`, shared with the risk workflow).
- Every score is rounded to 4 decimals with banker's rounding (`MidpointRounding.ToEven`).

With a total the evaluation becomes `CALCULATED` with `calculatedAt` set; without one it keeps
`totalScore = null`, `calculatedAt = null` and `status = DRAFT`. The stored score always reflects the latest
calculation: when a recalculation cannot determine a total, a previous score is cleared and the status
returns to `DRAFT`. Saving the evaluation form as a draft (`POST /evaluations/submit` with `isDraft = true`) runs
the same calculation but does not demote an evaluation that is already `CALCULATED`: it stays `CALCULATED` while a
total exists.

Limits: the per-product multiplier (BR-48 Task Multiplier) lives on `WorkTypeKpi.Multiplier` and is applied by
`VerifiedResultRecorder` when a verified result is recorded, but only as a snapshot on `WorkResultItem`
(`rawValue`, `multiplierApplied`, `value`). `ActualProgress` keeps the real amount; the multiplier converts it to
points only in this calculation (`weightedActualValue`, stored in `performance_contribution.weighted_actual_value`).
There is still no separate
per-evaluation or per-period multiplier (BR-32); to change a rate for a period, add a new `WorkTypeKpi` row with a
new `EffectiveFrom`. Everything that cannot be scored counts as 0 at every level (a KPI without a target or progress, a qualitative group
not yet scored); this rule awaits BA confirmation. Two calculations of the same existing
evaluation at the same time can overwrite each other (the last one wins; the evaluation has no concurrency token). A
unique index on (evaluation, anchoring progress row) for KPI rows stops two requests from inserting the same KPI row
(409 `CONFLICT`); it cannot stop them when the progress changes between the two requests.

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
    "totalScore": 12.4445,
    "status": "CALCULATED",
    "calculatedAt": "2026-10-21T04:10:00+00:00",
    "updatedAt": "2026-10-21T04:10:00+00:00",
    "groups": [
      {
        "evaluationProfileItemId": "5eed0000-0000-0000-0004-000000000001",
        "groupName": "Kinh doanh",
        "weightPercent": 70.0,
        "componentScore": 17.7778,
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
            "achievementPercentRaw": 17.7778,
            "achievementPercentCapped": 17.7778,
            "componentScore": 17.7778
          }
        ]
      }
    ]
  },
  "traceId": "request-trace-id"
}
```

Field rules: IDs are GUID strings; timestamps are ISO 8601 with offset; decimals have at most 4 places;
`groups` are the root groups of the profile tree in `displayOrder`; `evaluationProfileItemId` is the tree node and `kpis` lists the KPI leaves of the group and of its sub-groups; `kpis` are ordered by `kpiCode` (ordinal). A group without any KPI
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
