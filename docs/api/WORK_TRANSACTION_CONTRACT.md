# Work Transaction Reconcile Contract

Status: **IMPLEMENTED** (PR 3, 4/11/2026) - verified by unit tests and by the migration + seed on PostgreSQL; HTTP exchange not yet verified against a running API.

Follows `API_CONVENTIONS.md`. Imports are described in `TRANSACTION_IMPORT_CONTRACT.md`.

## Rules

- An employee attaches up to **10** transaction codes to a work they are assigned to. A code can belong to one work only (409 `WORK_TRANSACTION_DUPLICATE`); the 11th code is 409 `WORK_TRANSACTION_LIMIT`.
- Each code has its own status:

| Status | Meaning |
| --- | --- |
| `IN_REVIEW` | The code is not in any import yet, or it cannot be credited yet (`reason` holds the error code, e.g. `KPI_TARGET_NOT_ASSIGNED`, `KPI_AGGREGATION_NOT_SUPPORTED`). Retried on the next import. |
| `MATCHED` | Imported code, the imported employee is the work's assignee, `product_code` equals `WorkType.ProductCode` and the customer / referral checks below pass. KPI is credited immediately. `reason` is `null`, or `REFERRER_NOT_CREDITED` when the referrer's share was skipped (see Referral). |
| `NOT_MATCHED` | Final. `reason` is `EMPLOYEE_MISMATCH`, `PRODUCT_MISMATCH`, `CUSTOMER_MISMATCH`, `TRANSACTION_BEFORE_HANDOVER`, `REFERRER_MISMATCH` or `METRIC_TYPE_MISMATCH`. |

- Adding a code moves the work to `SUBMITTED` (from `NEW`, `IN_PROGRESS`, `REJECTED`, `OVERDUE`). The work becomes `DONE` only when **all** its codes are `MATCHED`; otherwise it stays `SUBMITTED`. No new work status.
- A matched code credits **every** KPI mapped to the work type, each with its own value: `work_type_kpi.value_source` = `COUNT` (default, credits 1) or `AMOUNT` (credits the file's `amount`). The multiplier is applied only when the KPI is scored. SUM, RATIO (uses the file's `denominator`) and AVG are supported; `NO_AGGREGATION` is never credited (code stays `IN_REVIEW`, reason `KPI_AGGREGATION_NOT_SUPPORTED`).
- Reconcile runs when a code is added and after every `POST /transaction-imports`. After an import it retries **every** `IN_REVIEW` code that has an imported row in any batch, not only codes of the new batch, so a code left `IN_REVIEW` (locked period, missing target) is matched by the next import once the cause is fixed. It is idempotent: one matched code writes one ledger entry.
- Transaction codes are trimmed and upper-cased when stored (both work codes and imported codes) and compared ignoring case, so `gd2610001` and `GD2610001` are the same code (and a duplicate on add).
- Checks, in order: employee (`EMPLOYEE_MISMATCH`), product (`PRODUCT_MISMATCH`), customer, then referral.
  - **Customer:** when the work has a `customerCif` and the file row has a customer code, they must be equal (trimmed,
    ignoring case), otherwise `CUSTOMER_MISMATCH`. A work without `customerCif` is not checked.
  - **Referral (BR-07):** the referrer is the work's latest `ACCEPTED` handover (`fromEmployee`, its `referrerSharePercent`).
    Without a handover, the file's Refer ID is the referrer with the default 30 % share (a Refer ID equal to the
    assignee is ignored). With a handover:
    - a transaction dated before the handover day (`handoverAt` at UTC+7) is `TRANSACTION_BEFORE_HANDOVER`;
    - a Refer ID naming someone other than the handover's referrer is `REFERRER_MISMATCH` (to be fixed by the manager).
  - The referrer gets `share %` of every KPI value and the assignee the rest. A referrer without an active target for a
    KPI (e.g. a Teller referring a term deposit has no `KPI_TERM_DEP` target) does not block the code: the assignee still
    gets their share, the referrer's share is not credited and the code is `MATCHED` with reason
    `REFERRER_NOT_CREDITED`. A missing target of the assignee still keeps the code `IN_REVIEW` (`KPI_TARGET_NOT_ASSIGNED`).
- Amount and date are never declared by the employee (only the code is entered): an `AMOUNT` KPI is credited with the
  Core Banking `amount` and the progress date is the Core Banking date. Not supported: a negative contribution (e.g.
  `KPI_CASA` = deposits − withdrawals) and per-customer counting (`KPI_NEW_CUST`, `KPI_CROSS_SELL`).
- The manual verification path (`VerifiedResultRecorder.RecordAsync`) is unchanged: there a referrer without a target
  still rejects the approval.

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