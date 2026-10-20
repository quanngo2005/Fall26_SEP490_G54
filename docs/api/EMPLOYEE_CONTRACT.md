# Employee detail

Status: backend implemented (read endpoints used by the frontend). Typed client:
`frontend/src/app/core/employee.service.ts`. Routes use the backend origin, no API prefix; all
responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`). Write endpoints
(`POST/PUT/PATCH/DELETE /employees...`) and the option lookups (`/employees/departments`,
`/employees/job-positions`, `/employees/managers`) exist on the backend but are **not used** by the
employee detail page and are not part of this contract revision.

## Authorization

`[Authorize]` only (any authenticated account). There is no department-scope check on these
endpoints; the page additionally reads `/departments/{id}/members`, which is scoped (see
`DEPARTMENT_CONTRACT.md`) and returns 403 outside the caller's scope.

## Endpoints

### `GET /employees`

Query: `keyword` (code/name/email/phone contains), `departmentId`, `jobPositionId`, `managerId`
(GUIDs), `status`, `page` (default 1), `pageSize` (default 20, clamped 1..100).
`data`: `PagedResult<EmployeeDetail>`.

### `GET /employees/{id}`

`id` is the **employee** id (GUID), not the account id. 404 `NOT_FOUND` for an unknown id.
`data`: `EmployeeDetail`.

```json
{
  "id": "5eed0000-0000-0000-0007-000000000004",
  "employeeCode": "NV004",
  "fullName": "Lê Minh Châu",
  "email": "chau@example.test",
  "phone": null,
  "departmentId": "5eed0000-0000-0000-0001-000000000004",
  "departmentCode": "CN-HN-KD1",
  "departmentName": "Nhóm Kinh doanh 1",
  "jobPositionId": "5eed0000-0000-0000-0006-000000000001",
  "jobPositionCode": "CV-KD",
  "jobPositionTitle": "Chuyên viên Kinh doanh",
  "managerId": null,
  "managerCode": null,
  "managerName": null,
  "status": "ACTIVE",
  "directReportsCount": 0,
  "lastSyncAt": "2026-10-01T00:00:00+00:00",
  "updatedAt": "2026-10-01T00:00:00+00:00"
}
```

`phone`, `managerId`, `managerCode`, `managerName` are nullable; the client renders `null` as N/A.

## Enum wire format

`status` is an UPPER_SNAKE string: `ACTIVE | INACTIVE | ON_LEAVE | TERMINATED`.

Responses are produced with `EnumWireFormat.ToWire()`. The `status` query filter, the optional `status`
of `POST /employees` (defaults to `ACTIVE`), the required `status` of `PUT /employees/{id}` and the
`PATCH /employees/{id}/status` body are parsed with `EnumWireFormat.ParseWire()` (case-insensitive). An
unknown value, a C# member name such as `OnLeave`, or an ordinal such as `2` is 400 `VALIDATION_ERROR`;
ordinals are no longer accepted. The client reads the string as-is, with no adapter.

## Identifier hand-off

Department member rows link to `/employee-profile?id={employeeId}`, which redirects to
`/employee-detail?id={employeeId}`. The page accepts the id from the path (`/employee-detail/{id}`)
or the `id` query parameter. Without an id it shows an empty state; it never loads an arbitrary
employee.

## Related data

Open work count and performance score on the page come from
`GET /departments/{departmentId}/members?keyword={employeeCode}` (see `DEPARTMENT_CONTRACT.md`).
`null` is shown as N/A. A 403/404 from that call also renders N/A; it is not an error of this page.

Not available from any endpoint (not shown): per-employee work list, per-employee KPI list,
performance history.
