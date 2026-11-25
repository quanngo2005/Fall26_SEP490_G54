# Service Rating Import Contract (Excel)

Status: **IMPLEMENTED** (as of 23/11/2026) - unit-tested with InMemory (`ServiceRatingImportServiceTests`, `ServiceRatingImportTemplateTests`, `ControllerDelegationTests`); not yet driven over HTTP against PostgreSQL.

System Admin, Department Manager, Branch Manager and QA/Inspector (VERIFIER) import customer service ratings (`07_Danh_gia_dich_vu.xlsx`, UC32) and download the template (UC29). A 1-star rating alerts the department manager of the rated employee (BR-17).

## Permission

- `SERVICE_RATING_IMPORT_MANAGE` (resource `service_rating_import`, action `manage`, scope `All`), granted to the roles that hold `TRANSACTION_IMPORT_MANAGE`: SYSTEM_ADMIN, DEPT_MANAGER, BRANCH_MANAGER, VERIFIER (migration `AddServiceRatingImport`, `seed_dev_data.sql`).
- No department scope. One file per request.

## Excel file (.xlsx, first sheet, row 1 = header)

The template sheet is named `Đánh giá dịch vụ`; the importer reads the first sheet whatever its name. Headers are matched like the transaction import (trim, strip `*` and parenthetical notes, case- and accent-insensitive, aliases, any column order). Unknown extra columns are ignored.

| Template Header | Accepted Aliases | Required | Type | Notes |
| --- | --- | --- | --- | --- |
| `Ngày đánh giá*` | `rating_date` | yes | date | Excel date cell or text `dd/MM/yyyy` (`d/M/yyyy`, `yyyy-MM-dd` also accepted) |
| `Mã NV*` | `employee_code`, `Mã nhân viên` | yes | text | Must exist in `employee.employee_code` |
| `Mã giao dịch*` | `transaction_code` | yes | text, max 100 | Stored upper-cased; one rating per transaction code across all imports |
| `Điểm (1–5)*` | `score` | yes | integer 1..5 | |
| `Nhận xét KH` | `customer_comment` | no | text, max 1000 | |
| `Ghi chú (demo)` | `note` | no | text | Ignored during import |

Max file size: `SystemParameters:ImportMaxFileMb`. Max rows: `SystemParameters:ImportMaxRows`.

## Data

- `import_batch.file_type` (`TRANSACTION` | `SERVICE_RATING`, varchar, default `TRANSACTION` for every existing row). A batch is listed only by the endpoint of its own type.
- `service_rating(id, import_batch_id FK cascade, employee_id FK, transaction_code unique, rating_date date, score smallint CHECK 1..5, customer_comment null, created_at)`.
- Rejected rows: `import_batch_error(row_number, field, code, message)`, as for transactions.

## Endpoints

Same envelopes (`ApiResponse<T>`), paging (`PagedResult<T>`, `page` default 1, `pageSize` default 20 clamped to 1..200) and errors as `TRANSACTION_IMPORT_CONTRACT.md`.

### POST /service-rating-imports

`multipart/form-data`, field `file`. Returns `201` + `Location: /service-rating-imports/{id}`, `ApiResponse<ImportBatchDto>`:

```json
{ "data": { "id": "guid", "fileName": "07_Danh_gia_dich_vu.xlsx", "status": "COMPLETED",
  "totalRows": 16, "importedRows": 14, "duplicateRows": 1, "errorRows": 1,
  "errors": [ { "row": 9, "field": "Điểm (1–5)*", "code": "INVALID_VALUE", "message": "'Điểm (1–5)*' phải là số nguyên từ 1 đến 5." } ],
  "importedAt": "2026-11-23T04:30:00Z", "importedBy": "guid", "reconcile": null } }
```

`reconcile` is always `null` (ratings are not reconciled). Valid rows are stored; invalid rows are reported in Vietnamese with `field` set to the header as written in the file, persisted, and not stored.

A `transaction_code` already rated (in the database or on an earlier row of the same file) with identical employee, date, score and comment is counted in `duplicateRows` and skipped, so re-uploading a file is safe. With any different value the row is `CONFLICTING_DUPLICATE`. A concurrent import of the same code is re-read and classified the same way (no `500`).

Errors: `400` missing / non-xlsx / empty file or missing required header column (`IMPORT_INVALID_FILE`); `401`; `403`.

Row error codes, in validation order:
- `REQUIRED_FIELD_MISSING` — a required value is empty
- `INVALID_VALUE` — bad date, code over 100 characters, score not an integer 1..5, comment over 1000 characters
- `EMPLOYEE_NOT_FOUND` — `Mã NV*` does not exist
- `PERIOD_LOCKED` — the rating date falls inside a `LOCKED` or `CLOSED` evaluation period (MSG27 / BR-29, same rule as transactions)
- `CONFLICTING_DUPLICATE` — the transaction code is already rated with different values (see above)

#### BR-17 notification

After the batch is saved, each stored rating with score 1 raises one `RATING_ONE_STAR` notification (`IMPORTANT`, `relatedType = IMPORT_BATCH`, `relatedId` = batch id) to every active department manager (role `DEPT_MANAGER`) of the rated employee's department, the rated employee excepted. When the file has row errors, the importer also receives `IMPORT_COMPLETED_WITH_ERRORS`. Publishing is best effort and never fails the import.

### GET /service-rating-imports

Returns `ApiResponse<PagedResult<ImportBatchDto>>` of `SERVICE_RATING` batches (without `errors`), newest first.

### GET /service-rating-imports/{id}

Returns `ApiResponse<ImportBatchDto>`; `404` if missing or not a rating batch.

### GET /service-rating-imports/{id}/errors

Returns `ApiResponse<PagedResult<ImportRowErrorDto>>` (`{ row, field, code, message }`) ordered by row; `404` as above.

### GET /service-rating-imports/{id}/ratings

Returns `ApiResponse<PagedResult<ServiceRatingDto>>` ordered by transaction code; `404` as above.

```json
{ "id": "guid", "transactionCode": "GD2610001", "employeeCode": "NV001", "ratingDate": "2026-10-06", "score": 5, "customerComment": "Nhân viên nhiệt tình" }
```

### GET /service-rating-imports/template

Returns `service-rating-import-template.xlsx`: sheet `Đánh giá dịch vụ` with the six headers above and one sample row (`05/10/2026`, `NV001`, `TX-SAMPLE-0001`, `5`), plus a `Notes` sheet. Same permission. `200` with the xlsx content type.

## Hạn chế

- Ratings are stored only: **no KPI is calculated from them** yet (KPI data source `SERVICE_RATING` is not fed).
- The rating is not matched against `imported_transaction` (the transaction code is not required to exist there).
- The import history screen (SCR-ADM-07) lists transaction batches only; rating batches are reachable through this API.
