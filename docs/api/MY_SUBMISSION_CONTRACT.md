# API Contract — My Submissions (Lịch sử nộp kết quả, SCR-REV-01 / SCR-REV-02)

Status: **IMPLEMENTED** (11/11/2026) — unit-tested with InMemory (`MySubmissionServiceTests`) and by frontend specs; not run against PostgreSQL or a running API.

Follows `API_CONVENTIONS.md`. The submission itself is described in `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`, transaction codes in `WORK_TRANSACTION_CONTRACT.md`, appeals in `APPEAL_CONTRACT.md`.

## 1. Endpoints (authenticated; the caller only ever sees their own submissions)

| Method | Route | Query | Response |
| --- | --- | --- | --- |
| GET | `/submissions/mine` | `matchStatus`, `periodId`, `keyword`, `page` (default 1), `pageSize` (default 20, max 100) | 200 `PagedResult<MySubmissionListItemDto>` |
| GET | `/submissions/{submissionId}/reconciliation` | — | 200 `MySubmissionDetailDto` |

There is no `[HasPermission]`: the employee comes from the token and the service enforces ownership (listed in `intentionallyOpen` of `scripts/codereview-backend.ps1`).

### 1.1 List

- One row per work: the **latest version** (highest `submissionNo`) of the caller's submissions. Earlier versions are reachable from the detail's `versions`. `DRAFT` submissions are never listed.
- Newest first (`submittedAt` descending, then `submissionId`).
- `matchStatus`: `NONE` (the work has no transaction code), `MATCHED` (every code matched), `NOT_MATCHED` (at least one code not matched, final), `IN_REVIEW` (no code rejected but one still waits for imported data). Any casing; another value is 400 `VALIDATION_ERROR`. Same aggregation as `GET /verifications`.
- `periodId`: only submissions with `submittedAt` between the period's start and end date (UTC, inclusive). Unknown id is 404.
- `keyword`: case-insensitive contains (`ILike`, `%` and `_` escaped) on the work code, the work title or a transaction code of the work.
- A page beyond the last one returns no items and the real `totalCount`.

```json
{ "items": [ {
    "submissionId": "guid", "submissionCode": "#SUB-WK-1018-V2", "submissionNo": 2,
    "workId": "guid", "workCode": "WK-1018", "workTitle": "…", "workTypeName": "Tiền gửi",
    "submittedAt": "2026-10-20T09:12:40+00:00", "status": "SUBMITTED", "matchStatus": "NOT_MATCHED",
    "transactionCount": 2, "matchedTransactionCount": 1, "firstTransactionCode": "TXN-77001",
    "appealDeadline": null } ],
  "totalCount": 9, "page": 1, "pageSize": 20 }
```

`workTypeName` and `firstTransactionCode` can be `null`. `appealDeadline` is set only while the submission is `REJECTED`, has no appeal in progress and is still inside the window.

### 1.2 Detail

Errors: 403 `FORBIDDEN` (submission of someone else), 404 `NOT_FOUND` (unknown id or a draft).

```json
{ "submissionId": "guid", "submissionCode": "#SUB-WK-1018-V2", "submissionNo": 2,
  "workId": "guid", "workCode": "WK-1018", "workTitle": "…", "workTypeName": "Tiền gửi",
  "submittedAt": "…", "status": "SUBMITTED", "matchStatus": "NOT_MATCHED",
  "evidences": [ { "id": "guid", "fileName": "hop-dong.pdf", "filePath": "…", "mimeType": "application/pdf", "fileSize": 1024, "uploadedAt": "…" } ],
  "transactions": [ {
      "transactionCode": "TXN-77001", "status": "NOT_MATCHED", "reason": "PRODUCT_MISMATCH",
      "fields": [
        { "field": "TRANSACTION_CODE", "declared": "TXN-77001", "imported": "TXN-77001", "matched": true },
        { "field": "EMPLOYEE", "declared": "NV-0412", "imported": "NV-0412", "matched": true },
        { "field": "PRODUCT", "declared": "DEPOSIT", "imported": "CREDIT_CARD", "matched": false },
        { "field": "AMOUNT", "declared": null, "imported": "95000000", "matched": null } ] } ],
  "versions": [ { "submissionId": "guid", "submissionNo": 2, "submittedAt": "…", "status": "SUBMITTED", "matchStatus": "NOT_MATCHED" } ],
  "appeal": { "state": "NOT_APPLICABLE", "deadline": null, "appealId": null } }
```

- `fields[].declared` is what the work expects (the code the employee attached, the assignee's employee code, `WorkType.ProductCode`); `imported` is the row of the data import with the same transaction code (only a `MATCHED` code is linked to its row, so a code rejected for the employee or the product is looked up by its code, as the reconcile does). The employee is compared by id and the product ignoring case, like the reconcile. `matched` is `null` when there is nothing to compare: the code has no import yet (every `imported` is `null`), or `AMOUNT`, which has no declared value. `AMOUNT.imported` is a plain decimal string.
- `transactions[].reason` is the error code of the work transaction (`EMPLOYEE_MISMATCH`, `PRODUCT_MISMATCH`, `KPI_TARGET_NOT_ASSIGNED`, `KPI_AGGREGATION_NOT_SUPPORTED`) or `null`.
- `versions`: every non-draft submission of the caller for the same work, newest first. `matchStatus` is the work's current reconciliation, shared by all versions.
- `appeal.state`: `NOT_APPLICABLE` (not `REJECTED`), `AVAILABLE` (rejected, before `deadline` = rejection time + `SystemParameters:AppealRejectedHours`, default 48 h), `EXPIRED`, `IN_PROGRESS` (an appeal is `OPEN` / `UNDER_REVIEW`, `appealId` set). Filing is still `POST /submissions/{id}/appeals`, which enforces the same rules.

## 2. Differences from the SCR-REV mockup

The data model has no CIF code, no declared product or amount on the submission, and a result appeal is only possible for a **rejected** submission, so the screen follows the real rules: the comparison is per transaction code (code, employee, product, amount), a not-matched submission shows guidance instead of an appeal button, and the appeal button appears only for `REJECTED` submissions inside the window.
