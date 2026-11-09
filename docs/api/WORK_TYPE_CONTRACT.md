# Work type detail

Status: backend implemented. Typed client: `frontend/src/app/core/work-type.service.ts`. Routes use the
backend origin, no API prefix; all responses use `ApiResponse<T>`.

Frontend screens using this contract:

- `/work-type-detail[/:id]`: read-only detail (uses `GET /work-types`, `GET /work-types/{id}`).
- `/work-types` (SCR-CFG-02, admin route guard): list and detail drawer, opened from the admin sidebar
  item `workTypes` (see `SIDEBAR_CONFIGURATION_CONTRACT.md`; `/work-types` is in the route allowlist).
  Uses `GET /work-types`,
  `GET /work-types/kpis` (KPI filter and result unit), `PATCH /work-types/{id}/status`
  (activate / deactivate), `GET /job-positions` and `GET /job-positions/{id}/work-types`
  (job position tabs and badges, see `JOB_POSITION_CONTRACT.md`), and `GET /works?workTypeId=&status=`
  (open / completed work counts, see `WORK_CONTRACT.md`).

`POST`, `PUT` and `DELETE` exist on the backend but no screen calls them yet: the "Create WorkType" and
"Edit" buttons are disabled until those screens exist.

## Authorization

| Endpoint | Permission |
|---|---|
| `GET /work-types`, `GET /work-types/{id}`, `GET /work-types/kpis` (also `GET /kpis`) | `[Authorize]` only (any authenticated account) |
| `POST /work-types`, `PUT /work-types/{id}`, `PATCH /work-types/{id}/status`, `DELETE /work-types/{id}` | `work-type:manage`, scope `All` |

`work-type:manage` is held through the `ADMIN` role's wildcard `FULL_ACCESS`. `SYSTEM_ADMIN` is accepted by
the `/work-types` route guard but has **no** `work-type:manage` grant, so activating or deactivating a work
type as `SYSTEM_ADMIN` returns 403 `FORBIDDEN` and the drawer shows the action error. Granting the permission
to `SYSTEM_ADMIN` (a seed / migration change) or narrowing the route guard is an open decision.

## Endpoints

### `GET /work-types`

Query: `keyword`, `status`, `workflowDefinitionId`, `page` (default 1), `pageSize` (default 20,
clamped 1..100). `data`: `PagedResult<WorkTypeDetail>`. The list screens load up to 100 items and filter,
search and page in the browser.

### `GET /work-types/kpis`

`data`: array of `{ id, code, name, unit }` (KPI options with their result unit).

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

### `PATCH /work-types/{id}/status`

Body `{ "status": "ACTIVE" | "INACTIVE" | "ARCHIVED" }`. 200 `ApiResponse` (the client only checks
`success`). 403 without `work-type:manage`, 400 `VALIDATION_ERROR` for a blank or unknown value (see
"Enum API format").

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

## Derived and not available values

The screens show these values without a work type field:

- **Job position:** from `GET /job-positions/{id}/work-types`; "N/A" when no active position maps the work
  type. It is never guessed from the work type code.
- **Result unit:** the `unit` of the work type's first KPI in `GET /work-types/kpis`.
- **Transaction / CIF code:** "Required" when the work type has a `productCode` or a required KPI, otherwise
  "None". This is a display rule, not a backend field.
- **Open / completed works:** `totalCount` of `GET /works?workTypeId=&status=` (the work status groups mirror
  `WorkStatusGroups.Open` / `Completed`). It is limited by the caller's `work:read` scope, so a caller whose
  scope is below `All` sees counts for the works they can read only. "N/A" when the request fails.
- **Created by / last modified:** no endpoint or field. The list shows `createdAt` only (labelled "Created"),
  and the drawer history shows the creation time only.
- A "certified/ready" configuration status has no endpoint or field and is not rendered.
