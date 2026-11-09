# Period lock (BR-29)

Status: backend implemented as a **write guard only**. There is no new endpoint, route or DTO. Responses use
`ApiResponse<T>` (see `API_CONVENTIONS.md`). Reversal entries (BR-16) and an automatic lock at 23:59:59 are **not**
implemented.

## Rule

KPI data of a locked evaluation period is read-only. A period is **locked** when `evaluation_period.status` is
`LOCKED` or `CLOSED`. `OPEN` and `IN_REVIEW` accept writes (they are handled by the existing per-endpoint rules).

The lock unit is the **evaluation period** (`EvaluationPeriod.Status`), not the calendar month. A period is locked by
`POST /evaluations/approvals/finalize` (see `EVALUATION_PERIOD_CONTRACT.md`). Open question for the product owner:
BR-29 says "preceding month"; the system locks per evaluation period (`docs/BPMS_AI_Agent_Context.md` section 9).

## Guarded writes

A guarded write that targets a locked period fails with **409 `PERIOD_LOCKED`**. Nothing is written.

| Endpoint | How the period is resolved |
| --- | --- |
| `POST /verifications/{submissionId}/decision` with `APPROVED` and submitted KPI metrics | the employee's `ACTIVE` target that covers today's business date (UTC+7); its `evaluationPeriodId` |
| `POST /appeals/{id}/decision` with `ACCEPT` (records the verified result) | same as above |
| `POST /target-assignments` | body `evaluationPeriodId` |
| `POST /target-assignments/{id}/allocations` | the parent target's `evaluationPeriodId` |
| `POST /target-assignments/{id}/period-allocations` | the parent target's period and every `evaluationPeriodId` in the body |
| `POST /target-assignments/{deptYearId}/confirm` | the department year target's period, each quarter target's period, and every month and day period it would generate |

Rules:

- The check runs before any row is written, so a rejected request leaves the database unchanged.
- It runs after authorization and existence checks, so an unknown id is still 404 and a forbidden caller is still 403.
- Targets for `ImportYearTargets`, `AllocateDepartments` and `AllocateQuarters` already reject every non-`OPEN`
  period with 409 `TARGET_PERIOD_NOT_OPEN` (`TARGET_ALLOCATION_CONTRACT.md`). That code is unchanged.
- When an approved result cannot be credited to a locked period, the whole approval is rejected. The result is
  **not** moved to the active period automatically. Correcting a locked period through a negative entry in the active
  period (BR-16) is a separate, planned feature.

## Not guarded (out of scope)

These do not write KPI progress, so they are not blocked by this rule: work create/status changes, result submission
and withdrawal, evidence upload, risk-event confirmation (already 409 `RISK_EVENT_PERIOD_UNAVAILABLE`), score
calculation (already 409 `PERFORMANCE_EVALUATION_INVALID_STATE`) and the appeal score ledger (already writes to the
active period).

## Errors

| Status | `errorCode` | When |
| --- | --- | --- |
| 409 | `PERIOD_LOCKED` | a guarded write targets a period whose status is `LOCKED` or `CLOSED` |

Messages (`ErrorCodes.GetDefaultMessage`):

- vi: `Kỳ đánh giá đã bị khóa nên không thể thay đổi hoặc nhập dữ liệu KPI.`
- en: `The evaluation period is locked, so KPI data cannot be changed or imported.`

Clients branch on `errorCode`, not on `message`.
