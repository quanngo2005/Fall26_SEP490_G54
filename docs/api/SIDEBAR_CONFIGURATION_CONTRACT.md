# Role sidebar configuration

Status: implemented by this feature. Routes use the backend origin, no API prefix.
All responses use `ApiResponse<T>`; no pagination. All endpoints require an active,
unlocked authenticated account. Configuration changes affect navigation visibility
and order only, never API permissions. Accounts with multiple roles receive an
ordered union of their role menus, filtered by their permission claims.

## Endpoints

- `GET /sidebar-configurations/current`: configurations for the caller's recognized
  workspace roles. Unknown roles produce `data: []`.
- `GET /sidebar-configurations`: admin only; configurations for all workspace roles.
- `PUT /sidebar-configurations/{role}`: admin only; replace ordered menu identifiers.
  `role` is one of `employee`, `departmentManager`, `branchManager`, `hr`, `qa`, `admin`.
  Role aliases in authentication map to these workspace roles.

Admin authorization is checked against current database role assignments in BLL,
not against menu visibility. `ADMIN` and `SYSTEM_ADMIN` are administrator aliases.

DTO: `{ role: string, itemIds: string[], version: integer, routes: { [menuId]: string } }`.
Request: `{ itemIds: string[], version: integer, routes?: { [menuId]: string } }`.
`routes` contains per-role route overrides for selected items only. Omitted keys
use the catalog route; an empty string explicitly disables navigation. Omitting
the whole field preserves existing overrides (backward-compatible older clients).
Routes must be an internal application route from the supported allowlist (backend:
`SidebarCatalog.Routes`; frontend: `NavigationService.routeOptions`), or empty. Both
lists must stay identical; this document intentionally does not duplicate them. No
external URLs, query strings, hashes or arbitrary routes. `/sidebar-settings` is admin-only.
Group `organization` has no route override; it expands children. Admin's required
`systemConfiguration` route cannot be changed away from `/sidebar-settings`.
Route changes do not change the menu's required permission or destination API authorization.
Identifiers match `MenuKey` in `navigation.service.ts` and the backend catalog.
Top-level order follows the array; children use the same array for their relative
order. Children require their parent (`organization`). No duplicates or unknown
identifiers. `overview` is required for every role; `systemConfiguration` is also
required for admin so the editor stays reachable. Empty configuration is invalid.
Items without an effective route remain disabled. Icons remain a fixed catalog.
The editor supports pointer/touch drag-and-drop and keyboard ArrowUp/ArrowDown
reordering, and persists the resulting identifier array with route overrides.

Configurations without a saved row return application defaults with `version: 0`.
The first save creates the row with version 1. Each subsequent save increments it.
The request version must match the stored version; concurrent changes return 409.

Example update:

```json
{ "itemIds": ["overview", "myWork", "myProfile"], "version": 0 }
```

Example successful response (200):

```json
{
  "success": true,
  "status": 200,
  "data": { "role": "employee", "itemIds": ["overview", "myWork", "myProfile"], "version": 1 }
}
```

Errors: 400 `VALIDATION_ERROR` (invalid role/menu/required item/version), 401
for absent/invalid authentication, 403 `FORBIDDEN` (inactive/locked/non-admin),
409 `CONFLICT` (stale version or concurrent first save), 500 for unexpected errors.
Framework authentication errors may have no JSON body. Client displays localized
errors and never substitutes defaults or mock success for failed requests.

Saved configurations are loaded on entry/session changes and editor saves refresh
the current sidebar. Other open sessions receive updates on their next navigation
reload; this feature does not provide push notifications.
