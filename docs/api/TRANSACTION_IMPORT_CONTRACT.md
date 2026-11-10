# Transaction Import Contract (Core Banking Excel)

Status: **IMPLEMENTED** (PR 2, 4/11/2026) - verified by unit tests; HTTP exchange not yet verified against a running API.

Admin imports Core Banking transactions from an Excel file. Imported rows are only stored; matching them to
works and crediting KPI is PR 3.

## Permission

- `TRANSACTION_IMPORT_MANAGE` (resource `transaction-import`, action `manage`, scope `All`), granted to Admin only.
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
  "importedAt": "2026-11-04T10:00:00Z", "importedBy": "guid" } }
```

Valid rows are imported; invalid rows are reported (not stored). A `transaction_code` that already exists is
counted in `duplicateRows` and skipped (re-uploading the same file is safe).

Errors: `400` missing/non-xlsx/empty file or missing header column (`IMPORT_INVALID_FILE`); `401`; `403`.
Row error codes: `REQUIRED_FIELD_MISSING`, `INVALID_VALUE`, `EMPLOYEE_NOT_FOUND`, `DUPLICATE_IN_FILE`.

### GET /transaction-imports (implemented)

Query `page` (default `1`), `pageSize` (default `20`, clamped to `1..200`). Returns
`ApiResponse<PagedResult<ImportBatchDto>>` (without `errors`), sorted by `ImportedAt` descending (newest first).

### GET /transaction-imports/{id} (implemented)

Returns `ApiResponse<ImportBatchDto>`; `404` if missing.

### GET /transaction-imports/{id}/transactions (implemented)

Query `page`, `pageSize`, `employeeCode`, `productCode`. Returns `PagedResult<ImportedTransactionDto>`:
`id, transactionCode, employeeCode, productCode, transactionDate, amount, denominator, referEmployeeCode`.
