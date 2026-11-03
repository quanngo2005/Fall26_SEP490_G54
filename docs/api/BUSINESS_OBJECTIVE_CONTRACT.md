# Business objectives

Status: backend implemented (no frontend client yet). Routes use the backend origin, no API prefix; all
responses use `ApiResponse<T>`. `GET /business-objectives` also feeds the objective dropdown of the campaign plan form
(`CAMPAIGN_CONTRACT.md`); it used to be an action of `CampaignController` and is now served by `BusinessObjectivesController`.

## Authorization

| Operation | Policy |
| --- | --- |
| `GET` endpoints | permission `objective:read`, scope `Dept` or wider |
| `POST`, `PUT`, `DELETE` | permission `objective:manage`, scope `All` |

Seeded grants (migration `AddObjectiveWeightAndKpiRequestApproval`): `objective:read` to BRANCH_MANAGER (`Branch`),
DEPT_MANAGER and MANAGER (`Dept`); `objective:manage` to BRANCH_MANAGER. ADMIN passes every permission policy.
401 when unauthenticated, 403 when the permission is missing.

Read scope is resolved in the service from the caller's effective permission:

- `Branch` or wider: every objective (an objective belongs to the branch; it has no department column).
- `Dept`: only objectives that have at least one target (`target_assignment`) on a department of the caller's
  department subtree, or on an employee of that subtree. The list leaves the others out; the detail of any other
  objective is 403. An objective that has no target yet is therefore not visible to a Department Manager, which
  includes the campaign dropdown.

## Endpoints

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| GET | `/business-objectives` | query `keyword`, `status` | 200 `BusinessObjective[]` |
| GET | `/business-objectives/{id}` | — | 200 `BusinessObjectiveDetail` (403, 404) |
| POST | `/business-objectives` | `BusinessObjectiveSave` | 201 `BusinessObjective`, `Location` header |
| PUT | `/business-objectives/{id}` | `BusinessObjectiveSave` (full replacement) | 200 `BusinessObjective` (400, 404, 409) |
| DELETE | `/business-objectives/{id}` | — | 200 (404, 409 `BUSINESS_OBJECTIVE_IN_USE`) |

The list is not paged and is ordered by `code`. `keyword` matches code or name. Without `status` the list leaves out
`CANCELLED` objectives (the campaign dropdown rule). `status` is UPPER_SNAKE (`DRAFT | ACTIVE | CLOSED | CANCELLED`),
parsed with `EnumApiFormat.ParseEnum()`; an unknown value or an ordinal is 400 `VALIDATION_ERROR`.

`BusinessObjective`: `id`, `parentObjectiveId` (nullable), `code`, `name`, `description` (nullable), `startDate`,
`endDate` (`YYYY-MM-DD`), `priorityWeight` (nullable), `status`, `updatedAt`.

`BusinessObjectiveDetail`: `objective` (a `BusinessObjective`), `usage` (`kpiCount`, `targetCount`, `campaignCount`,
`childObjectiveCount`; `targetCount` counts only the targets the caller may see) and `kpis[]`
(`kpiDefinitionId`, `kpiCode`, `kpiName`, `unit`, the KPIs linked through `/business-objectives/{id}/kpis`).

`BusinessObjectiveSave`: `code` (required), `name` (required), `description` (nullable), `startDate`, `endDate`
(required), `parentObjectiveId` (nullable), `priorityWeight` (nullable). The status is not client-supplied.

## Rules

- `code` and `name` are trimmed, required and limited to the length of their database columns; the message states
  the real limit. A duplicate `code` is 409 `CONFLICT` (also when two requests race: the unique index is caught).
- `endDate` must not be before `startDate` (400).
- `parentObjectiveId` must exist (404) and must not be the objective itself or one of its descendants (400).
- A new objective is created `ACTIVE`. There is no endpoint that changes the status (no use case asks for one).
- `PUT` is allowed only while the objective is `DRAFT` or `ACTIVE`; a `CLOSED` or `CANCELLED` objective is 409.
- `DELETE` removes the objective only when nothing references it: no linked KPI, target, campaign or child objective.
  Otherwise 409 `BUSINESS_OBJECTIVE_IN_USE`; a reference added between the check and the delete is caught as the same 409.
- `priorityWeight`: nullable; when given it must be `>= 0` (database `CHECK`) and fit the column (`numeric(8,4)`).
  **The permitted range and whether the weights of the objectives of one period must add up to a total are awaiting
  confirmation**; nothing else is validated and no total is enforced.

## Not covered

- Changes of an objective are written to `audit_logs` by the database change interceptor; there is no separate endpoint.
- Linking KPIs to an objective stays in `KpiMappingController` (`/business-objectives/{objectiveId}/kpis`), unchanged.
- Updates do not check for concurrent edits (last write wins).
