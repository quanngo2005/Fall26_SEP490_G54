# Evaluation profile management

Status: backend implemented (no frontend client yet). Routes use the backend origin, no API prefix; all
responses use `ApiResponse<T>`. An evaluation profile defines, for one job position, which KPI groups are
scored and with what weight, as a weight tree. `EvaluationService` / `PerformanceEvaluationService` read these profiles when
calculating performance scores; this contract only manages them.

## Authorization

| Operation | Policy |
| --- | --- |
| `GET` endpoints | `[Authorize]` + permission `kpi:read`, scope `All` |
| `POST`, `PUT`, `PATCH /{id}/status`, `DELETE` | permission `kpi:manage`, scope `All` |

ADMIN passes every permission policy. The seed grants `kpi:manage` to ADMIN only, so in practice only ADMIN
can write. 401 when unauthenticated, 403 when the permission is missing.

## Lookups (existing endpoints, not duplicated)

- Job positions: `GET /employees/job-positions` -> `{ id, code, title }`.
- KPI definitions: `GET /kpi-definitions` -> the KPIs a leaf can point at.

## Data model rules

- A profile belongs to exactly one job position and has a **tree** of items. An item is a node with a
  `weightPercent` and a `displayOrder`, relative to its siblings. A **group** node has a `name` and children; a **KPI
  leaf** has a `kpiDefinitionId` and no children. Exactly one of `name` and `kpiDefinitionId` is set. Roots are groups,
  the tree has at most 3 levels, and a KPI appears once per profile. Group names are unique among siblings.
- Versioning: `(code, version)` is unique. `POST` with a `code` that already exists creates the next version
  (`version = max(version) + 1`) for the same job position. A new profile starts at version 1. `version` is
  never client-supplied and never changes on update.
- A profile is **in use** (`isInUse = true`) once any performance evaluation references it.
- Only one profile per `code` can be `ACTIVE`, and a job position cannot have two `ACTIVE` profiles whose
  effective periods overlap. A period with `effectiveTo = null` is open-ended.
- `ARCHIVED` profiles are not removed from profile selection by the evaluation engine (it picks by effective
  period first, then `ACTIVE`); archiving only retires the profile from management.
- The dashboard reads weights from the current profile, not from the weight snapshot taken at calculation
  time. This is why items of an in-use profile are immutable.
- Scoring (see `PERFORMANCE_EVALUATION_CONTRACT.md`): a group scores as the weighted mean of its scored children,
  and the total is the weighted sum of the root groups.

## Status

`status` is one of `DRAFT | ACTIVE | INACTIVE | ARCHIVED` (UPPER_SNAKE, the PostgreSQL enum names).
Responses use `EnumApiFormat.ToEnumString()`; the `status` query filter and the PATCH body are parsed with
`EnumApiFormat.ParseEnum()` (case-insensitive). An unknown value or an ordinal is 400 `VALIDATION_ERROR`.

New profiles are always created as `DRAFT`. A `DRAFT` profile may have any total weight, including none.

Allowed transitions (`PATCH /{id}/status`):

| From | To |
| --- | --- |
| `DRAFT` | `ACTIVE`, `ARCHIVED` |
| `ACTIVE` | `INACTIVE`, `ARCHIVED` |
| `INACTIVE` | `ACTIVE`, `ARCHIVED` |
| `ARCHIVED` | none (409 `CONFLICT`) |

Any other transition, including a transition to `DRAFT`, is 409 `CONFLICT`. Setting the current status again
is a no-op and returns 200.

Activation (`-> ACTIVE`) requires:

1. At least one item; the weights of the roots add up to exactly `100`, and so do the children of every group; every
   group has at least one child. Otherwise 400 `VALIDATION_ERROR`.
2. The job position is `ACTIVE`, otherwise 400 `VALIDATION_ERROR`.
3. After the profile's own earlier `ACTIVE` version is retired (see below), no other `ACTIVE` profile of the same
   job position overlaps its effective period, otherwise 409 `CONFLICT`.

When a profile is activated and another version with the same `code` is `ACTIVE`, that version is changed to
`INACTIVE` in the same transaction. Its `effectiveTo` is **not** modified.

## Endpoints

### `GET /evaluation-profiles`

Query: `keyword` (case-sensitive substring of code or name, like `GET /work-types`), `status`, `jobPositionId`, `page` (default 1), `pageSize` (default
20, clamped 1..100). Sorted by `jobPositionCode`, `code`, `version` descending.
`data`: `PagedResult<EvaluationProfileListItem>`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "5eed0000-0000-0000-0010-000000000001",
        "code": "TELLER",
        "name": "Teller profile",
        "jobPositionId": "5eed0000-0000-0000-0004-000000000001",
        "jobPositionCode": "TELLER",
        "jobPositionTitle": "Teller",
        "version": 2,
        "effectiveFrom": "2026-07-01",
        "effectiveTo": null,
        "status": "ACTIVE",
        "itemCount": 3,
        "totalWeightPercent": 100.0,
        "updatedAt": "2026-10-23T07:00:00+00:00"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "request-trace-id"
}
```

### `GET /evaluation-profiles/{id}`

404 `NOT_FOUND` for an unknown id. `data`: `EvaluationProfile`.

```json
{
  "id": "5eed0000-0000-0000-0010-000000000001",
  "code": "TELLER",
  "name": "Teller profile",
  "description": null,
  "jobPositionId": "5eed0000-0000-0000-0004-000000000001",
  "jobPositionCode": "TELLER",
  "jobPositionTitle": "Teller",
  "version": 2,
  "effectiveFrom": "2026-07-01",
  "effectiveTo": null,
  "status": "ACTIVE",
  "isInUse": false,
  "totalWeightPercent": 100.0,
  "updatedAt": "2026-10-23T07:00:00+00:00",
  "items": [
    {
      "id": "5eed0000-0000-0000-0004-000000000001",
      "parentItemId": null,
      "name": "Service quality",
      "kpiDefinitionId": null,
      "kpiCode": null,
      "kpiName": null,
      "weightPercent": 100.0,
      "displayOrder": 1,
      "children": [
        {
          "id": "5eed0000-0000-0000-0004-000000000101",
          "parentItemId": "5eed0000-0000-0000-0004-000000000001",
          "name": null,
          "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000004",
          "kpiCode": "CSAT",
          "kpiName": "Customer satisfaction",
          "weightPercent": 100.0,
          "displayOrder": 1,
          "children": []
        }
      ]
    }
  ]
}
```

`description` and `effectiveTo` are nullable. `items` are the root nodes ordered by `displayOrder`; every node carries its `children`, ordered the same way.
`totalWeightPercent` is the total of the roots.

### `POST /evaluation-profiles`

Body `CreateEvaluationProfile`; creates a `DRAFT` profile. `201` with a `Location` header pointing at
`GET /evaluation-profiles/{id}`; `data`: `EvaluationProfile`.

```json
{
  "code": "TELLER",
  "name": "Teller profile",
  "description": null,
  "jobPositionId": "5eed0000-0000-0000-0004-000000000001",
  "effectiveFrom": "2026-07-01",
  "effectiveTo": null,
  "items": [
    {
      "name": "Service quality",
      "weightPercent": 100,
      "displayOrder": 1,
      "children": [
        { "kpiDefinitionId": "5eed0000-0000-0000-0005-000000000004", "weightPercent": 100, "displayOrder": 1 }
      ]
    }
  ]
}
```

| Field | Rule |
| --- | --- |
| `code` | required, trimmed, max 50 |
| `name` | required, trimmed, max 200 |
| `description` | optional, max 2000 |
| `jobPositionId` | required, must exist (404 `NOT_FOUND` otherwise) |
| `effectiveFrom` | required, `YYYY-MM-DD` |
| `effectiveTo` | optional, must be `>= effectiveFrom` |
| `items[].name` | group name, required for a group, max 200, unique among siblings; absent for a KPI leaf |
| `items[].kpiDefinitionId` | KPI of a leaf, must exist (404 `NOT_FOUND` otherwise), no duplicates in the profile; absent for a group |
| `items[].children` | children of a group (at most 3 levels in total); a leaf has none |
| `items[].weightPercent` | `> 0` and `<= 100`, at most 4 decimal places, relative to the siblings (the totals are enforced on activation) |
| `items[].displayOrder` | `>= 1` |

When the `code` already exists the request creates the next version; the `jobPositionId` must then equal the
job position of the existing versions, otherwise 409 `CONFLICT`. A second profile for the same job position
with the same `effectiveFrom` is 409 `CONFLICT`.

### `PUT /evaluation-profiles/{id}`

Body `UpdateEvaluationProfile`: same fields as create without `code` (code and version never change).
`items` **replaces** the whole tree. A node is matched with the stored one by its key among the same parent (the
KPI of a leaf, the name of a group, case-insensitive): matches are updated in place and keep their `id`, nodes
that are no longer sent are removed with everything beneath them, and new nodes get a new `id`. `data`: `EvaluationProfile`.

- `ARCHIVED` profile: 409 `CONFLICT`.
- Profile **in use**: only `name`, `description` and `effectiveTo` may differ. A body whose `jobPositionId`,
  `effectiveFrom` or items differ from the stored profile is 409 `EVALUATION_PROFILE_IN_USE`; create a new
  version instead. Sending the unchanged values is accepted.
- `ACTIVE` profile: the total weight must stay exactly `100` (400 `VALIDATION_ERROR`), the job position must be
  `ACTIVE`, and the period must not overlap another `ACTIVE` profile of the same job position (409
  `CONFLICT`).
- Same validation rules as create otherwise.

### `PATCH /evaluation-profiles/{id}/status`

Body `{ "status": "ACTIVE" }`. See the Status section. `data` is empty; 200 `ApiResponse` with a message.

### `DELETE /evaluation-profiles/{id}`

Soft delete: sets the status to `ARCHIVED` and keeps the rows. Deleting an `ARCHIVED` profile is a no-op and
returns 200. 404 `NOT_FOUND` for an unknown id.

### Item endpoints

Manage one node (group or KPI leaf + weight) at a time. The node is identified by its `itemId` in the route. Permissions are the same as the profile endpoints: `GET` needs `kpi:read`, the others
`kpi:manage`, scope `All`.

| Verb and route | Body | Success |
| --- | --- | --- |
| `GET /evaluation-profiles/{id}/items` | none | 200, `data`: `EvaluationProfileItem[]`, the root nodes with their `children`, ordered by `displayOrder`; 404 `NOT_FOUND` for an unknown profile |
| `POST /evaluation-profiles/{id}/items` | `{ "parentItemId", "name" or "kpiDefinitionId", "weightPercent", "displayOrder" }`; a null `parentItemId` adds a root group, otherwise the parent must be a group of the profile | 201 with a `Location` header pointing at `GET /evaluation-profiles/{id}/items`; `data`: `EvaluationProfileItem` |
| `PUT /evaluation-profiles/{id}/items/{itemId}` | `{ "name"?, "weightPercent", "displayOrder" }` (`name` only for a group; the place in the tree never changes) | 200, `data`: `EvaluationProfileItem` |
| `DELETE /evaluation-profiles/{id}/items/{itemId}` | none | 200, `data` is empty; the node and everything beneath it are removed |

Rules for `POST`, `PUT` and `DELETE`:

- Only a `DRAFT` or `INACTIVE` profile can be changed item by item. An `ACTIVE` profile is 409 `CONFLICT`: it must
  keep the total weight at exactly `100`, so change its whole item list with `PUT /evaluation-profiles/{id}`.
  An `ARCHIVED` profile is 409 `CONFLICT`.
- A profile that is **in use** is 409 `EVALUATION_PROFILE_IN_USE`; create a new version instead.
- The total weight is not enforced by these endpoints (it can be below or above `100` while editing). It is
  enforced when the profile is activated, see the Status section.
- `POST` with a KPI already in the profile is 409 `CONFLICT`. An unknown KPI or parent node is 404 `NOT_FOUND`. A parent
  that is a KPI leaf, a root KPI leaf, or a tree deeper than 3 levels is 400 `VALIDATION_ERROR`. `PUT` or `DELETE`
  for a node that is not in the profile is 404 `NOT_FOUND`.
- Invalid `weightPercent` or `displayOrder` is 400 `VALIDATION_ERROR` (same rules as create).
- Every successful change updates the profile's `updatedAt`.
- Risk: two concurrent requests can leave the total weight temporarily different from `100` on a `DRAFT` or
  `INACTIVE` profile; this is safe because activation checks the total again.

## Errors

| HTTP | `errorCode` | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | invalid or missing fields, bad enum value, weights not totalling 100 on activation |
| 401 / 403 | `UNAUTHORIZED` / `FORBIDDEN` | not signed in / missing permission |
| 404 | `NOT_FOUND` | unknown profile, job position, KPI or tree node |
| 409 | `CONFLICT` | duplicate `(code, version)` or `(jobPosition, effectiveFrom)`, overlapping active period, illegal transition, archived profile, job position mismatch for a new version, concurrent write |
| 409 | `EVALUATION_PROFILE_IN_USE` | structural change (including any item endpoint write) to a profile already used by a performance evaluation |

## Not available

No endpoint copies a profile into a new version in one call (create a new version with `POST` and the same
`code`), and nothing here recalculates existing performance evaluations.
