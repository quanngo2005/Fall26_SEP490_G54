# Employee detail

Status: backend implemented (read endpoints used by the frontend). Typed client:
`frontend/src/app/core/employee.service.ts`. Routes use the backend origin, no API prefix; all
responses use `ApiResponse<T>` (see `API_CONVENTIONS.md`). Write endpoints
(`POST/PUT/PATCH/DELETE /employees...`) exist but are not used by the read-only list or detail page.
The list at `/employees` also uses the department and job-position option lookups below.

## Authorization

`GET /employees`, `GET /employees/{id}` and `GET /employees/by-code/{code}` require
`employee:read` at `Own` scope or higher. The service restricts rows to self (`Own`), the caller's
department subtree (`Dept`), the nearest branch subtree (`Branch`), or everyone (`All`/full access).
Missing permission returns 403; an unknown or out-of-scope employee detail returns 404.
`GET /employees/me` and the option lookups require authentication. The lookups return all active
departments/positions, not a scope-filtered employee roster.
The detail page additionally reads scoped `/departments/{id}/members` (see `DEPARTMENT_CONTRACT.md`).

## Endpoints

### `GET /employees`

Query: `keyword` (code/name/email/phone contains), `departmentId`, `jobPositionId`, `managerId`
(GUIDs), `status`, `page` (default 1),
`pageSize` (default 20, clamped 1..100).
`data`: `PagedResult<EmployeeDetail>`.

Rows are ordered by employee code. The list omits `pageSize` on its initial request and uses the
server-returned page size for subsequent requests; it derives page counts and display ranges from
the response, not local sample counts. Search and filters are applied on the server.

### `GET /employees/departments`

`data`: an array of `{ id, code, name, levelType }`. IDs are GUID strings;
`levelType` follows the backend's current numeric `DepartmentLevelType` serialization
(`Branch`, `Department`, `Team` in declaration order). The list does not use this field
to decide access or filtering. Active departments only, ordered by code.

### `GET /employees/job-positions`

`data`: an array of `{ id, code, title }`. IDs are GUID strings.
Active positions only, ordered by code. Both lookup filters send GUID IDs, not display codes.

### List limitations

The employee DTO includes `username` from the related account, or null when there is no account.
The list displays the username verbatim and a localized "No account" label for null.
The DTO has no account state, joining date, import batch, global synchronization
timestamp or locked evaluation tier/history. The list displays N/A for global synchronization,
does not infer import state from employee status. There is no account filter or username lookup endpoint.
`MISSING_IN_FILE` is not an employee status. No write or automatic fallback to demo data is performed.
Drawer integration is a separate task.
The list preserves its two pagination buttons, binding them to adjacent server-derived page numbers
so later pages remain reachable without changing the layout. Search sends a request on input changes,
with the previous request cancelled; no timer or demo fallback is used.

### `GET /employees/me`

The employee behind the signed-in account (the UI route `/employee-detail/me`). `data`: `EmployeeDetail`.
404 `NOT_FOUND` when the account does not exist; 403 `FORBIDDEN` when the account is locked or the
employee is not active.

### `GET /employees/by-code/{code}`

`code` is the unique **employee code** (for example `NV004`, matched exactly after trimming; URL-encode
it). 404 `NOT_FOUND` for an unknown code. `data`: `EmployeeDetail`. This is what UI addresses use, so a
GUID never appears in a URL.

### `GET /employees/{id}`

`id` is the **employee** id (GUID), not the account id. 404 `NOT_FOUND` for an unknown id.
`data`: `EmployeeDetail`. Kept for clients that only know the id; the UI uses it solely to resolve a
legacy link (see "Identifier hand-off").

```json
{
  "id": "5eed0000-0000-0000-0007-000000000004",
  "employeeCode": "NV004",
  "fullName": "Lê Minh Châu",
  "username": "chau",
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
`username` is nullable and means no linked account when null; it does not describe whether an account is locked.

## Enum API format

`status` is an UPPER_SNAKE string: `ACTIVE | INACTIVE | ON_LEAVE | TERMINATED`.

Responses are produced with `EnumApiFormat.ToEnumString()`. The `status` query filter, the optional `status`
of `POST /employees` (defaults to `ACTIVE`), the required `status` of `PUT /employees/{id}` and the
`PATCH /employees/{id}/status` body are parsed with `EnumApiFormat.ParseEnum()` (case-insensitive). An
unknown value, a C# member name such as `OnLeave`, or an ordinal such as `2` is 400 `VALIDATION_ERROR`;
ordinals are no longer accepted. The client reads the string as-is, with no adapter.

## Phone number rule

Wherever a phone number is written (`POST /employees`, `PUT /employees/{id}`, `PUT /me/profile`) it must be
**exactly 10 digits and start with 0** (`^0[0-9]{9}$`, for example `0901234567`); blank means "no number".
Anything else is 400 `VALIDATION_ERROR`. `PUT /employees/{id}` applies the rule **only when the phone
changes**, so a record whose stored number predates the rule can still have its other fields edited.

## Identifier hand-off

URLs identify an employee by **employee code**, never by GUID:

| Address | Meaning |
|---|---|
| `/employee-detail` | Picker (no employee loaded). |
| `/employee-detail/me` | The signed-in user's own record (`GET /employees/me`). |
| `/employee-detail/{code}` | That employee (`GET /employees/by-code/{code}`). |

Department member rows, the picker rows and the manager link all use `/employee-detail/{employeeCode}`;
links to a department use `/departments?department={departmentCode}`.

Links from before this change still open: `/employee-detail?id={guid}`, `/employee-detail/{guid}` and
`/departments?departmentId={guid}` are resolved, and the address bar is replaced with the code form
(`/employee-detail/{code}`).

Replacing the GUID in the address is presentation only. The same employee-read permission and scope
checks apply (see "Authorization").

## Own record: change phone and password

When the loaded employee is the signed-in user (the `me` key, or the same email as the session) the hero card
shows **Change phone number** and **Change password**; each opens a popup. The checks are a convenience only:
the endpoints behind them (`PUT /me/profile`, `POST /me/password`, see `MY_PROFILE_CONTRACT.md`) can only ever
act on the signed-in account.

## Related data

Open work count and performance score on the page come from
`GET /departments/{departmentId}/members?keyword={employeeCode}` (see `DEPARTMENT_CONTRACT.md`).
`null` is shown as N/A. A 403/404 from that call also renders N/A; it is not an error of this page.

Not available from any endpoint (not shown): per-employee work list, per-employee KPI list,
performance history.
