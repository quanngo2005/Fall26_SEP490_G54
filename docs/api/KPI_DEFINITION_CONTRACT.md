# KPI definitions

Status: backend implemented. Typed client: `frontend/src/app/core/kpi-definition.service.ts`.
Routes use the backend origin, no API prefix; all responses use `ApiResponse<T>`.

## Authorization

`[Authorize]` on the controller; the frontend route additionally requires an authenticated session
(`authGuard`). The sidebar entry is gated by `KPI_MANAGE`, which is UX only. The API is the source of
authorization.

| Operation | Policy |
| --- | --- |
| `GET /`, `/groups`, `/{id}` | `[Authorize]` only (unchanged) |
| `GET /code-suggestion`, `/summary`, `/{id}/usage`, `/{id}/history` | permission `kpi:read`, scope `All` (granted to every role) |
| `POST`, `PUT`, `PATCH /{id}/status` | permission `kpi:manage`, scope `All` (unchanged) |
| `POST /requests` | permission `kpi:request`, scope `All` |
| `POST /{id}/approve`, `POST /{id}/reject` | permission `kpi:approve`, scope `All` |

`kpi:request` and `kpi:approve` are granted to BRANCH_MANAGER (migration `AddObjectiveWeightAndKpiRequestApproval`);
ADMIN passes every permission policy.

## Endpoints (all implemented)

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| GET | `/kpi-definitions` | query `keyword`, `kpiGroupId`, `status`, `aggregationType`, `jobPositionId`, `creditRole`, `dataSource`, `effectiveAtPeriodId`, `page`, `pageSize` | 200 `PagedResult<KpiDefinition>`, ordered by product (KPIs without one last), then code |
| GET | `/kpi-definitions/code-suggestion` | query `name` (required) | 200 `{ "code": ... }`: the unused code a KPI with that name would get (400 for an empty name) |
| GET | `/kpi-definitions/summary` | — | 200 `KpiSummary` |
| GET | `/kpi-definitions/{id}/usage` | — | 200 `KpiUsage` (404) |
| GET | `/kpi-definitions/{id}/history` | — | 200 `KpiHistoryEntry[]`, oldest first (404) |
| GET | `/kpi-definitions/groups` | — | 200 `KpiGroupOption[]` |
| GET | `/kpi-definitions/{id}` | — | 200 `KpiDefinition` (404 `NOT_FOUND`) |
| POST | `/kpi-definitions` | `KpiDefinitionSave` | 201 `KpiDefinition`, `Location` header |
| PUT | `/kpi-definitions/{id}` | `KpiDefinitionSave` (full replacement) | 200 `KpiDefinition` (404, 409) |
| POST | `/kpi-definitions/requests` | `KpiRequest` | 201 `KpiDefinition`, `Location` header (400, 409) |
| PATCH | `/kpi-definitions/{id}/status` | `{ "status": ... }` | 200 (404, 409; not used by the page) |
| POST | `/kpi-definitions/{id}/approve` | — | 200 (404, 409, 422 `MAKER_CANNOT_BE_CHECKER`) |
| POST | `/kpi-definitions/{id}/reject` | `{ "reason": ... }` | 200 (400, 404, 409, 422 `MAKER_CANNOT_BE_CHECKER`) |

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

`KpiDefinitionSave`: `kpiGroupId` (GUID, required), `code` (≤ 50; **leave it empty on create**, the server generates it from `name`; required on update and never changed), `name` (required,
≤ 255), `description` (≤ 2000, nullable), `formula` (≤ 1000, nullable), `unit` (required, ≤ 50, free
text), `aggregationType`, `direction`, `maxAchievementPercent` (nullable, 0.0001..9999.9999).

### Generated code

A new KPI gets its code from its name: upper case, no accents (`đ` becomes `D`), every other character becomes `_`, words are
joined by one `_` and the code is cut at 40 characters (`Tiền gửi - giới thiệu` becomes `TIEN_GUI_GIOI_THIEU`). When that code is
taken the next free number is added (`TIEN_GUI_GIOI_THIEU_2`). `GET /kpi-definitions/code-suggestion?name=` returns the code a name
would get, so the form can show it read-only while the user types; a client that sends its own `code` on create keeps it
(409 `CONFLICT` when it exists). The code of an existing KPI cannot be edited.

### Catalog fields (SCR-STRAT-02)

Added to `KpiDefinition` (response) and `KpiDefinitionSave` (request). All are optional on save, so an old client keeps working.

| Field | Save | Response | Rules |
| --- | --- | --- | --- |
| `creditRole` | `PROCESSOR` or `REFERRER` | same | Whose work earns the KPI: the employee who handled the transaction, or the one in its Refer ID column. Default `PROCESSOR`; **omitted on update = keep the current value** |
| `dataSource` | `TRANSACTION` or `SERVICE_RATING` | same | Imported file the KPI is measured from. Default `TRANSACTION`; omitted on update = keep |
| `productName` | string ≤ 100, nullable | string or `null` | The list groups rows by it; `null` = not tied to a product |
| `baselineValue` | number ≥ 0, nullable | number or `null` | |
| `jobPositionIds` | GUID[] | `jobPositions: { id, code, title }[]` | Each must exist and be `ACTIVE`; replaces the whole set (omitted = none) |
| `effectiveFromPeriodId` / `effectiveToPeriodId` | GUID, nullable | `effectiveFrom` / `effectiveTo`: `{ id, code, name, startDate, endDate }` or `null` | Quarter periods only. `null` from = always applied, `null` to = no end. To needs From and must not be before it. A locked or closed period cannot be chosen (400, `(MSG27)`), except one the KPI already uses |
| — | — | `submittedAt` | When the KPI was sent for approval |

`PUT` replaces every catalog field except `creditRole` and `dataSource` (see above).
`GET /kpi-definitions?effectiveAtPeriodId=` keeps the KPIs in force in that period: its start lies between the start of the KPI's
first and last period, an open side counting as in force (404 for an unknown period).

`KpiSummary`: `{ total, draft, pendingApproval, rejected, active, inactive, archived }`, for the tabs of the list.

`KpiUsage`: `{ objectives: { id, code }[], activeTargetCount, relatedItems }`. `relatedItems` are the KPIs of the same product
(the KPI itself included, empty without a product), each `{ id, code, name, creditRole, dataSource, jobPositions: string[] }`.

`KpiHistoryEntry`: `{ occurredAt, actorName, action, summary }` from the audit log. `action` is `CREATE`, `SUBMIT`, `APPROVE`,
`REJECT` (the summary carries the reason), `DEACTIVATE`, `REACTIVATE`, `ARCHIVE` or `UPDATE`; an update that only touched the
timestamp is left out.

`unit` is free text. The page suggests the distinct units already returned by the API; there is no
fixed unit list.

## Enum API format

`aggregationType` ∈ `SUM | RATIO | AVG | NO_AGGREGATION`, `direction` ∈
`HIGHER_BETTER | LOWER_BETTER | ON_TARGET`, `status` ∈
`DRAFT | PENDING_APPROVAL | REJECTED | ACTIVE | INACTIVE | ARCHIVED`.

These are UPPER_SNAKE **strings in both directions**: responses are produced with
`EnumApiFormat.ToEnumString()`; request bodies (`aggregationType`, `direction`, `PATCH .../status`) and the
`status` / `aggregationType` query filters are parsed with `EnumApiFormat.ParseEnum()`
(case-insensitive). A blank value for a required field, an unknown value, a C# member name such as
`HigherBetter`, or an ordinal such as `1` is rejected with 400 `VALIDATION_ERROR`; ordinals are no longer
accepted. The client sends and reads the strings as-is, with no adapter.

## Request and approval flow

The System Admin creates KPIs and the Branch Manager approves them; the Branch Manager can also ask for a KPI that is
missing while creating an objective.

- `POST /kpi-definitions` (System Admin) always creates a `DRAFT`.
- `POST /kpi-definitions/requests` (Branch Manager) creates a `DRAFT` too, from `KpiRequest` =
  `{ "kpi": KpiDefinitionSave, "requestNote": "..." }`. Every required field of `KpiDefinitionSave` is still required
  (nothing is filled in for the manager). `requestNote` is required and limited to the length of its column; the message
  states the limit. The KPI records `requestedBy` (the manager's employee id) and `requestNote`, both returned by the
  KPI endpoints (`null` for a KPI the Admin created directly). The Admin completes it with `PUT`, then submits it.
- Duplicate `code` on any create or update is 409 `CONFLICT` (also when two requests race).
- `rejectReason` is returned only while the status is `REJECTED`.

Allowed transitions of `PATCH /{id}/status`:

| From | To |
| --- | --- |
| `DRAFT` | `PENDING_APPROVAL`, `ARCHIVED` |
| `PENDING_APPROVAL` | `ARCHIVED` (leaves otherwise only through approve / reject) |
| `REJECTED` | `PENDING_APPROVAL`, `ARCHIVED` |
| `ACTIVE` | `INACTIVE`, `ARCHIVED` |
| `INACTIVE` | `ACTIVE`, `ARCHIVED` |
| `ARCHIVED` | none (409 `CONFLICT`) |

Any other transition (including `DRAFT -> ACTIVE`, which used to be allowed) is 409 `CONFLICT`; setting the current
status again is a no-op (200). Sending for approval records the caller as the maker (`submittedBy`, not returned) and clears
`rejectReason`. A `PENDING_APPROVAL` KPI cannot be edited (`PUT`: 409 `CONFLICT`); a `REJECTED` one can be edited and resubmitted.

`POST /{id}/approve` moves a `PENDING_APPROVAL` KPI to `ACTIVE`; `POST /{id}/reject` moves it to `REJECTED` and
stores `reason` (required, limited to the length of its column). Any other status is 409 `CONFLICT`. The approver must not
be the employee who sent the KPI for approval (the maker): 422 `MAKER_CANNOT_BE_CHECKER`. The Branch Manager who requested
the KPI only asked for it, so may approve or reject it once the System Admin has completed and submitted it (a branch with a
single Branch Manager would otherwise never get the KPI approved).

Known limits:

- Editing a KPI that is already `ACTIVE` (`PUT`) takes effect at once and does not go through approval; "changed
  KPI" approval would need versioning and is not built.
- `GET` endpoints are `[Authorize]` only, so any signed-in user can read `rejectReason`, `requestNote` and `requestedBy`
  (an employee id) of a KPI. Not restricted, to leave the permission of the existing reads unchanged.
- `ADMIN` passes every permission policy, so an Admin other than the one who submitted a KPI can approve it.
- `requested_by` and `submitted_by` are foreign keys without an index of their own (no query filters on them); the KPI table
  is small.

## Not persisted (client-only drafts)

Role weights and deposit multipliers are **not** part of `KpiDefinition` or `KpiDefinitionSave`; no
endpoint stores them. Measurement type (quantitative/qualitative) and version are likewise not in the DTO.
Defining these requires a backend contract revision first. (Effective period, product, credit role,
data source and applicable positions are stored: see the catalog fields above.)
