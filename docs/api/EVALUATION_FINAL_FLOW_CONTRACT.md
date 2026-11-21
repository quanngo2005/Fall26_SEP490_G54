# Evaluation Final Flow (DM -> BM -> FINAL)

Status: implemented (2026-11-20). Extends `EVALUATION_CONTRACT.md` (form, my-evaluation) and
`RANKING_BOARD_CONTRACT.md` (ranking screen). Enums are UPPER_SNAKE strings matching PostgreSQL names.

The employee check/confirm step is removed. The DM presses "Hoàn tất" after the period's ranking lock day, and the
evaluation is then sent to the branch manager (BM).

Actors: DM = Department Manager (Trưởng phòng), BM = Branch Manager (Giám đốc chi nhánh), Employee = nhân viên.

## Status model: `performance_evaluation_status`

| Value | Set by | Meaning |
| --- | --- | --- |
| `DRAFT` | DM save (initial) | Draft, being edited by DM |
| `DISPUTED` | Employee appeal | Score frozen until the appeal is resolved; blocks save and send |
| `CALCULATED` | DM "Hoàn tất" (on or after lock day), or BM returns to check | Score locked by DM; the employee is notified (`EVALUATION_CALCULATED`) |
| `SUBMITTED` | BM-side "Gửi Giám đốc chi nhánh" on the ranking board | Sent to BM for approval |
| `FINAL` | BM approval of the calibration (`approvals/{departmentId}/approve`, `approvals/finalize`) | Last record; the final tier is real and shown in reports |

Removed: `EMPLOYEE_CHECK`, `EMPLOYEE_CONFIRMED`, `IN_REVIEW`, `APPROVED`. `PerformanceEvaluationStatus` in
`PostgresEnums.cs`, the DB enum (migration `20261120020000_RemoveEmployeeCheckEvaluationStatuses`), and
`frontend/src/app/core/evaluation.models.ts` must match this list exactly.
`IN_REVIEW` of `work_transaction` and `PeriodStatus` is a different enum and is not changed.

## Transitions

| From | To | Actor | Endpoint | Condition |
| --- | --- | --- | --- | --- |
| `DRAFT` | `CALCULATED` | DM | `POST /evaluations/submit` (`isDraft = false`) | `rankingLockDay` reached (`IsRankingLocked`); form has a total score. Sends `EVALUATION_CALCULATED` to the employee once |
| `CALCULATED` | `SUBMITTED` | DM | `POST /evaluations/ranking-board/send-to-director` | Batch for the department. Blocked by `sendBlockers` (see below) |
| `SUBMITTED` | `FINAL` | BM | `POST /evaluations/approvals/{departmentId}/approve`, `POST /evaluations/approvals/finalize` | Calibration approved |
| `SUBMITTED` | `CALCULATED` | BM | `POST /evaluations/approvals/{departmentId}/recalibrate` | BM asks for re-check; reason required |
| any | `DISPUTED` | Employee | appeal flow | Blocks every transition above |

- `DRAFT` and `CALCULATED`: the form can be saved through `POST /evaluations/submit` with `isDraft = true`
  (a `CALCULATED` record stays `CALCULATED`).
- `POST /evaluations/submit` on `DISPUTED`, `SUBMITTED` or `FINAL` returns 409 `PERFORMANCE_EVALUATION_INVALID_STATE`
  (for `DISPUTED`: 409 `EVALUATION_DISPUTE_PENDING`).
- Saving a draft is never blocked by the lock day. Only completing (`isDraft = false`) is.

## Lock day

The lock day is `rankingLockDay` of the period (same source as the ranking board `rankingLockDate`).
`POST /evaluations/submit` with `isDraft = false` before the lock day returns 409 `PERIOD_NOT_LOCKED_YET`.
The form returns `canComplete = true` only when the caller can edit and the lock day has passed. The FE shows a
notice (`evaluationForm.completeLockedNotice`) while `canEdit` is true and `canComplete` is false.

## Send to BM gate (`sendBlockers`)

`POST /evaluations/ranking-board/send-to-director` is allowed only when `sendBlockers` is empty.
`sendBlockers` (Vietnamese text) contains:

1. A tier without a configured slot.
2. Employees with an open dispute (`DISPUTED`): "Còn N nhân viên đang khiếu nại (Disputed)."
3. Employees not yet `CALCULATED`: "Còn N nhân viên chưa hoàn tất đánh giá (chưa ở trạng thái Calculated)."
4. Employees whose tier was changed without a reason.

The time gate of the old flow (lock day + `AppealRejectedHours`) is removed.

## Appeal

- `canAppeal` is true when the remaining appeal days are > 0 and the status is `CALCULATED`, `SUBMITTED` or `FINAL`.
- Filing the appeal does not change the appeal-day calculation.

## Automatic calculation

There is no background job that moves statuses. Every change above is made by a user action.

## Endpoints in this flow

| Endpoint | Permission | Scope | Actor |
| --- | --- | --- | --- |
| `POST /evaluations/submit` (`isDraft = false`) | `evaluation:submit` | `Dept` | Department Manager |
| `POST /evaluations/ranking-board/send-to-director` | `evaluation:finalize` | `Branch` | Department Manager (sends the ranking) |
| `POST /evaluations/approvals/{departmentId}/approve` | `evaluation:finalize` | `Branch` | Branch Manager |
| `POST /evaluations/approvals/{departmentId}/recalibrate` | `evaluation:finalize` | `Branch` | Branch Manager |
| `POST /evaluations/approvals/finalize` | `evaluation:finalize` | `Branch` | Branch Manager |

`evaluation:finalize` at `Branch` is held by `BRANCH_MANAGER`. `DEPT_MANAGER` receives 403 on the approve,
recalibrate and finalize endpoints.

Removed endpoints (not present in the controller): `POST /evaluations/send-to-employee`,
`POST /evaluations/personal/confirm`, `POST /evaluations/personal/acknowledge`, `POST /evaluations/lock-score`.
The BLL methods `SubmitEvaluationToBranchAsync` and `ReturnEvaluationToCalculatedAsync` (`EvaluationService.cs`) have no
controller route; they are not reachable over HTTP and are left unchanged (out of scope, see the report).

## Notifications

| Event | Recipient | Type |
| --- | --- | --- |
| `DRAFT` -> `CALCULATED` | Employee (`IMPORTANT`) | `EVALUATION_CALCULATED`, `relatedType = EVALUATION_PERIOD` |

See `NOTIFICATION_CONTRACT.md` for the other evaluation notifications.
