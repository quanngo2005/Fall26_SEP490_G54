# User Account Contract (SCR-ADM-01 / SCR-ADM-02)

Status: **implemented**. All endpoints are restricted to the system role-management
permission (`[HasPermission("role", "update", "System")]`) and return the standard
`ApiResponse<T>` envelope.

## Endpoints

### 1. List accounts

`GET /user-accounts`

Query parameters:

- `keyword` (optional): username, employee code, or full-name search.
- `roleId` and `departmentId` (optional UUID filters).
- `status` (optional): `ACTIVE`, `LOCKED`, or `EMPLOYEE_TERMINATED`.
- `page` (default `1`) and `pageSize` (default `20`, maximum `100`).

Returns `ApiResponse<PagedResult<UserAccountSummary>>` (`items`, `totalCount`, `page`, `pageSize`).
Each item carries `departmentCode` and `departmentName`; the UI shows the name. There is no
`lastLoginAt` field (removed: the screen does not use it).

### 2. Summary counts

`GET /user-accounts/summary`

Returns `ApiResponse<UserAccountSummaryCounts>` (`activeCount`, `lockedCount`, `employeesWithoutAccountCount`).

### 3. Eligible employees for account creation

`GET /user-accounts/eligible-employees`

Returns `ApiResponse<EligibleEmployee[]>` listing non-terminated employees without an existing account,
including `suggestedRoleId` (mapped from `JobPosition.Code`) and `suggestedUsername`.

### 4. Account detail (Drawer SCR-ADM-02)

`GET /user-accounts/{id:guid}`

Returns `ApiResponse<UserAccountDetail>` including employee metadata, suggested role, creation info,
last password change timestamp, and merged audit/authentication history (`history`), or `404 NOT_FOUND`.

### 5. Create account (SCR-ADM-01-M1)

`POST /user-accounts`

Body: `{ employeeId, username, roleId, mustChangePassword }`.
Returns `201 Created` with `ApiResponse<CreateUserAccountResult>` (`account` + one-time `temporaryPassword`).
Conflict codes: `409 USERNAME_EXISTS`, `409 ACCOUNT_EXISTS`.

### 6. Update account role (SCR-ADM-02-M1)

`PUT /user-accounts/{id:guid}`

Body: `{ roleId }`.
Returns `200 OK` with updated `ApiResponse<UserAccountDetail>`.

### 7. Reset password

`POST /user-accounts/{id:guid}/reset-password` (no body)

Generates a new one-time temporary password, replaces the stored hash, clears the failed-login
counter, **sets `account.must_change_password`** and revokes every refresh token of the account (the
user is signed out everywhere).
Returns `200 OK` with `ApiResponse<{ temporaryPassword }>`; the password is shown to the admin once
and the system does not e-mail it. The lock state is left unchanged.

## Forced password change

`account.must_change_password` (boolean, default `false`; existing accounts are unaffected) is set when
an account is created with a one-time password (`POST /user-accounts` with `mustChangePassword: true`,
the employee import, employee creation) and by "reset password". It is exposed as
`user.mustChangePassword` in the `/auth/login`, `/auth/refresh` and `/me/password` responses.

While it is `true` the frontend shows a popup that cannot be dismissed (the only other action is
signing out) until the user changes the password through `POST /me/password`, which clears the flag and
returns a new session with `mustChangePassword: false`.

> Enforcement is in the frontend only: the API itself does not yet reject other calls from an account
> whose flag is set.

### 8. Lock / unlock

`POST /user-accounts/{id:guid}/lock` and `POST /user-accounts/{id:guid}/unlock` (no body)

- `lock`: sets `IsLocked`, leaves `LockedUntil` empty (open-ended, never expires by itself) and revokes
  every refresh token. An admin cannot lock the account they are signed in with: `400 CANNOT_LOCK_SELF`.
- `unlock`: clears `IsLocked`, `LockedUntil` and the failed-login counter.
- Both return `200 OK` with the updated `ApiResponse<UserAccountDetail>` and are idempotent.

Reset, lock and unlock return `400 VALIDATION_ERROR` for an employee who has left the company
(`status = EMPLOYEE_TERMINATED`, view-only) and `404 NOT_FOUND` for an unknown account.
