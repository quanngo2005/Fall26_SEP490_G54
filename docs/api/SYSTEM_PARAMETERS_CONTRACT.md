# System parameters (SCR-ADM-04, tab "Tham số vận hành")

Status: **implemented**. Responses use `ApiResponse<T>`, no pagination.
Both endpoints are admin only (same rule as `/sidebar-configurations`).

The "Thanh điều hướng theo vai trò" tab of the same screen uses
`SIDEBAR_CONFIGURATION_CONTRACT.md`.

## Endpoints

- `GET /system-parameters`: all parameters.
- `PUT /system-parameters`: replace the values of the listed editable parameters.

DTO `SystemParameter`:

| field       | type             | notes                                                     |
| ----------- | ---------------- | --------------------------------------------------------- |
| `key`       | string           | stable identifier, see table below                        |
| `value`     | number \| string | number for editable parameters; text for fixed ones       |
| `min`       | number \| null   | inclusive lower bound, `null` when not editable           |
| `max`       | number \| null   | inclusive upper bound, `null` when unbounded/not editable |
| `locked`    | boolean          | fixed by business rule; the server rejects changes        |
| `updatedAt` | string \| null   | ISO 8601 UTC                                              |
| `updatedBy` | string \| null   | account name of the last editor                           |

Request: `{ "changes": [{ "key": "appealWindowDays", "value": 7 }] }`. Response: the
full updated `SystemParameter[]`. Changes take effect immediately and are audit logged.

| key                    | unit | editable range / rule                    |
| ---------------------- | ---- | ---------------------------------------- |
| `manualApprovalHours`  | h    | 1–168                                    |
| `importMaxFileMb`      | MB   | 1–10                                     |
| `importFormats`        | text | locked, `.xlsx`                          |
| `kpiLockTime`          | time | locked, `23:59:59` (BR-29)               |
| `appealWindowDays`     | day  | 1–30                                     |
| `preCloseCheckDays`    | day  | 1–30                                     |
| `appealRejectedHours`  | h    | locked, `48` (BR-31)                     |
| `handoverThresholdVnd` | VND  | integer greater than 0, default 50000000 |

Errors: 400 `VALIDATION_ERROR` (unknown key, out of range, locked key, non-integer),
401, 403 `FORBIDDEN`, 409 `CONFLICT` (concurrent modification). The client never substitutes mock success for a failed request.

## Audit trail

Each change to a parameter is recorded in table `system_parameter_changes` with columns:
- `id`: UUID
- `key`: parameter key
- `old_value`, `new_value`: string representations
- `changed_by`: account ID of the editor
- `changed_at`: ISO 8601 UTC timestamp of the change

Index: `(key, changed_at)`.

## Consumers

The following system parameters are consumed by other services:
- `appealRejectedHours`: `AppealService.CreateAppealAsync` (hours for appeal deadline).
- `importMaxFileMb`: `TargetAssignmentController.Import` (max file size validation).

Other parameters are defined for future use or UI administration only.
