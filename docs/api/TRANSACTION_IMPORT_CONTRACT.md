# Transaction Import Contract (Core Banking Excel)

Status: **IMPLEMENTED** (PR 2, 4/11/2026; template & role permissions TI-B, 10/11/2026) - verified by unit tests; HTTP exchange not yet verified against a running API.
Hardening round (SRS UC33, BR-08, BR-09, BR-29): **IMPLEMENTED** 10/11/2026 - unit-tested only; not yet run against PostgreSQL or over HTTP.

Admin, Department Manager, Branch Manager, and QA/Inspector import Core Banking transactions from an Excel file. Imported rows are only stored; matching them to
works and crediting KPI is PR 3.

## Permission

- `TRANSACTION_IMPORT_MANAGE` (resource `transaction-import`, action `manage`, scope `All`), granted to Admin,
  System Admin, Department Manager, Branch Manager and QA/Inspector (SRS UC31).
- No department scope. One file per request.

## Excel file (.xlsx, first sheet, row 1 = header)

| Column | Required | Type | Notes |
| --- | --- | --- | --- |
| `transaction_code` | yes | text, max 100 | Unique across all imports |
| `employee_code` | yes | text | Must exist in `employee.employee_code` |
| `product_code` | yes | text | Compared with `WorkType.ProductCode` in PR 3 |
| `transaction_date` | yes | date | |
| `amount` | yes | decimal >= 0 | |
| `denominator` | no | decimal > 0 | For RATIO / AVG KPIs |
| `refer_employee_code` | no | text | Stored only |

Max file size: `SystemParameters:ImportMaxFileMb`.

## Endpoints

### POST /transaction-imports (implemented)

`multipart/form-data`, field `file`. Returns `201` + `Location`, `ApiResponse<ImportBatchDto>`.

```json
{ "data": { "id": "guid", "fileName": "tx.xlsx", "status": "COMPLETED",
  "totalRows": 100, "importedRows": 97, "duplicateRows": 2, "errorRows": 1,
  "errors": [ { "row": 5, "field": "employee_code", "code": "EMPLOYEE_NOT_FOUND", "message": "..." } ],
  "importedAt": "2026-11-04T10:00:00Z", "importedBy": "guid",
  "reconcile": { "matched": 3, "notMatched": 1, "inReview": 0 } } }
```

`reconcile` (POST only) counts the work transaction codes found in the batch after the automatic reconcile:
`matched` credited, `notMatched` flagged for the manager (`EMPLOYEE_MISMATCH`, `PRODUCT_MISMATCH` or
`METRIC_TYPE_MISMATCH`), `inReview` still waiting (for example no target yet). `null` on the GET endpoints.

**BR-09 / MSG13.** An imported transaction is a quantitative value. KPIs of the work type whose data source is not
`TRANSACTION` (service ratings) are not credited from it. When the work type maps only such KPIs, the code is
`NOT_MATCHED` with reason `METRIC_TYPE_MISMATCH` (409 message: metric types do not match, manual mapping required).

Valid rows are imported; invalid rows are reported (not stored). A `transaction_code` that already exists is
counted in `duplicateRows` and skipped (re-uploading the same file is safe).

Errors: `400` missing/non-xlsx/empty file or missing header column (`IMPORT_INVALID_FILE`); `401`; `403`.
Row error codes: `REQUIRED_FIELD_MISSING`, `INVALID_VALUE`, `EMPLOYEE_NOT_FOUND`, `DUPLICATE_IN_FILE`,
`PERIOD_LOCKED`, `CONFLICTING_DUPLICATE`.

#### Duplicate and period-lock rules (BR-08, BR-29)

- A `transaction_code` that already exists with the **same** employee, product, date, amount, denominator and
  refer code is a duplicate: counted in `duplicateRows`, skipped. If any of those values **differ**, the row is an
  error `CONFLICTING_DUPLICATE` (message: transaction code already imported with different values) and is not stored.
- Concurrent imports of the same code never fail the request: the loser of the unique-index race is re-read and
  classified as duplicate or `CONFLICTING_DUPLICATE`. No `500`.
- A row whose `transaction_date` falls inside an evaluation period with status `LOCKED` or `CLOSED` is rejected with
  row error `PERIOD_LOCKED` (message follows MSG27: records of that period are locked, further changes require a
  Reversal Entry). It is not stored and not credited. No reversal entry is created by the import (decision of
  the product owner, 10/11/2026). Other rows of the file are still imported.
- Row validation order: required/format errors, `EMPLOYEE_NOT_FOUND`, `PERIOD_LOCKED`, then duplicate checks.

Every row error is persisted per batch and readable through `GET /transaction-imports/{id}/errors`.

### GET /transaction-imports (implemented)

Query `page` (default `1`), `pageSize` (default `20`, clamped to `1..200`). Returns
`ApiResponse<PagedResult<ImportBatchDto>>` (without `errors`), sorted by `ImportedAt` descending (newest first).

### GET /transaction-imports/{id} (implemented)

Returns `ApiResponse<ImportBatchDto>`; `404` if missing.

### GET /transaction-imports/{id}/transactions (implemented)

Query `page`, `pageSize`, `employeeCode`, `productCode`. Returns `PagedResult<ImportedTransactionDto>`:
`id, transactionCode, employeeCode, productCode, transactionDate, amount, denominator, referEmployeeCode`.

### GET /transaction-imports/{id}/errors (implemented; UC33)

Query `page`, `pageSize` (default 20, max 200). Returns `ApiResponse<PagedResult<ImportRowErrorDto>>` ordered by
`row`: `{ row, field, code, message }`. `404` if the batch does not exist. `GET /transaction-imports/{id}` keeps
`errors: null`; use this endpoint instead.

### GET /transaction-imports/template (implemented; UC29)

Returns the blank `.xlsx` template (`transaction-import-template.xlsx`) with the header row above (same columns and
order; headers must match exactly, no `*` suffix), one example row and a notes
sheet. Same permission as the import. `200` with the xlsx content type.
