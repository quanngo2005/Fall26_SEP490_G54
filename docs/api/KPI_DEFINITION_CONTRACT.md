# KPI definitions

Status: backend implemented. Typed client: `frontend/src/app/core/kpi-definition.service.ts`.
Routes use the backend origin, no API prefix; all responses use `ApiResponse<T>`.

## Authorization

`[Authorize]` on the controller; the frontend route additionally requires an authenticated session
(`authGuard`). The sidebar entry is gated by `KPI_MANAGE`, which is UX only. The API is the source of
authorization.

## Endpoints (all implemented)

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| GET | `/kpi-definitions` | query `keyword`, `kpiGroupId`, `status`, `aggregationType`, `page`, `pageSize` | 200 `PagedResult<KpiDefinition>` |
| GET | `/kpi-definitions/groups` | — | 200 `KpiGroupOption[]` |
| GET | `/kpi-definitions/{id}` | — | 200 `KpiDefinition` (404 `NOT_FOUND`) |
| POST | `/kpi-definitions` | `KpiDefinitionSave` | 201 `KpiDefinition`, `Location` header |
| PUT | `/kpi-definitions/{id}` | `KpiDefinitionSave` (full replacement) | 200 `KpiDefinition` (404, 409) |
| PATCH | `/kpi-definitions/{id}/status` | `{ "status": ... }` | 200 (not used by the page) |

`page` default 1; `pageSize` default 20, clamped 1..100. `keyword` matches code/name.
Duplicate `code` is a 409 `CONFLICT`; the client branches on the HTTP status / `errorCode`, never on
the message.

```json
{
  "id": "5eed0000-0000-0000-0005-000000000001",
  "kpiGroupId": "5eed0000-0000-0000-0004-000000000001",
  "kpiGroupName": "Kinh doanh",
  "code": "HDV",
  "name": "Huy động vốn",
  "description": null,
  "formula": null,
  "unit": "tỷ VND",
  "aggregationType": "SUM",
  "direction": "HIGHER_BETTER",
  "maxAchievementPercent": 150,
  "status": "ACTIVE",
  "updatedAt": "2026-10-14T08:30:00+00:00"
}
```

`KpiDefinitionSave`: `kpiGroupId` (GUID, required), `code` (required, ≤ 50), `name` (required,
≤ 255), `description` (≤ 2000, nullable), `formula` (≤ 1000, nullable), `unit` (required, ≤ 50, free
text), `aggregationType`, `direction`, `maxAchievementPercent` (nullable, 0.0001..9999.9999).

`unit` is free text. The page suggests the distinct units already returned by the API; there is no
fixed unit list.

## Enum wire format

Target: `aggregationType` ∈ `SUM | RATIO | AVG | NO_AGGREGATION`, `direction` ∈
`HIGHER_BETTER | LOWER_BETTER | ON_TARGET`, `status` ∈ `DRAFT | ACTIVE | INACTIVE | ARCHIVED`.

**Current backend behaviour:** no string-enum converter is registered, so these are serialized as
declaration ordinals (`SUM=0, RATIO=1, AVG=2, NO_AGGREGATION=3`; `HIGHER_BETTER=0, LOWER_BETTER=1,
ON_TARGET=2`; `DRAFT=0, ACTIVE=1, INACTIVE=2, ARCHIVED=3`) and request bodies must use ordinals. The
client adapter (`core/enum-wire.ts`) reads either form and writes ordinals (accepted both by the
current backend and by a future string-enum backend). Inferred from the backend source, not verified
against a running API.

## Not persisted (client-only drafts)

Role weights and deposit multipliers are **not** part of `KpiDefinition` or `KpiDefinitionSave`; no
endpoint stores them, so the page keeps them as clearly labelled browser-session drafts only. They are
never sent to the API and are lost on reload. Measurement type (quantitative/qualitative), version and
effective date are likewise not in the DTO and are not shown. Defining these requires a backend
contract revision first.
