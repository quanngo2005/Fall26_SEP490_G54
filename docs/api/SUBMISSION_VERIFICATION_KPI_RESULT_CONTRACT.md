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
  "title": "EVIDENCE_LOCKED",
  "errorCode": "EVIDENCE_LOCKED",
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
2. Route tại root, không prefix API hoặc phiên bản, danh từ số nhiều, kebab-case. Mỗi endpoint có đúng một route chính tắc; alias deprecated bị ẩn khỏi Swagger.
3. Enum trả ra API dạng UPPER_SNAKE, khớp `[PgName]` trong DB (`SUBMITTED`, `UNDER_REVIEW`, `IN_PROGRESS`…), qua `EnumApiFormat.ToEnumString()`.
4. **409** dành cho xung đột trạng thái, **422** dành cho vi phạm business rule (BR-xx).

## 2. Routes

### 2.1 Result Submission
| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/works/{workId}/submission-context` | — | 200 `SubmissionContextDto` |
| POST | `/works/{workId}/submissions` | multipart: `comment`, `metricItems[]` hoặc `metricItemsJson`, `files[]` | 201 `SubmissionResultDto`, message `WORK_RESULT_SUBMITTED` (`SuccessMessages`) |
| GET | `/works/{workId}/submissions` | — | 200 `SubmissionDetailDto[]` |
| GET | `/submissions/{submissionId}` | — | 200 `SubmissionDetailDto` |
| POST | `/submissions/{submissionId}/withdraw` | — | 200 `SubmissionResultDto` |

### 2.2 Verification
| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/verifications/{submissionId}/dossier` | — | 200 `VerificationDossierDto` |
| POST | `/verifications/{submissionId}/decision` | `SubmitVerificationDecisionDto` | 200 `VerificationDecisionResultDto` |
| GET | `/verifications/{submissionId}/history` | — | 200 `VerificationHistoryDto[]` |

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

#### Ghi nhận KPI khi `APPROVED` (không đổi route hay DTO)
Khi duyệt `APPROVED` kèm `verifiedValue` và công việc có KPI của loại công việc, hệ thống chọn bản ghi
`WorkTypeKpi` **đang hiệu lực tại ngày hiện tại** (múi giờ nghiệp vụ): `EffectiveFrom <= hôm nay &&
(EffectiveTo == null || EffectiveTo >= hôm nay)`, nếu có nhiều bản ghi khớp thì lấy `EffectiveFrom`
lớn nhất. KPI và hệ số (`multiplier`, BR-48 Task Multiplier) đến từ bản ghi đó. Không có bản ghi nào
đang hiệu lực (hết hạn hoặc chưa tới ngày bắt đầu) thì hành vi giữ như không có KPI: duyệt bình
thường, không tạo `WorkResultItem`, không cộng tiến độ. Hệ thống tạo `WorkResultItem` cho KPI đó, ghi
vào target ACTIVE của từng nhân sự:

| Công việc có bàn giao `ACCEPTED`? | Người nhận điểm | Tỷ lệ |
|---|---|---|
| Không | Người nộp | 100% |
| Có (lấy bản ghi mới nhất theo `acceptedAt`, nếu trống thì `handoverAt`) | Người giao (`fromEmployeeId`, người giới thiệu) | `referrerSharePercent` của bản bàn giao (mặc định cột DB là 30%; bản bàn giao tạo qua API có giá trị 0 nếu Manager/Admin không đặt, xem `HANDOVER_CONTRACT.md`) |
| | Người nhận (`toEmployeeId`, người xử lý) | `100 - referrerSharePercent` (mặc định 70%) |

- Tỷ lệ được lưu theo từng bản bàn giao ở cột `task_handover.referrer_share_percent` (`numeric(7,4)`, 0-100, mặc định cột 30, ràng buộc `ck_task_handover_referrer_share`), không còn là hằng số trong code.
- Phần của người giới thiệu được làm tròn 4 chữ số thập phân, người xử lý nhận phần còn lại nên tổng hai phần luôn bằng `verifiedValue` (đây là phần **raw**, trước khi nhân hệ số).
- Mỗi `WorkResultItem` lưu `rawValue` (phần raw của người nhận, sau khi chia bàn giao nếu có) và
  `multiplierApplied` (hệ số của mapping đang hiệu lực tại thời điểm duyệt). `value = round(rawValue ×
  multiplierApplied, 4, ToEven)` và chính `value` (đã nhân hệ số) được cộng vào `ActualProgress`. Đổi
  hệ số sau đó (bản ghi mới hoặc sửa tại chỗ) không làm thay đổi các `WorkResultItem`/`ActualProgress`
  đã ghi trước đó.
- Nếu công việc có KPI và có `verifiedValue`, **mọi** nhân sự nhận điểm đều phải có target `ACTIVE` cho KPI đó, với kỳ đánh giá chứa ngày hiện tại (múi giờ nghiệp vụ). Thiếu target của bất kỳ ai thì toàn bộ yêu cầu bị từ chối với 409 `KPI_TARGET_NOT_ASSIGNED`, message nêu tên và mã nhân sự thiếu target; không ghi `Verification`, `VerifiedWorkResult` hay `WorkResultItem`, submission và công việc giữ nguyên trạng thái để duyệt lại sau khi đã giao target.
- Công việc không có KPI trong loại công việc, hoặc duyệt không kèm `verifiedValue`, vẫn được duyệt bình thường và không ghi `WorkResultItem`.
- Target đã đạt hoặc vượt chỉ tiêu mà vẫn `ACTIVE` thì vẫn nhận điểm; không có giới hạn trên (tỷ lệ hoàn thành bị chặn bởi `MaxAchievementPercent` của KPI khi tính điểm đánh giá).
- Mỗi công việc (Task ID) chỉ được đóng góp KPI một lần. Nếu công việc đã có `WorkResultItem`, yêu cầu bị từ chối với 409 `KPI_ALREADY_CONTRIBUTED` và không ghi nhận gì thêm.

### 2.2.1 Evidence (file bằng chứng)
| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/evidence/{id}/download` | — | 200 file, `Content-Disposition: attachment` |
| GET | `/evidence/{id}/preview` | — | 200 file; `inline` + `Content-Security-Policy: sandbox` cho PDF/ảnh, các loại khác trả như download |

Hai URL này đã nằm trong `EvidenceItemDto` (`previewUrl`, `downloadUrl`) của dossier. Cả hai yêu cầu `Authorization: Bearer`, nên FE phải tải bằng HttpClient rồi tạo blob; không dùng `<a href>` trực tiếp.

Ai được xem: người đã nộp (với evidence của appeal là người đã gửi khiếu nại); người có quyền `verification:decide` với phạm vi (`Dept` / `Branch` / `All`) bao phủ phòng ban của người nộp; ADMIN. Evidence gắn với risk event chỉ mở cho phạm vi `All`. Phản hồi luôn có `Cache-Control: private, no-store`.

Quy tắc file khi nộp: tối đa 10 file, mỗi file ≤ 25 MB; đuôi thuộc `.pdf .xlsx .xls .docx .doc .png .jpg .jpeg`; nội dung phải khớp đuôi (kiểm tra chữ ký file: `%PDF`, PNG, JPEG, ZIP cho `.xlsx/.docx`, OLE cho `.xls/.doc`); tên file ≤ 255 ký tự. `mimeType` lưu theo đuôi đã xác thực, không theo `Content-Type` của client. Một file sai thì không file nào của lần nộp đó được lưu. Request `POST /works/{workId}/submissions` cho phép tối đa 251 MB (10 × 25 MB + phần form), giới hạn này chỉ áp cho route đó.

Lưu trữ: thư mục `Storage:EvidencePath` (Docker: `/app/data/evidence`, volume `evidence_data`). `Evidence.FilePath` chỉ chứa tên file lưu trữ, không chứa thư mục.

### 2.3 KPI Result *(chỉ contract — implement ở GB-251)*
| Method | Route | Query | Thành công |
|---|---|---|---|
| GET | `/works/{workId}/kpi-results` | — | 200 `KpiResultDto[]` |
| GET | `/kpi-results` | `employeeId, kpiDefinitionId, evaluationPeriodId, status, pageIndex, pageSize` | 200 `KpiResultDto[]` + `pagination` |

DTO: `backend/src/G54.BLL/Dtos/KpiResult/KpiResultDto.cs`. Một `KpiResultDto` tương ứng một `WorkResultItem` thuộc `VerifiedWorkResult`. `status` = `VALID | INVALID` (INVALID khi bị `RiskEvent` vô hiệu hóa, kèm `invalidatedAt`). `value` là điểm đã nhân hệ số (những gì cộng vào `ActualProgress`); `rawValue` và `multiplierApplied` cho biết điểm gốc và hệ số đã dùng tại thời điểm duyệt, để truy xuất loại công việc → hệ số → điểm.

### 2.4 Alias deprecated (bị ẩn khỏi Swagger, xóa sau khi FE chuyển route)
| Alias | Route chính tắc |
|---|---|
| `GET /works/{workId}/submissions/{submissionId}` | `GET /submissions/{submissionId}` |
| `POST /works/{workId}/submissions/{submissionId}/withdraw` | `POST /submissions/{submissionId}/withdraw` |
| `GET /verification/dossiers/{submissionId}` | `GET /verifications/{submissionId}/dossier` |
| `GET /verifications/pending/{submissionId}` | `GET /verifications/{submissionId}/dossier` |
| `POST /verification/dossiers/{submissionId}/decision` | `POST /verifications/{submissionId}/decision` |
| `GET /verification/submissions/{submissionId}/history` | `GET /verifications/{submissionId}/history` |

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
| Evidence: không phải người nộp, không có phạm vi verification bao phủ, không phải ADMIN | 403 | `FORBIDDEN` |
| Evidence không tồn tại hoặc file không còn trên máy chủ | 404 | `NOT_FOUND` |
| Work đang `SUBMITTED / VERIFIED / DONE / CANCELLED` khi nộp | 409 | `SUBMISSION_INVALID_STATE` |
| Withdraw khi submission khác `SUBMITTED` | 409 | `SUBMISSION_INVALID_STATE` |
| Thiếu KPI bắt buộc, giá trị âm, mẫu số RATIO ≤ 0, file sai định dạng/nội dung không khớp đuôi/quá 25MB/quá 10 file/tên quá 255 ký tự | 400 | `VALIDATION_ERROR` (+ `errors[]`) |
| *(dự phòng — BR-12)* Nộp trong 5 phút gia hạn | 201 | message `LATE_SUBMISSION_ACCEPTED` (`SuccessMessages`) |
| *(dự phòng — BR-12)* Nộp quá hạn | 422 | `CAMPAIGN_DEADLINE_EXPIRED` |

### 3.3 Verification
| Tình huống | HTTP | errorCode |
|---|---|---|
| Submission không tồn tại (dossier, decision, history) | 404 | `NOT_FOUND` |
| `decision` ngoài `APPROVED / REVISION_REQUIRED / REJECTED`; feedback < 10 ký tự; thiếu/sai `resubmitDeadline`; `verifiedValue` âm | 400 | `VALIDATION_ERROR` |
| Người thẩm định chính là người nộp (BR-02) | 422 | `MAKER_CANNOT_BE_CHECKER` |
| Submission đã `APPROVED` — khóa vĩnh viễn (BR-13) | 409 | `EVIDENCE_LOCKED` |
| Submission ở `DRAFT / REJECTED / WITHDRAWN` | 409 | `VERIFICATION_INVALID_STATE` |
| Duyệt `APPROVED` cho công việc (Task ID) đã đóng góp KPI trước đó (BR-08) | 409 | `KPI_ALREADY_CONTRIBUTED` |
| Duyệt `APPROVED` mà kỳ đánh giá của chỉ tiêu đang áp dụng đã `LOCKED` / `CLOSED` (xem `PERIOD_LOCK_CONTRACT.md`) | 409 | `PERIOD_LOCKED` |
| Duyệt `APPROVED` có KPI và `verifiedValue` nhưng một nhân sự nhận điểm chưa có target `ACTIVE` trong kỳ hiện tại | 409 | `KPI_TARGET_NOT_ASSIGNED` |

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
Định nghĩa tại `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs` (message VI/EN qua `ErrorCodes.GetDefaultMessage`). Mã mới trong GB-252: `SUBMISSION_INVALID_STATE`, `VERIFICATION_INVALID_STATE`, `KPI_RESULT_NOT_FOUND`. Mã bổ sung sau đó: `KPI_TARGET_NOT_ASSIGNED`.
