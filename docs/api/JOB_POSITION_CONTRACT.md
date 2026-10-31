# Job Position API Contract (SCR-ORG-02 / SCR-ORG-02-D1)

Status: implemented. Typed client: `frontend/src/app/core/job-position.service.ts`.
Routes use the backend origin, no API prefix. All responses use `ApiResponse<T>`; paging uses `PagedResult<T>`
(`items`, `totalCount`, `page`, `pageSize`) as defined in `API_CONVENTIONS.md`.

The `GET` endpoints are read-only. Every figure and label is read from the database; nothing is computed from constants,
renamed or synthesised. A value with no data is `null` (or an empty list / `0` for counts) and the client renders `null`
as N/A.

## Authorization

All endpoints require the permission `organization:read` with scope `Dept` or higher
(`[HasPermission("organization", "read", "Dept")]`; permission code `ORGANIZATION_READ`). It is read from
`role_permission`, so access is configuration, not code. The migration `SeedOrganizationReadAccess` creates the permission,
the roles `SYSTEM_ADMIN`, `BRANCH_MANAGER` and `DEPT_MANAGER`, and grants it to exactly:

| Role | Scope | Meaning |
| --- | --- | --- |
| `SYSTEM_ADMIN`, `ADMIN` | `All` | system administrator |
| `BRANCH_MANAGER` | `Branch` | branch manager |
| `DEPT_MANAGER`, `MANAGER` | `Dept` | department manager (`MANAGER` is the seeded manager role) |

Every other role (employee, verifier, HR, QA, ...) has no `organization:read` row and receives `403 FORBIDDEN`; holding
`evaluation:read` no longer helps. The wildcard `FULL_ACCESS` permission (development admin) also passes. A missing, expired
or invalid token receives `401 UNAUTHORIZED`. An administrator can grant the permission to another role by adding a
`role_permission` row (or through the permission matrix, SCR-ADM-03); no code change is needed. The same permission guards
`/departments` (SCR-ORG-01, see `DEPARTMENT_CONTRACT.md`); the frontend route guard `organizationGuard` checks it (and
`FULL_ACCESS`) for `/departments`, `/job-positions` and `/department-performance` and sends other users to `/home`.
Job positions are global reference data, so there is no per-department data scope.
## Endpoints

### `GET /job-positions`

Query:

- `keyword` (optional): case-insensitive contains on `code` or `title`; blank is ignored.
- `status` (optional): `ACTIVE` | `INACTIVE` | `ARCHIVED`, case-insensitive. An unknown value is `400 VALIDATION_ERROR`.
- `page` (default 1, values below 1 become 1), `pageSize` (default 50, clamped 1..100).

Ordered by `code`. The local development bootstrap position `DEV_ADMIN` is never listed.

`data`: `PagedResult<JobPositionSummary>`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "5eed0000-0000-0000-0006-000000000001",
        "code": "SALES",
        "title": "Chuyên viên Kinh doanh",
        "description": "Bán hàng, phát triển khách hàng",
        "employeeCount": 7,
        "evaluationProfileId": "5eed0000-0000-0000-0002-000000000001",
        "evaluationProfileName": "Hồ sơ đánh giá Chuyên viên Kinh doanh",
        "workTypeCount": 3,
        "kpiItems": ["Điểm hài lòng", "Khách hàng mới"],
        "lastSyncAt": "2026-10-24T08:15:00+07:00",
        "status": "ACTIVE"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 50
  }
}
```

Field rules:

- `code`, `title`, `description` are the stored values (`description` is `null` when empty). `code` is never rewritten.
- `employeeCount`: employees of that position with status `ACTIVE`; `0` when there are none.
- `evaluationProfileId` / `evaluationProfileName`: the evaluation profile with status `ACTIVE` whose
  `effectiveFrom <= today <= effectiveTo` (open-ended when `effectiveTo` is null); on a tie the latest `effectiveFrom`,
  then the highest `version`. `null` when there is none. The name is the stored profile name, unchanged.
- `workTypeCount`: work types linked to the position with status `ACTIVE`.
- `kpiItems`: distinct names of the KPIs mapped to those work types whose mapping is effective today
  (`effectiveFrom <= today <= effectiveTo`), ordered by name. Empty list when there are none.
- `status`: `ACTIVE | INACTIVE | ARCHIVED`. "Today" is the Vietnam business day (UTC+7).

### `GET /job-positions/{id}`

`id` is a GUID. `404 NOT_FOUND` for an unknown id and for the development bootstrap position.

`data`: `JobPositionDetail`.

```json
{
  "success": true,
  "status": 200,
  "data": {
    "id": "5eed0000-0000-0000-0006-000000000002",
    "code": "TELLER",
    "title": "Giao dịch viên",
    "description": "Giao dịch tại quầy",
    "employeeCount": 3,
    "lastSyncAt": "2026-10-24T08:15:00+07:00",
    "status": "ACTIVE",
    "evaluationProfile": {
      "id": "5eed0000-0000-0000-0002-000000000002",
      "code": "EP-TELLER",
      "name": "Hồ sơ đánh giá Giao dịch viên",
      "description": null,
      "version": 1,
      "effectiveFrom": "2026-01-01",
      "status": "ACTIVE",
      "totalWeightPercent": 100,
      "items": [
        { "id": "5eed0000-0000-0000-0004-000000000004", "groupName": "Kinh doanh", "groupCode": "KD", "weightPercent": 20, "displayOrder": 1 },
        { "id": "5eed0000-0000-0000-0004-000000000005", "groupName": "Chất lượng dịch vụ", "groupCode": "CLDV", "weightPercent": 50, "displayOrder": 2 }
      ]
    },
    "workTypes": [
      {
        "id": "5eed0000-0000-0000-000c-000000000001",
        "code": "MO_TAI_KHOAN",
        "name": "Mở tài khoản khách hàng",
        "description": null,
        "kpiCount": 2,
        "kpis": [
          { "id": "5eed0000-0000-0000-0005-000000000002", "code": "KH_MOI", "name": "Khách hàng mới", "isRequired": true, "unit": "Tài khoản" }
        ]
      }
    ]
  }
}
```

Field rules:

- `evaluationProfile` is chosen with the same rule as the list and is `null` when there is none.
  `items` are ordered by `displayOrder`; `weightPercent` and `totalWeightPercent` are decimals (never truncated) and the
  total is the exact sum of the items.
- `workTypes`: `ACTIVE` work types linked to the position, ordered by `code`. `kpis` are the mappings effective today,
  ordered by KPI `code`; `kpiCount` equals `kpis.length`.
- `employeeCount` follows the list rule.

### `GET /job-positions/{id}/work-types`

Lists the work types assigned to one job position. Same permission as the other reads (`organization:read`, scope `Dept` or
higher); no `job-position:manage` is needed.

- `id` is a GUID. `404 NOT_FOUND` for an unknown id and for the development bootstrap position.
- `data`: an array of `JobPositionWorkType`, the same shape and rules as `workTypes` in `GET /job-positions/{id}`: only `ACTIVE`
  work types, ordered by `code`, with `kpis` the mappings effective today (Vietnam business day) ordered by KPI `code`, and
  `kpiCount` equal to `kpis.length`. A position with no work type returns an empty array (`200`), not `404`.
- Not paged: a position has a small, bounded set of work types.

```json
{
  "success": true,
  "status": 200,
  "data": [
    {
      "id": "5eed0000-0000-0000-000c-000000000001",
      "code": "MO_TAI_KHOAN",
      "name": "Mở tài khoản khách hàng",
      "description": null,
      "kpiCount": 1,
      "kpis": [
        { "id": "5eed0000-0000-0000-0005-000000000002", "code": "KH_MOI", "name": "Khách hàng mới", "isRequired": true, "unit": "Tài khoản" }
      ]
    }
  ]
}
```

### `POST /job-positions/{id}/work-types`

Assigns a work type to a job position. Requires `job-position:manage` with scope `Dept` or higher (in addition to the
controller-level `organization:read`). Migration `SeedJobPositionManageAccess` grants it to managers only: `BRANCH_MANAGER`
(`Branch`) and `DEPT_MANAGER` / `MANAGER` (`Dept`). `SYSTEM_ADMIN` administers the system, not job positions, so it is not granted
and receives `403 FORBIDDEN`. Job positions are global reference data, so the scope is a minimum level, not a data filter.

Body:

```json
{ "workTypeId": "5eed0000-0000-0000-000c-000000000001" }
```

- `id` is a GUID; the development bootstrap position is treated as unknown.
- `workTypeId` must be a GUID of an existing work type with status `ACTIVE`.
- Success is `201 Created` with a `Location` header pointing to `GET /job-positions/{id}`; `data` is the updated
  `JobPositionDetail` (same shape as the detail endpoint).
- A work type is linked to a position at most once (unique `job_position_id` + `work_type_id`); assigning it again is `409`.

### `DELETE /job-positions/{id}/work-types/{workTypeId}`

Removes a work type from a job position. Same permission as the assign endpoint: `job-position:manage` with scope `Dept` or
higher (`BRANCH_MANAGER`, `DEPT_MANAGER` / `MANAGER`); `SYSTEM_ADMIN` receives `403 FORBIDDEN`.

- `id` and `workTypeId` are GUIDs. The development bootstrap position is treated as unknown.
- Only the link between the position and the work type is deleted; the work type itself and its link to other positions are untouched.
- A link to a work type that is no longer `ACTIVE` can still be removed.
- Success is `200 OK` with `data` = the updated `JobPositionDetail` (same shape as the detail endpoint).
- Removing a work type that is not assigned to the position is `404 NOT_FOUND` (it is not silently reported as success).
## Errors

| Status | When |
| --- | --- |
| 400 `VALIDATION_ERROR` | unknown `status` filter; `workTypeId` empty, unknown or not `ACTIVE` (assign) |
| 401 `UNAUTHORIZED` | missing, expired or invalid access token |
| 403 `FORBIDDEN` | the caller has none of the allowed roles |
| 404 `NOT_FOUND` | unknown id (detail, work types, assign, remove); work type not assigned to the position (remove) |
| 409 `CONFLICT` | the work type is already assigned to the position (assign) |
