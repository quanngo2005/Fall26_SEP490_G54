# Work type detail

Status: backend implemented (read endpoints used by the frontend). Typed client:
`frontend/src/app/core/work-type.service.ts`. Routes use the backend origin, no API prefix; all
responses use `ApiResponse<T>`. Write endpoints (`POST`, `PUT`, `PATCH /{id}/status`, `DELETE`) and
the `/work-types/kpis`, `/work-types/workflows` lookups exist on the backend but are **not used** by
the work type detail page.

## Authorization

`[Authorize]` only (any authenticated account).

## Endpoints

### `GET /work-types`

Query: `keyword`, `status`, `workflowDefinitionId`, `page` (default 1), `pageSize` (default 20,
clamped 1..100). `data`: `PagedResult<WorkTypeDetail>`. The page loads up to 100 items to fill the
work type picker.

### `GET /work-types/{id}`

404 `NOT_FOUND` for an unknown id. `data`: `WorkTypeDetail`.

```json
{
  "id": "5eed0000-0000-0000-000c-000000000002",
  "code": "HUY_DONG",
  "name": "Huy động tiền gửi",
  "description": null,
  "productCode": null,
  "workflowDefinitionId": "5eed0000-0000-0000-000d-000000000001",
  "workflowName": "Quy trình huy động",
  "status": "ACTIVE",
  "createdAt": "2026-01-01T00:00:00+00:00",
  "kpis": [
    {
      "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000001",
      "kpiCode": "HDV",
      "kpiName": "Huy động vốn",
      "isRequired": true,
      "multiplier": 1.0,
      "effectiveFrom": "2026-01-01",
      "effectiveTo": null
    }
  ]
}
```

`description`, `productCode` and `kpis[].effectiveTo` are nullable (`null` end date = no end, `null`
product code = not matched to any product). Date-only values are `YYYY-MM-DD`. The mapping state
shown in the UI (current / upcoming / expired) is derived from `effectiveFrom`/`effectiveTo` against
today's date in Vietnam (UTC+7); it is not a wire field.

### Product code (BR-48 Task Multiplier)

`productCode` (max 50 chars, trimmed, blank saved as `null`) identifies the product this work type
represents (e.g. a credit card tier). At most one ACTIVE/INACTIVE/ARCHIVED work type may hold a given
non-null `productCode`; a second `POST`/`PUT` with the same code returns 409 `CONFLICT` (checked
up front and on the unique-index violation, mirroring the duplicate `code` check).

`kpis[].multiplier` (`decimal(6,4)`, default `1.0`, must be `> 0` and `<= 10`) is the per-mapping
weight applied when a verified result is recorded for that KPI during the mapping's effective window
(`VerifiedResultRecorder`, see `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`). A value outside
`(0, 10]` is 400 `VALIDATION_ERROR`.

The transaction matcher (BR-09, not built yet) must require
`ImportedTransaction.product == WorkType.productCode`; a mismatch is Not Matched and does not
auto-approve.

## Enum API format

`status` ∈ `ACTIVE | INACTIVE | ARCHIVED` (UPPER_SNAKE string, the `RecordStatus` PostgreSQL names).

Responses are produced with `EnumApiFormat.ToEnumString()`. The `status` query filter and the
`PATCH /work-types/{id}/status` body are parsed with `EnumApiFormat.ParseEnum()` (case-insensitive); a
blank value on the PATCH, an unknown value, or an ordinal such as `1` is 400 `VALIDATION_ERROR`.
Ordinals (which used to start at 1) are no longer accepted. The client reads the string as-is, with no
adapter.

## Not available (not shown)

Linked job positions, campaigns, work usage totals and any "certified/ready" configuration status
have no endpoint or field, so the page does not render them.
