# API Contract & Error Mapping — Submission / Verification / KPI Result

> Jira: GB-252. Nguồn chuẩn cho BE (GB-247, GB-248, GB-251) và FE (GB-208, GB-210).
> Mọi thay đổi route / mã lỗi phải cập nhật file này.

## 1. Quy ước chung

### 1.1 Envelope
Mọi response (thành công **và** lỗi, kể cả 400 model-validation, 401/403 từ JWT/policy) đều là `ApiResponse`:

```json
{
  "success": true,
  "status": 200,
  "message": "Nộp kết quả công việc thành công. Đang chờ thẩm định.",
  "data": { },
  "pagination": null,
  "errorCode": null,
  "errors": null,
  "title": null,
  "traceId": "0HN7..."
}
```

```json
{
  "success": false,
  "status": 409,
  "title": "MSG23",
  "errorCode": "MSG23",
  "message": "Truy cập bị từ chối: Tệp tài liệu minh chứng đã bị khóa vĩnh viễn sau khi phê duyệt.",
  "errors": null,
  "traceId": "0HN7..."
}
```

- `traceId` cũng được trả trong header `X-Trace-Id`.
- FE **phân nhánh theo `errorCode`**, không theo `message` (message có thể đổi câu chữ).
- `errors[]` chỉ có với lỗi validation, mỗi phần tử dạng `"field: message"`, hoặc chỉ `message` nếu lỗi không gắn với field nào.

### 1.2 Nguyên tắc
1. Service throw `AppException` hoặc lớp con; `GlobalExceptionHandler` map sang envelope. Controller không tự build `ApiResponse.Fail`.
2. Route: `/api/v1`, danh từ số nhiều, kebab-case. Mỗi endpoint có đúng một route chính tắc; route cũ là alias deprecated, bị ẩn khỏi Swagger.
3. Enum trả ra API dạng UPPER_SNAKE, khớp `[PgName]` trong DB (`SUBMITTED`, `UNDER_REVIEW`, `IN_PROGRESS`…), qua `EnumWireFormat.ToWire()`.
4. **409** dành cho xung đột trạng thái, **422** dành cho vi phạm business rule (BR-xx).

## 2. Routes

### 2.1 Result Submission
| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/api/v1/works/{workId}/submission-context` | — | 200 `SubmissionContextDto` |
| POST | `/api/v1/works/{workId}/submissions` | multipart: `comment`, `metricItems[]` hoặc `metricItemsJson`, `files[]` | 201 `SubmissionResultDto`, message `MSG06` |
| GET | `/api/v1/works/{workId}/submissions` | — | 200 `SubmissionDetailDto[]` |
| GET | `/api/v1/submissions/{submissionId}` | — | 200 `SubmissionDetailDto` |
| POST | `/api/v1/submissions/{submissionId}/withdraw` | — | 200 `SubmissionResultDto` |

### 2.2 Verification
| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/api/v1/verifications/{submissionId}/dossier` | — | 200 `VerificationDossierDto` |
| POST | `/api/v1/verifications/{submissionId}/decision` | `SubmitVerificationDecisionDto` | 200 `VerificationDecisionResultDto` |
| GET | `/api/v1/verifications/{submissionId}/history` | — | 200 `VerificationHistoryDto[]` |

`SubmitVerificationDecisionDto`:
```json
{
  "decision": "APPROVED | REVISION_REQUIRED | REJECTED",
  "verifiedValue": 15,
  "feedback": "bắt buộc >= 10 ký tự khi REVISION_REQUIRED/REJECTED",
  "resubmitDeadline": "2026-10-20T10:00:00+07:00 (bắt buộc khi REVISION_REQUIRED, phải > now)",
  "checklistResults": [{ "standardId": "STD-01", "isPassed": true }]
}
```

### 2.3 KPI Result *(chỉ contract — implement ở GB-251)*
| Method | Route | Query | Thành công |
|---|---|---|---|
| GET | `/api/v1/works/{workId}/kpi-results` | — | 200 `KpiResultDto[]` |
| GET | `/api/v1/kpi-results` | `employeeId, kpiDefinitionId, evaluationPeriodId, status, pageIndex, pageSize` | 200 `KpiResultDto[]` + `pagination` |

DTO: `backend/src/G54.BLL/Dtos/KpiResult/KpiResultDto.cs`. Một `KpiResultDto` tương ứng một `WorkResultItem` thuộc `VerifiedWorkResult`. `status` = `VALID | INVALID` (INVALID khi bị `RiskEvent` vô hiệu hóa, kèm `invalidatedAt`).

### 2.4 Alias deprecated (bị ẩn khỏi Swagger, xóa sau khi FE chuyển route)
| Alias | Route chính tắc |
|---|---|
| `GET /api/v1/works/{workId}/submissions/{submissionId}` | `GET /api/v1/submissions/{submissionId}` |
| `POST /api/v1/works/{workId}/submissions/{submissionId}/withdraw` | `POST /api/v1/submissions/{submissionId}/withdraw` |
| `GET /api/v1/verification/dossiers/{submissionId}` *(FE đang dùng)* | `GET /api/v1/verifications/{submissionId}/dossier` |
| `GET /api/v1/verifications/pending/{submissionId}` | `GET /api/v1/verifications/{submissionId}/dossier` |
| `POST /api/v1/verification/dossiers/{submissionId}/decision` *(FE đang dùng)* | `POST /api/v1/verifications/{submissionId}/decision` |
| `GET /api/v1/verification/submissions/{submissionId}/history` | `GET /api/v1/verifications/{submissionId}/history` |

## 3. Error mapping

### 3.1 Chung (mọi endpoint)
| Tình huống | HTTP | errorCode |
|---|---|---|
| Body/form sai kiểu, thiếu field, vi phạm DataAnnotation | 400 | `VALIDATION_ERROR` (+ `errors[]`) |
| Thiếu/hết hạn/bị revoke token, `sub` không hợp lệ, nhân viên không active | 401 | `UNAUTHORIZED` |
| Thiếu permission (policy) | 403 | `FORBIDDEN` |
| Lỗi không lường trước | 500 | `INTERNAL_ERROR` |

### 3.2 Submission
| Tình huống | HTTP | errorCode |
|---|---|---|
| Work / Submission không tồn tại | 404 | `NOT_FOUND` |
| Không phải người được giao (context, submit) | 403 | `FORBIDDEN` |
| Không phải người đã nộp (withdraw) | 403 | `FORBIDDEN` |
| Work đang `SUBMITTED / VERIFIED / DONE / CANCELLED` khi nộp | 409 | `SUBMISSION_INVALID_STATE` |
| Withdraw khi submission khác `SUBMITTED` | 409 | `SUBMISSION_INVALID_STATE` |
| Thiếu KPI bắt buộc, giá trị âm, mẫu số RATIO ≤ 0, file sai định dạng/quá 25MB/quá 10 file | 400 | `VALIDATION_ERROR` (+ `errors[]`) |
| *(dự phòng — BR-12)* Nộp trong 5 phút gia hạn | 201 | message `MSG17` |
| *(dự phòng — BR-12)* Nộp quá hạn | 422 | `MSG18` |
| *(dự phòng — BR-08)* Task đã đóng góp KPI | 409 | `MSG22` |

### 3.3 Verification
| Tình huống | HTTP | errorCode |
|---|---|---|
| Submission không tồn tại (dossier, decision, history) | 404 | `NOT_FOUND` |
| `decision` ngoài `APPROVED / REVISION_REQUIRED / REJECTED`; feedback < 10 ký tự; thiếu/sai `resubmitDeadline`; `verifiedValue` âm | 400 | `VALIDATION_ERROR` |
| Người thẩm định chính là người nộp (BR-02) | 422 | `MSG10` |
| Submission đã `APPROVED` — khóa vĩnh viễn (BR-13) | 409 | `MSG23` |
| Submission ở `DRAFT / REJECTED / WITHDRAWN` | 409 | `VERIFICATION_INVALID_STATE` |

### 3.4 KPI Result
| Tình huống | HTTP | errorCode |
|---|---|---|
| Work không tồn tại | 404 | `NOT_FOUND` |
| Work chưa có verified result | 404 | `KPI_RESULT_NOT_FOUND` |
| `status` / paging không hợp lệ | 400 | `VALIDATION_ERROR` |
| Result bị invalidate | 200 | — (`status = INVALID`) |

## 4. Mapping enum

| API (`decision`) | DB `verification_decision` | Submission status sau quyết định | Work status sau quyết định |
|---|---|---|---|
| `APPROVED` | `APPROVED` | `APPROVED` | `VERIFIED` |
| `REVISION_REQUIRED` | `NEED_MORE_INFO` | `UNDER_REVIEW` | `IN_PROGRESS` |
| `REJECTED` | `REJECTED` | `REJECTED` | `REJECTED` |
| `PENDING` *(chỉ xuất hiện trong history)* | `PENDING` | — | — |

Các trường status khác (`SubmissionDetailDto.status`, `SubmissionContextDto.workStatus`, `aggregationType`, `direction`, `VerificationDossierDto.status`) trả đúng giá trị `PgName`: `SUBMITTED`, `UNDER_REVIEW`, `WITHDRAWN`, `IN_PROGRESS`, `HIGHER_BETTER`…

## 5. Mã lỗi
Định nghĩa tại `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs` (message VI/EN qua `ErrorCodes.GetDefaultMessage`). Mã mới trong GB-252: `SUBMISSION_INVALID_STATE`, `VERIFICATION_INVALID_STATE`, `KPI_RESULT_NOT_FOUND`.
