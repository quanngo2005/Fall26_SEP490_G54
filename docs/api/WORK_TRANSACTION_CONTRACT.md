# Work Transaction Reconcile Contract

Status: **IMPLEMENTED** (PR 3, 4/11/2026) - verified by unit tests and by the migration + seed on PostgreSQL; HTTP exchange not yet verified against a running API.

Follows `API_CONVENTIONS.md`. Imports are described in `TRANSACTION_IMPORT_CONTRACT.md`.

## Rules

- An employee attaches up to **10** transaction codes to a work they are assigned to. A code can belong to one work only (409 `WORK_TRANSACTION_DUPLICATE`); the 11th code is 409 `WORK_TRANSACTION_LIMIT`.
- Each code has its own status:

| Status | Meaning |
| --- | --- |
| `IN_REVIEW` | The code is not in any import yet, or it cannot be credited yet (`reason` holds the error code, e.g. `KPI_TARGET_NOT_ASSIGNED`, `KPI_AGGREGATION_NOT_SUPPORTED`). Retried on the next import. |
| `MATCHED` | Imported code, the imported employee is the work's assignee and `product_code` equals `WorkType.ProductCode`. KPI is credited immediately. |
| `NOT_MATCHED` | Final. `reason` is `EMPLOYEE_MISMATCH` or `PRODUCT_MISMATCH`. |

- Adding a code moves the work to `SUBMITTED` (from `NEW`, `IN_PROGRESS`, `REJECTED`, `OVERDUE`). The work becomes `DONE` only when **all** its codes are `MATCHED`; otherwise it stays `SUBMITTED`. No new work status.
- A matched code credits **every** KPI mapped to the work type, each with its own value: `work_type_kpi.value_source` = `COUNT` (default, credits 1) or `AMOUNT` (credits the file's `amount`). The multiplier is applied only when the KPI is scored. SUM, RATIO (uses the file's `denominator`) and AVG are supported; `NO_AGGREGATION` is never credited (code stays `IN_REVIEW`, reason `KPI_AGGREGATION_NOT_SUPPORTED`).
- Reconcile runs when a code is added and after every `POST /transaction-imports` (all `IN_REVIEW` codes that appear in the new batch). It is idempotent: one matched code writes one ledger entry.
- A handed-over work keeps the 30/70 split.

## Endpoints (authenticated; only the work's assignee, otherwise 403)

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/works/{workId}/transactions` | List the codes. |
| POST | `/works/{workId}/transactions` | Body `{ "transactionCodes": ["TX1", "TX2"] }`. Adds and reconciles. |
| DELETE | `/works/{workId}/transactions/{transactionId}` | Remove a code that is not `MATCHED` (409 `WORK_INVALID_STATE` otherwise). |

All three return `ApiResponse<WorkTransactionsDto>`:

```json
{ "workId": "guid", "workStatus": "SUBMITTED",
  "items": [ { "id": "guid", "transactionCode": "TX1", "status": "MATCHED", "reason": null, "updatedAt": "..." } ] }
```

Errors: 400 `VALIDATION_ERROR` (no codes, code over 100 characters), 401, 403, 404, 409 `WORK_INVALID_STATE`, `WORK_TRANSACTION_LIMIT`, `WORK_TRANSACTION_DUPLICATE`.

## Data

- `work_transaction` (new): work, code (unique), status, reason, `imported_transaction_id` (unique).
- `work_type_kpi.value_source` (new, default `COUNT`, existing rows backfilled by the column default).
- `verified_work_result.verification_id` is now nullable and `work_transaction_id` links import-credited entries; the `validate_work_result_item` trigger accepts either source.
- Seed (`seed_dev_data.sql`): a demo import batch and NV004 works in `MATCHED` (DONE), `NOT_MATCHED` and `IN_REVIEW` states.