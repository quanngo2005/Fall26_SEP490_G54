# Job Position API Contract (SCR-ORG-02 / SCR-ORG-02-D1)

Status: implemented. Typed client: `frontend/src/app/core/job-position.service.ts`.
Routes use the backend origin, no API prefix. All responses use `ApiResponse<T>`; paging uses `PagedResult<T>`
(`items`, `totalCount`, `page`, `pageSize`) as defined in `API_CONVENTIONS.md`.

The endpoints are read-only. Every figure and label is read from the database; nothing is computed from constants,
renamed or synthesised. A value with no data is `null` (or an empty list / `0` for counts) and the client renders `null`
as N/A.

## Authorization

Both endpoints use the policy `OrganizationViewer` (`[Authorize(Policy = OrganizationAccess.Policy)]`): the caller's JWT
`role` claim must contain at least one of

| Role code | Meaning |
| --- | --- |
| `SYSTEM_ADMIN`, `ADMIN` | system administrator |
| `BRANCH_MANAGER` | branch manager |
| `DEPT_MANAGER`, `DEPARTMENT_MANAGER`, `MANAGER` | department manager (`MANAGER` is the seeded role code) |

Codes are compared after trimming, upper-casing and turning spaces/hyphens into underscores (`branch-manager` matches).
Every other role (employee, HR, QA, verifier, ...) receives `403 FORBIDDEN`, even if it holds a data permission. A missing,
expired or invalid token receives `401 UNAUTHORIZED`. The same policy guards `/departments` (SCR-ORG-01); the frontend
route guard `organizationGuard` applies it to `/departments`, `/job-positions` and `/department-performance` and sends
other users to `/home`. Job positions are global reference data, so there is no per-department data scope.

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

## Errors

| Status | When |
| --- | --- |
| 400 `VALIDATION_ERROR` | unknown `status` filter |
| 401 `UNAUTHORIZED` | missing, expired or invalid access token |
| 403 `FORBIDDEN` | the caller has none of the allowed roles |
| 404 `NOT_FOUND` | unknown id (detail) |
