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
      "effectiveFrom": "2026-01-01",
      "effectiveTo": null
    }
  ]
}
```

`description` and `kpis[].effectiveTo` are nullable (`null` end date = no end). Date-only values are
`YYYY-MM-DD`. The mapping state shown in the UI (current / upcoming / expired) is derived from
`effectiveFrom`/`effectiveTo` against today's UTC date; it is not a wire field.

## Enum wire format

`status` ∈ `ACTIVE | INACTIVE | ARCHIVED` (UPPER_SNAKE string, the `RecordStatus` PostgreSQL names).

Responses are produced with `EnumWireFormat.ToWire()`. The `status` query filter and the
`PATCH /work-types/{id}/status` body are parsed with `EnumWireFormat.ParseWire()` (case-insensitive); a
blank value on the PATCH, an unknown value, or an ordinal such as `1` is 400 `VALIDATION_ERROR`.
Ordinals (which used to start at 1) are no longer accepted. The client reads the string as-is, with no
adapter.

## Not available (not shown)

Linked job positions, campaigns, work usage totals and any "certified/ready" configuration status
have no endpoint or field, so the page does not render them.
