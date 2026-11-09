# Traceability (drill-down) contract

Read-only drill-down below the performance dashboard: score -> KPI -> ledger line -> result -> transaction.
Routes sit under `/performance-dashboard`. Conventions: `docs/api/API_CONVENTIONS.md`; the dashboard itself:
`docs/api/PERFORMANCE_DASHBOARD_CONTRACT.md`.

## Endpoints

| Step | Endpoint | Permission |
| --- | --- | --- |
| Score -> ledger | `GET /performance-dashboard/employees/{employeeId}/ledger?periodId=&kpiId=&page=&pageSize=` | `evaluation:read` |
| Ledger line -> work / submission / risk / appeal | `GET /performance-dashboard/ledger-entries/{entryId}` | `evaluation:read` |
| Ledger line -> transactions | `GET /performance-dashboard/ledger-entries/{entryId}/transactions` | `evaluation:read` |

Scope is the same as `GET /performance-dashboard/employees/{employeeId}`: the caller's own id is always allowed;
another employee needs scope `Dept` or higher and the employee's department must be visible to the caller. For a
ledger line the employee is the one the line belongs to. Anyone inside that scope sees the transaction codes
unmasked (decision 5/11/2026); the values are never logged.

## Ledger list

Query: `periodId` (omit for the current period, same rule as the dashboard; a `DAY` period is 400), `kpiId`
(optional), `page` (>= 1, default 1), `pageSize` (1..100, default 20). An invalid page is 400 `VALIDATION_ERROR`.

Lines, newest `occurredAt` first:

| `type` | Source | `points` |
| --- | --- | --- |
| `RESULT` | a verified work result item (manual approval or matched import) | real amount x multiplier |
| `RISK_PENALTY` | a risk event of the period that is not `DISMISSED` | minus the severity penalty |
| `HOLD`, `TASK_DEDUCTION`, `REFUND` | an appeal / manager score entry of the period | signed as stored |

`kpiId` keeps only the `RESULT` lines of that KPI (risk and appeal lines belong to the total, not to a KPI), so
`summary.riskPenaltyPoints` and `summary.scoreEntryPoints` are 0 with a `kpiId`.

```json
{
  "success": true,
  "data": {
    "period": { "id": "...", "code": "2026-10", "name": "2026-10", "periodType": "MONTH", "startDate": "2026-10-01", "endDate": "2026-10-31" },
    "summary": { "resultPoints": 15.00, "riskPenaltyPoints": -5.00, "scoreEntryPoints": -5.00 },
    "entries": {
      "items": [
        {
          "id": "guid", "type": "RESULT", "occurredAt": "2026-10-22T06:00:00+00:00",
          "points": 15.0, "rawValue": 10.0, "multiplier": 1.5,
          "kpiDefinitionId": "guid", "kpiCode": "HDV",
          "workId": "guid", "workCode": "W-1", "description": "Work title"
        }
      ],
      "totalCount": 3, "page": 1, "pageSize": 20
    }
  }
}
```

`rawValue` is the real amount, `multiplier` the product multiplier (BR-48) snapshotted at approval and `points` their
product; the KPI is scored from points only for `SUM` KPIs. `RISK_PENALTY` and score entries have `rawValue`,
`multiplier`, `kpiDefinitionId`, `kpiCode` null; `workId` is set on a score entry that came from a task deduction.
`summary` covers all lines, not just the page. `period` is `null` (and no lines) when no period exists.

## Ledger entry

`data` = `{ entry, employeeId, work, submission, riskEvent, appeal, recordedByName }`. `entry` is the list line.

- `work`: `{ id, code, title, workTypeCode, status }`, null when the line has no work.
- `submission`: `{ id, submissionNo, status, submittedAt, comment, verifierName, decision, verifiedAt }`; only for a
  `RESULT` approved manually. A result written by an imported match has no submission, so it is null.
- `riskEvent`: `{ id, code, eventType, severity, status, description }` for `RISK_PENALTY`.
- `appeal`: `{ id, status, reason }` for a score entry that came from an appeal.
- `recordedByName`: who recorded the risk event or score entry; null for a result.

## Ledger entry transactions

`data` = `{ entryId, workId, items }`. `items` are all transaction codes of the line's work, oldest first:
`{ id, transactionCode, status, reason, isSource, productCode, amount, transactionDate, referEmployeeCode,
importFileName, importedAt }`.

- `status`: `MATCHED`, `NOT_MATCHED` or `IN_REVIEW` (`docs/api/WORK_TRANSACTION_CONTRACT.md`).
- `isSource`: true for the code the verified result was written from.
- `productCode`, `amount`, `transactionDate`, `referEmployeeCode`, `importFileName`, `importedAt` come from the
  imported file and are null for a code that matched nothing.
- A line without a work (risk penalty, appeal hold) returns `workId: null` and `items: []`, not an error.

## Errors

| Situation | HTTP | errorCode |
| --- | --- | --- |
| Missing, expired or malformed token | 401 | `UNAUTHORIZED` |
| No `evaluation:read`, or the employee is outside the caller's visible departments | 403 | `FORBIDDEN` |
| Unknown employee, period or ledger entry | 404 | `NOT_FOUND` |
| `periodId` is a `DAY` period, or `page` / `pageSize` out of range | 400 | `VALIDATION_ERROR` |

## Limits

- The list reads the results, risk events and score entries directly, not the stored KPI rows; it shows lines even
  for an evaluation that was never calculated. Its `resultPoints` may differ from the calculated KPI score after a
  later approval until the next calculation.
- Lines are not paged in the database; the three sources are merged in memory.
- A result's `occurredAt` is the verification time; a risk event's is its `occurredAt`; a score entry's its `createdAt`.
