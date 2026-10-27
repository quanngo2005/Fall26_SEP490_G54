# Role Permission Matrix Contract (SCR-ADM-03-M2)

Status: **proposed** — consumed by `frontend/src/app/core/role-permission.service.ts`; backend not implemented yet.

All endpoints require `[HasPermission("role", "update", "System")]` (SYSTEM_ADMIN) and use the standard `ApiResponse<T>` envelope (see `API_CONVENTIONS.md`).

| Method | Endpoint | Description |
|---|---|---|
| GET | `/roles` | List roles for the selector |
| GET | `/roles/{roleId}/permission-matrix` | Matrix of resource × action → scope for a role |
| PUT | `/roles/{roleId}/permission-matrix` | Apply changed cells (optimistic concurrency) |

## Models

```jsonc
// RoleOption
{ "id": "uuid", "code": "DM", "name": "Trưởng phòng", "isSystem": true }

// RolePermissionMatrix
{
  "roleId": "uuid",
  "version": 3,
  "modules": [
    {
      "module": "kpi", "name": "KPI & Mục tiêu",
      "resources": [
        {
          "resource": "target", "name": "Mục tiêu nhân viên",
          "cells": [
            { "action": "view", "applicable": true, "scope": "Dept", "locked": false, "allowedScopes": null },
            { "action": "create", "applicable": true, "scope": null, "locked": true, "lockReason": "Chỉ System Admin tạo KPI" },
            { "action": "approve", "applicable": false, "scope": null, "locked": false }
          ]
        }
      ]
    }
  ]
}
```

- `action`: `view | create | update | approve | import | export`. Missing cell = not applicable.
- `scope`: `Own | Dept | Branch | All | null` (null = not granted), matching `PermissionScopes`.
- `locked`: business-fixed cell; server must reject changes to it.
- `allowedScopes`: optional subset the admin may pick; null = all.

## PUT body

```json
{ "version": 3, "changes": [{ "resource": "target", "action": "view", "scope": "Branch" }] }
```

Only changed cells are sent. `scope: null` revokes. Returns the updated `RolePermissionMatrix` with a new `version`.

## Errors

| Status | errorCode | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Unknown resource/action, non-applicable cell, scope outside `allowedScopes` |
| 403 | `FORBIDDEN` | Caller not SYSTEM_ADMIN, or a locked cell was changed |
| 404 | `ROLE_NOT_FOUND` | Unknown `roleId` |
| 409 | `VERSION_CONFLICT` | `version` is stale; the FE asks the user to reload |

## Role list (SCR-ADM-03)

Status: **proposed** — consumed by `features/role-permissions/role-list.component.ts`.

`GET /roles` returns `RoleSummary[]`, a superset of `RoleOption` (the matrix selector ignores the extra fields):

```jsonc
{ "id": "uuid", "code": "TELLER", "name": "Giao dịch viên", "isSystem": true,
  "description": null, "accountCount": 14, "status": "Active" } // status: Active | Inactive
```

`GET /roles/{roleId}/accounts` returns the accounts holding the role:

```jsonc
[{ "id": "uuid", "employeeCode": "NV001", "fullName": "Nguyễn Văn A", "departmentName": "Phòng GD 1" }]
```

The drawer's Permissions tab reuses `GET /roles/{roleId}/permission-matrix` and shows granted cells only. The History tab waits for a role audit endpoint.
