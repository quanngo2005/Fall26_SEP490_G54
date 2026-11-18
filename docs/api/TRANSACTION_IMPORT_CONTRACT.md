# Transaction Import Contract (Core Banking Excel)

Status: **IMPLEMENTED & VERIFIED** (as of 16/11/2026) - unit-tested (including real `06_Giao_dich.xlsx` sample), migration applied via `DbMigrator`, and verified over HTTP against PostgreSQL.

Admin, System Admin, Department Manager, Branch Manager, and QA/Inspector import Core Banking transactions from an Excel file (`06_Giao_dich.xlsx` or legacy English format) and automatically reconcile pending work transaction codes.

## Permission

- `TRANSACTION_IMPORT_MANAGE` (resource `transaction-import`, action `manage`, scope `All`), granted to Admin, System Admin, Department Manager, Branch Manager and QA/Inspector (SRS UC31).
- No department scope. One file per request.

## Excel file (.xlsx, first sheet, row 1 = header)

Headers are matched by normalized name (trim, strip `*` and parenthetical notes like `(VND)` or `(demo)`, case- and accent-insensitive) with aliases so both Vietnamese (`06_Giao_dich.xlsx`) and legacy English headers work in any column order. Unknown extra columns (`Tên sản phẩm`, `Ghi chú (demo)`) are ignored.

| Template Header | Accepted Aliases | Required | Type | Notes |
| --- | --- | --- | --- | --- |
| `Mã giao dịch*` | `transaction_code` | yes | text, max 100 | Unique across all imports |
| `Ngày GD*` | `transaction_date`, `Ngày giao dịch` | yes | date | Excel date cell or text `dd/MM/yyyy`, `d/M/yyyy`, `yyyy-MM-dd` |
| `Mã khách hàng*` | `customer_code` | when column present | text, max 50 | Stored in `imported_transaction.customer_code`; optional when column omitted (legacy files), required on every row when the column exists |
| `Mã NV*` | `employee_code`, `Mã nhân viên` | yes | text | Must exist in `employee.employee_code` and not be `TERMINATED` |
| `Mã sản phẩm*` | `product_code` | yes | text, max 100 | Must exist in the product catalog (distinct `WorkType.ProductCode`, BR-33/48) |
| `Tên sản phẩm` | `product_name` | no | text | Ignored during import |
| `Số tiền (VND)*` | `amount`, `Số tiền` | yes | decimal >= 0 | Numeric cell or text with `.` / `,` / space thousands separators (e.g. `15.000.000`, `15,000,000`) |
| `Mẫu số` | `denominator` | no | decimal > 0 | For RATIO / AVG KPIs |
| `Refer ID` | `refer_employee_code`, `Mã NV giới thiệu` | no | text, max 50 | Optional referrer employee code (BR-07); when provided, must exist in `employee.employee_code` |
| `Ghi chú (demo)` | `note` | no | text | Ignored during import |

Max file size: `SystemParameters:ImportMaxFileMb`. Max rows: `SystemParameters:ImportMaxRows`.

## Endpoints

### POST /transaction-imports (implemented)

`multipart/form-data`, field `file`. Returns `201` + `Location`, `ApiResponse<ImportBatchDto>`.

```json
{ "data": { "id": "guid", "fileName": "06_Giao_dich.xlsx", "status": "COMPLETED",
  "totalRows": 23, "importedRows": 18, "duplicateRows": 0, "errorRows": 5,
  "errors": [ { "row": 22, "field": "Mã NV*", "code": "EMPLOYEE_NOT_FOUND", "message": "Mã nhân viên 'NV999' không tồn tại." } ],
  "importedAt": "2026-11-16T04:30:00Z", "importedBy": "guid",
  "reconcile": { "matched": 0, "notMatched": 0, "inReview": 0 } } }
```

`reconcile` (POST only) counts the work transaction codes reconciled after the import (every `IN_REVIEW` code that has an imported row, including codes from earlier batches; see `WORK_TRANSACTION_CONTRACT.md`). The batch is saved before reconcile runs: if reconcile fails, the response is still 201 with `"reconcile": null` and the codes are retried by the next import. Codes are stored upper-cased. Counts:
`matched` credited, `notMatched` flagged for the manager (`EMPLOYEE_MISMATCH`, `PRODUCT_MISMATCH` or `METRIC_TYPE_MISMATCH`), `inReview` still waiting (for example no target yet). `null` on the GET endpoints.

**BR-09 / MSG13.** An imported transaction is a quantitative value. KPIs of the work type whose data source is not `TRANSACTION` (service ratings) are not credited from it. When the work type maps only such KPIs, the code is `NOT_MATCHED` with reason `METRIC_TYPE_MISMATCH`.

Valid rows are imported; invalid rows are reported in Vietnamese with `field` set to the exact header name written in the uploaded file, and are not stored. A `transaction_code` that already exists with identical values is counted in `duplicateRows` and skipped (re-uploading the same file is safe).

Errors: `400` missing/non-xlsx/empty file or missing required header column (`IMPORT_INVALID_FILE`); `401`; `403`.
Row error codes:
- `REQUIRED_FIELD_MISSING` — missing required value (including `Mã khách hàng*` when column is present)
- `INVALID_VALUE` — invalid date format, negative/unparseable amount, `<= 0` denominator, or max-length exceeded
- `EMPLOYEE_NOT_FOUND` — `employee_code` does not exist
- `EMPLOYEE_TERMINATED` — `employee_code` belongs to an employee whose current status is `TERMINATED`
- `PRODUCT_NOT_FOUND` — `product_code` does not match any `WorkType.ProductCode` in the catalog (note: `WorkType.ProductCode` is the single product catalog source of truth in DAL)
- `REFER_EMPLOYEE_NOT_FOUND` — `refer_employee_code` is provided but does not exist
- `PERIOD_LOCKED` — `transaction_date` falls inside a `LOCKED` or `CLOSED` evaluation period (MSG27 / BR-29)
- `DUPLICATE_IN_FILE` — `transaction_code` appears more than once in the uploaded file (first valid row kept, BR-51)
- `CONFLICTING_DUPLICATE` — `transaction_code` already exists in DB with different employee, customer, product, date, amount, denominator, or refer code (BR-08)

#### Validation order and concurrency (BR-08, BR-29, BR-33/48, BR-51)

- Row validation order: required/format errors → `EMPLOYEE_NOT_FOUND` → `EMPLOYEE_TERMINATED` → `PRODUCT_NOT_FOUND` → `REFER_EMPLOYEE_NOT_FOUND` → `PERIOD_LOCKED` → `CONFLICTING_DUPLICATE` / duplicate check → `DUPLICATE_IN_FILE`.
- Concurrent imports of the same code never fail the request: the loser of the unique-index race is re-read and classified as duplicate or `CONFLICTING_DUPLICATE`. No `500`.
- Every row error is persisted per batch in `import_batch_errors` and readable through `GET /transaction-imports/{id}/errors`.

### GET /transaction-imports (implemented)

Query `page` (default `1`), `pageSize` (default `20`, clamped to `1..200`). Returns `ApiResponse<PagedResult<ImportBatchDto>>` (without `errors`), sorted by `ImportedAt` descending (newest first).

### GET /transaction-imports/{id} (implemented)

Returns `ApiResponse<ImportBatchDto>`; `404` if missing.

### GET /transaction-imports/{id}/transactions (implemented)

Query `page`, `pageSize`, `employeeCode`, `productCode`. Returns `PagedResult<ImportedTransactionDto>`:
`id, transactionCode, employeeCode, productCode, transactionDate, amount, denominator, referEmployeeCode, customerCode`.

### GET /transaction-imports/{id}/errors (implemented; UC33)

Query `page`, `pageSize` (default 20, max 200). Returns `ApiResponse<PagedResult<ImportRowErrorDto>>` ordered by `row`: `{ row, field, code, message }`. `404` if the batch does not exist. `GET /transaction-imports/{id}` keeps `errors: null`; use this endpoint instead.

### GET /transaction-imports/template (implemented; UC29)

Returns the `.xlsx` template (`transaction-import-template.xlsx`) with the 10 Vietnamese headers above (`Mã giao dịch*`, `Ngày GD*`, `Mã khách hàng*`, `Mã NV*`, `Mã sản phẩm*`, `Tên sản phẩm`, `Số tiền (VND)*`, `Mẫu số`, `Refer ID`, `Ghi chú (demo)`), one sample row (`05/10/2026`, `KH10001`, `NV001`, `TD_CLASSIC`, `10000000`, `1`, `NV002`), and a `Notes` sheet. Same permission as the import. `200` with the xlsx content type.
