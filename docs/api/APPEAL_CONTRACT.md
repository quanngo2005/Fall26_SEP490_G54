# API Contract & Error Mapping — Appeal (Khiếu nại kết quả công việc bị từ chối)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: các endpoint dưới đây **đã implement ở BE**, FE chưa có.

## 1. Phạm vi

- Nhân viên khiếu nại một **đợt nộp kết quả (`ResultSubmission`) đã bị từ chối** (`REJECTED`).
- Khiếu nại đánh giá KPI (`performanceEvaluationId`) **chưa nằm trong phạm vi**: các endpoint dưới đây chỉ làm việc với appeal gắn với đợt nộp kết quả; appeal không gắn với đợt nộp không được trả ra và không được rút.
- Chưa có endpoint xử lý (nhận / từ chối) khiếu nại.
- Điểm đánh giá của kỳ liên quan chuyển sang `Disputed_Pending` khi nộp khiếu nại và được khôi phục khi rút (mục 2.5, BR-31).

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md` và mục 1 của `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`.

## 2. Routes

| Method | Route | Body / Query | Thành công |
|---|---|---|---|
| POST | `/submissions/{submissionId}/appeals` | multipart: `reason`, `files[]` | 201 `AppealDetailDto` + header `Location: /appeals/{id}`, message `CREATED` (`SuccessMessages`) |
| POST | `/appeals/{id}/withdraw` | — | 200 `AppealDetailDto` |
| GET | `/appeals` | query `status`, `page`, `pageSize` | 200 `PagedResult<AppealListItemDto>` |
| GET | `/appeals/{id}` | — | 200 `AppealDetailDto` |

Tất cả yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động.

### 2.1 Nộp khiếu nại — `POST /submissions/{submissionId}/appeals`

Form fields:
- `reason` (bắt buộc, sau khi cắt khoảng trắng đầu/cuối: 1–2000 ký tự).
- `files[]` (tùy chọn): tối đa 10 file, mỗi file ≤ 25 MB, cùng quy tắc đuôi file và kiểm tra nội dung như bằng chứng của đợt nộp (xem mục 2.2.1 của contract Submission). Một file sai thì không file nào được lưu và không tạo khiếu nại. Giới hạn kích thước request giống `POST /works/{workId}/submissions`.

Quy tắc:
1. Chỉ **người đã nộp** đợt nộp đó được khiếu nại.
2. Đợt nộp phải đang `REJECTED`.
3. Phải còn trong **48 giờ** kể từ thời điểm bị từ chối (`verifiedAt` của lần thẩm định `REJECTED` mới nhất; nếu trống thì `updatedAt` của đợt nộp). Tính theo giờ máy chủ; đúng 48:00:00 vẫn còn hợp lệ, quá một giây là hết hạn.
4. Đợt nộp không được có khiếu nại đang `OPEN` hoặc `UNDER_REVIEW`. Khiếu nại đã `WITHDRAWN` không chặn việc nộp khiếu nại mới (vẫn phải trong 48 giờ).

Appeal mới có `status = OPEN`, `filedAt` = giờ máy chủ.

### 2.2 Rút khiếu nại — `POST /appeals/{id}/withdraw`

- Chỉ **người đã nộp** khiếu nại được rút, và chỉ khi `status = OPEN`.
- Sau khi rút: `status = WITHDRAWN`. `decidedAt` giữ `null` vì rút không phải quyết định xử lý.

### 2.3 Danh sách — `GET /appeals`

Query: `status` (`OPEN | UNDER_REVIEW | ACCEPTED | REJECTED | WITHDRAWN`, không phân biệt hoa thường, bỏ trống là tất cả), `page` (mặc định 1), `pageSize` (mặc định 20, kẹp 1..100). Sắp xếp `filedAt` giảm dần, rồi `id`.

Phạm vi nhìn thấy (theo quyền `verification:decide` của người gọi, cùng cách tính với evidence):
| Quyền `verification:decide` | Thấy |
|---|---|
| Không có hoặc `Own` | Chỉ khiếu nại của mình |
| `Dept` | Của mình và của nhân sự thuộc phòng ban của mình (gồm phòng ban con) |
| `Branch` | Của mình và của nhân sự thuộc chi nhánh gần nhất của mình (gồm phòng ban con) |
| `All` hoặc ADMIN | Tất cả |

### 2.4 Chi tiết — `GET /appeals/{id}`

Cùng phạm vi như danh sách. Ngoài phạm vi: 403. Không tồn tại: 404.

### 2.5 Điểm `Disputed_Pending` (BR-31)

Khi nộp / rút khiếu nại, điểm đánh giá (`performance_evaluation`) của **chính người nộp** trong kỳ chứa ngày nộp đợt kết quả (`submittedAt`, tính theo giờ UTC+7, mọi kỳ có ngày đó nằm trong `[startDate, endDate]`, không phân biệt loại kỳ) thay đổi như sau. `Disputed_Pending` được lưu bằng trạng thái `DISPUTED` của `performance_evaluation_status`; trạng thái trước đó nằm ở cột `pre_dispute_status`, thời điểm ở `disputed_at`.

| Sự kiện | Điểm đánh giá |
|---|---|
| Nộp khiếu nại (201) | `CALCULATED` / `IN_REVIEW` / `APPROVED` → `DISPUTED`; `pre_dispute_status` = trạng thái cũ; `total_score` giữ nguyên |
| Nộp khiếu nại | `DRAFT`, `FINAL`, `DISPUTED` hoặc kỳ `LOCKED` / `CLOSED`: không đổi |
| Rút khiếu nại (200) | `DISPUTED` → `pre_dispute_status` (mặc định `CALCULATED`), xóa `pre_dispute_status` / `disputed_at` |
| Rút khiếu nại khi còn khiếu nại `OPEN` / `UNDER_REVIEW` khác của người đó trong cùng kỳ | Giữ `DISPUTED` |

Thay đổi nằm trong cùng lần lưu với khiếu nại. Điểm `DISPUTED` bị ẩn khỏi bảng xếp hạng, hạn ngạch Tier A, đường cong chuông, điểm trung bình và số người đã được đánh giá (xem `CALIBRATION_APPROVAL_CONTRACT.md` và `PERFORMANCE_DASHBOARD_CONTRACT.md`), đồng thời chặn khóa kỳ (`EVALUATION_DISPUTE_PENDING`).

Lưu form đánh giá (`POST /evaluations/submit`) cho điểm `DISPUTED` trả 409 `EVALUATION_DISPUTE_PENDING` để không vô tình xóa trạng thái tranh chấp.

Chưa có endpoint nhận / từ chối khiếu nại nên chưa có bước đưa điểm ra khỏi `DISPUTED` khi khiếu nại được xử lý.

## 3. DTO

`AppealListItemDto`:
```json
{
  "id": "uuid",
  "resultSubmissionId": "uuid",
  "workId": "uuid",
  "workCode": "TSK-...",
  "workTitle": "string",
  "submissionNo": 1,
  "employeeId": "uuid",
  "employeeCode": "NV001",
  "employeeName": "string",
  "status": "OPEN",
  "filedAt": "2026-10-24T10:00:00+00:00",
  "decidedAt": null,
  "updatedAt": "2026-10-24T10:00:00+00:00"
}
```

`AppealDetailDto` = các trường của `AppealListItemDto` cộng:
```json
{
  "reason": "string",
  "note": "string | null",
  "resolvedByEmployeeId": "uuid | null",
  "resolvedByEmployeeName": "string | null",
  "evidences": [
    {
      "id": "uuid",
      "fileName": "string",
      "fileSize": 1024,
      "mimeType": "application/pdf",
      "previewUrl": "/evidence/{id}/preview",
      "downloadUrl": "/evidence/{id}/download"
    }
  ]
}
```

File bằng chứng của khiếu nại tải qua `GET /evidence/{id}/download` và `/preview` (mục 2.2.1 contract Submission). Xem được: người nộp khiếu nại; người có `verification:decide` với phạm vi bao phủ phòng ban của người nộp khiếu nại; ADMIN.

## 4. Error mapping

| Tình huống | HTTP | errorCode |
|---|---|---|
| `reason` trống hoặc > 2000 ký tự; > 10 file; file sai định dạng / quá 25 MB / nội dung không khớp đuôi; `status` query không hợp lệ | 400 | `VALIDATION_ERROR` |
| Thiếu / hết hạn token, nhân viên không active | 401 | `UNAUTHORIZED` |
| Nộp / rút khiếu nại không phải của mình; xem khiếu nại ngoài phạm vi | 403 | `FORBIDDEN` |
| Đợt nộp / khiếu nại không tồn tại | 404 | `NOT_FOUND` |
| Đợt nộp không ở `REJECTED` khi nộp khiếu nại; rút khiếu nại không còn `OPEN` | 409 | `APPEAL_INVALID_STATE` |
| Đợt nộp đã có khiếu nại `OPEN` / `UNDER_REVIEW` | 409 | `APPEAL_ALREADY_OPEN` |
| Quá 48 giờ kể từ khi bị từ chối | 422 | `APPEAL_WINDOW_EXPIRED` |

Mã lỗi định nghĩa tại `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs` (message VI/EN qua `ErrorCodes.GetDefaultMessage`).

## 5. Ghi chú triển khai

- Quy tắc "một khiếu nại đang xử lý trên mỗi đợt nộp" do service kiểm tra, chưa có ràng buộc ở database. Hai yêu cầu đồng thời cho cùng một đợt nộp có thể cùng được chấp nhận.
- Audit nộp / rút khiếu nại hiện ghi qua log có cấu trúc (giống các service khác), chưa có bảng audit bền vững.
