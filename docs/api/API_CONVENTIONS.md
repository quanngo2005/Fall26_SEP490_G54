# Shared Frontend / Backend API Contract

Read this document before implementing any frontend API client or backend endpoint.
Feature-specific contracts live in `docs/api/<FEATURE>_CONTRACT.md` and are the source
of truth for both sides. Read existing contracts and controllers before choosing routes.

## Contract-first workflow

1. Define or update the feature contract before writing the client/controller, even
   when only one side is being implemented. Record whether each endpoint is planned
   or implemented. Do not claim an endpoint exists without checking the backend.
2. Specify HTTP method, full route, authentication/permission, path/query parameters,
   request and response fields/types/nullability, enum API values, pagination,
   success/error status codes, and representative JSON examples.
3. Use the same contract for TypeScript API models and BLL request/response DTOs.
   Keep presentation-only models separate; use explicit adapters when needed.
4. When the second side is implemented, implement the agreed contract and verify the
   actual HTTP exchange. Any contract change must update documentation, both affected
   sides, fixtures, and relevant tests together. Do not silently invent aliases.

## Routes and methods

- Endpoints start at the host root, without API/version prefixes. Resource segments
  use lowercase kebab-case plural nouns: `/kpi-definitions`, `/submissions`, `/verifications`.
- Use explicit controller routes such as `[Route("kpi-definitions")]`;
  controller renames must not change public URLs.
- `GET /resources`: query/list; `GET /resources/{id}`: detail;
  `POST /resources`: create; `PUT /resources/{id}`: update according to the
  documented replacement semantics; `PATCH /resources/{id}/status`: status change
  when that resource supports it; `DELETE /resources/{id}`: documented deletion.
- Domain commands use documented subresources. Existing canonical verification routes:
  `GET /verifications/{submissionId}/dossier`,
  `POST /verifications/{submissionId}/decision`,
  `GET /verifications/{submissionId}/history`.
- Authentication uses `/auth/login`, `/auth/refresh`, `/auth/logout`.
  Prefix removal is a breaking route change; clients must use the new root paths.

## Wire format

- JSON properties are camelCase. DTOs are separate from EF entities.
- IDs use the backend's declared type; GUID IDs are UUID strings in the client and
  fixtures. Display codes such as `SUB-WRK-...` are not GUID identifiers.
- Timestamps use ISO 8601 with an explicit offset/UTC `Z`; date-only values use
  `YYYY-MM-DD`. Define decimal precision, nullability, required fields, and exact
  enum serialization per endpoint; do not infer them from UI labels.
- Reuse `G54.BLL.Common.Models.ApiResponse<T>` for normal JSON data responses.
  Frontend types must match the existing serializer's optional/null fields:
  `success`, `data`, `message`, `errorCode`, `errors`, `traceId`, `status`, `title`,
  and `pagination` when present. Do not invent a second envelope.
- Standard paged data uses `PagedResult<T>`:
  `{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }` inside `data`.
  Record pagination defaults, limits, filters and sorting in the feature contract.
  Existing endpoints using top-level pagination metadata must be documented explicitly.
- The HTTP status is authoritative. Reads/updates normally return 200; creates return
  201 with a Location header; documented no-content operations return 204 with no
  JSON body. Verify any envelope `status` agrees with the HTTP response; existing
  `ApiResponse.Ok` defaults to 200, so do not assume it sets 201 for creates.
- Standard errors are mapped by `GlobalExceptionHandler` to `ApiResponse.Fail`.
  Specify validation, 401, 403, 404, 409, 422 and 500 behavior where applicable.
  Framework/auth errors and non-JSON/file endpoints need explicit documentation.
- Use stable `errorCode` for client decisions/localized messages, not human-readable
  message comparisons. Never return an HTTP 200 success body for a failed operation.

Example successful paged response:

```json
{
  "success": true,
  "status": 200,
  "data": { "items": [], "totalCount": 0, "page": 1, "pageSize": 20 },
  "traceId": "request-trace-id"
}
```

Example error response:

```json
{
  "success": false,
  "status": 404,
  "errorCode": "NOT_FOUND",
  "title": "NOT_FOUND",
  "message": "Resource not found.",
  "traceId": "request-trace-id"
}
```

Error-code examples are illustrative; use the actual constants in
`G54.BLL.Common.Constants.ErrorCodes` and the feature contract.

## Frontend-first implementation

- Create a typed Angular HttpClient service from the contract immediately.
  Components call the service, not HTTP directly. Use HttpParams for query values.
- `environment.apiUrl` is the separate backend origin without a path or trailing slash.
  Append `/<resource>` once. Never hardcode deployment hosts.
- nginx routes by Host: FRONTEND_HOST serves Angular and API_HOST proxies every path
  to the backend. API_URL is the browser-visible API origin; FRONTEND_ORIGIN configures
  backend CORS and API_URL is included in frontend CSP connect-src. No Accept-based routing.
  Keep the auth interceptor's API resource allowlist in sync when adding resources.
  Tokens may only go to the configured origin and known API paths, never third parties.
- Use the real API adapter by default. If demo data is needed before backend delivery,
  use a separate explicitly selected development mock adapter/fixture with the same
  DTO shape. Keep API switching at the provider/config boundary, not in components.
- Do not automatically fall back to mock success on HTTP failures, auth errors or
  empty results. Preserve real empty/error states. Failed writes must remain failures.
- Mock IDs, enums and payloads must be valid for the agreed backend contract.
  Do not assume TypeScript annotations validate incoming JSON at runtime.
- Verify method, final URL, query, body and envelope handling with HttpTestingController.
  Include empty data and error cases so mocks cannot hide integration mismatches.

## Backend-first implementation

- Keep API -> BLL -> DAL dependency direction, thin controllers, BLL validation and
  ownership checks, cancellation tokens, and authorization metadata.
- Implement explicit routes, BLL DTOs, correct JSON serialization/status codes and
  ProducesResponseType metadata from the feature contract.
- Publish accurate OpenAPI/Swagger schemas and JSON examples that the frontend can
  implement directly. Do not make the frontend reverse-engineer entities or errors.
- Verify wire behavior with appropriate controller/integration/contract tests,
  including auth, invalid requests and failed writes. Service-only unit tests do not
  establish that a public route or JSON envelope matches the contract.

## Completion

Run the applicable frontend/backend review scripts. Verify against a running backend
when available. State which endpoints were exercised and which remain planned or
mock-only. Matching conventions enables integration without changing the client;
it does not create a missing backend or configure deployment/network access.
