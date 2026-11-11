# User Account List Contract (SCR-ADM-01)

Status: **implemented**. The endpoint is restricted to the system role-management
permission and uses the standard `ApiResponse<T>` envelope.

## List accounts

`GET /user-accounts`

Query parameters:

- `keyword` (optional): username, employee code, or full-name search.
- `roleId` and `departmentId` (optional UUID filters).
- `status` (optional): `ACTIVE`, `LOCKED`, or `EMPLOYEE_TERMINATED`.
- `page` (default `1`) and `pageSize` (default `20`, maximum `100`).

The response is `ApiResponse<PagedResult<UserAccountSummary>>` and contains
`items`, `totalCount`, `page`, and `pageSize` inside `data`.
`lastLoginAt` is currently `null` because the auth audit schema does not retain
a dedicated successful-login timestamp. Account creation and mutation endpoints
remain out of scope until their request and authorization contract is approved.
