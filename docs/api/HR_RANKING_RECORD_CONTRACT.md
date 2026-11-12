# HR Ranking Records API Contract (GB-650)

HR Officer views the locked ranking records of an evaluation period (UC110, UC111). Read only; xlsx/PDF export is not part
of this contract.

Following [API_CONVENTIONS.md](API_CONVENTIONS.md): resource root `/ranking-records`, `ApiResponse<T>` envelope,
`PagedResult<T>` inside `data`, camelCase JSON, Bearer token.

Status: **implemented** (backend). No frontend client yet.

## 1. Endpoints

| Method | Endpoint | Description | Permission |
| --- | --- | --- | --- |
| `GET` | `/ranking-records` | Paged, filterable list of the ranking records of one locked period | `ranking_record:read` (All) |
| `GET` | `/ranking-records/{evaluationId}` | One ranking record in detail | `ranking_record:read` (All) |

`ranking_record:read` is granted with scope `All` to `HR_OFFICER`, `BRANCH_MANAGER`, `ADMIN` and `SYSTEM_ADMIN`
(migration `GrantRankingRecordReadToHr`).

## 2. What a ranking record is

One record is one `PerformanceEvaluation` that is `FINAL` and has a score, in a period whose status is `LOCKED`. A period
that is not locked has no records: the API answers 409 `EVALUATION_PERIOD_INVALID_STATE` instead of showing figures that can
still change.

- **Rank**: position of the employee inside the group of the same department and the same job position, by `totalScore`
  DESC then `employeeCode` ASC. Ranks are consecutive (1, 2, 3 ...), never shared. `groupSize` is the number of records in
  that group. The rank does not depend on the filters: it is computed over the whole group before filtering.
- **Tier**: the tier the Branch Manager finalized. It is the calibrated tier when a department calibration set one,
  otherwise `proposedTier`.
- **proposedTier**: the tier of `totalScore` from `ranking_tier_threshold`, capped by the downgrade rule when the employee
  has a risk event of at least the configured severity in the period (same rule as the calibration screen).
- Tier codes are the ones configured in `ranking_tier_threshold` (`A1`, `A2`, `A3`, `B`, `C` by default).

## 3. `GET /ranking-records`

### Query parameters

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `periodId` | GUID | yes | Evaluation period. Must be `LOCKED`. |
| `departmentId` | GUID | no | This department and its sub-departments. |
| `jobPositionId` | GUID | no | Job position of the employee. |
| `tier` | string | no | A configured tier code (any casing). |
| `keyword` | string | no | Case-insensitive contains on employee code or full name. |
| `page` | int | no | Default 1; below 1 is treated as 1. |
| `pageSize` | int | no | Default 20; clamped to 1..100. |

Sorting is fixed: department name, job position title, rank, then employee code.

### 200 OK

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "evaluationId": "6f0c7c64-3a62-4f4e-9d38-0b9f6d1f1a01",
        "employeeId": "e1f2a3b4-0000-0000-0000-000000000001",
        "employeeCode": "NV-0214",
        "fullName": "Nguyễn Thu Thảo",
        "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
        "departmentName": "Phòng Khách hàng Cá nhân",
        "jobPositionId": "b1b2c3d4-0000-0000-0000-000000000009",
        "jobPositionTitle": "Giao dịch viên",
        "totalScore": 95.0,
        "rank": 1,
        "groupSize": 12,
        "proposedTier": "A1",
        "tier": "A1",
        "isAdjusted": false,
        "lockedAt": "2026-10-11T10:00:00+00:00"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "0HMS12345678"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `evaluationId` | GUID | Id of the record; use it in the detail route. |
| `totalScore` | decimal | Stored score of the evaluation. |
| `rank` / `groupSize` | int | See section 2. |
| `proposedTier` / `tier` | string | See section 2. |
| `isAdjusted` | bool | `tier` differs from `proposedTier`. |
| `lockedAt` | ISO 8601 or null | `approved_at` of the evaluation (set when the period was locked). |

An empty result is `items: []`, `totalCount: 0`.

## 4. `GET /ranking-records/{evaluationId}`

Returns the list fields plus the period and the review information.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "record": { "evaluationId": "6f0c7c64-3a62-4f4e-9d38-0b9f6d1f1a01", "...": "same fields as a list item" },
    "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
    "periodCode": "Q3-2026",
    "periodName": "Kỳ đánh giá Quý 3/2026",
    "adjustmentReason": null,
    "coachingPlan": null,
    "approvalComments": "Phê duyệt chính thức kết quả đánh giá và xếp hạng toàn chi nhánh.",
    "acknowledgedAt": "2026-10-12T02:15:00+00:00"
  },
  "traceId": "0HMS12345678"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `adjustmentReason` | string or null | Reason the Department Manager gave when calibrating the tier. |
| `coachingPlan` | string or null | Coaching plan saved with the evaluation. |
| `approvalComments` | string or null | Comment of the Branch Manager at finalize. |
| `acknowledgedAt` | ISO 8601 or null | When the employee acknowledged the result (UC105); null until then. |

Component scores (business, service quality, operations) are not stored with a finalized evaluation and are not returned.

## 5. Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | `periodId` missing, or `tier` is not a configured tier code |
| 401 | `UNAUTHORIZED` | No or invalid token |
| 403 | `FORBIDDEN` | The caller lacks `ranking_record:read` at scope `All` |
| 404 | `NOT_FOUND` | Unknown period or department; the detail id is not a `FINAL` evaluation of a `LOCKED` period |
| 409 | `EVALUATION_PERIOD_INVALID_STATE` | The period is not `LOCKED` |
