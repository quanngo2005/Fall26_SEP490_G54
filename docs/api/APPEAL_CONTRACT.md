# API Contract & Error Mapping — Appeal (Khiếu nại kết quả công việc bị từ chối)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: các endpoint dưới đây **đã implement ở BE**, FE chưa có.

## 1. Phạm vi

- Nhân viên khiếu nại một **đợt nộp kết quả (`ResultSubmission`) đã bị từ chối** (`REJECTED`).
- Khiếu nại đánh giá KPI (`performanceEvaluationId`) **chưa nằm trong phạm vi**: các endpoint dưới đây chỉ làm việc với appeal gắn với đợt nộp kết quả; appeal không gắn với đợt nộp không được trả ra và không được rút.
- Xử lý khiếu nại bằng `ACCEPT / ADJUST / REJECT`.
- Điểm đánh giá của kỳ liên quan chuyển sang `Disputed_Pending` khi nộp khiếu nại và được khôi phục khi rút (mục 2.6, BR-31). Xử lý khiếu nại (`/decision`) chưa đưa điểm ra khỏi `Disputed_Pending`.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md` và mục 1 của `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`.

## 2. Routes

| Method | Route | Body / Query | Thành công |
|---|---|---|---|
| POST | `/submissions/{submissionId}/appeals` | multipart: `reason`, `files[]` | 201 `AppealDetailDto` + header `Location: /appeals/{id}`, message `CREATED` (`SuccessMessages`) |
| POST | `/appeals/{id}/withdraw` | — | 200 `AppealDetailDto` |
| POST | `/appeals/{id}/decision` | JSON `DecideAppealRequestDto` | 200 `AppealDecisionResultDto` |
| GET | `/appeals` | query `status`, `page`, `pageSize` | 200 `PagedResult<AppealListItemDto>` |
| GET | `/appeals/{id}` | — | 200 `AppealDetailDto` |
| POST | `/appeals/{id}/review` | JSON `ReviewAppealRequestDto` | 200 `ScoreEntryDto` (tạm giữ điểm, mục 2.6) |
| GET | `/appeals/{id}/score-entries` | — | 200 `ScoreEntryDto[]` |
| POST | `/appeals/{id}/score-dispute` | JSON `ScoreDisputeRequestDto` | 200 `AppealDetailDto` |
| POST | `/works/{id}/score-deductions` | JSON `ReviewAppealRequestDto` | 200 `ScoreEntryDto` |
| GET / PUT | `/system-parameters/appeal-score-hold` | PUT: JSON `{ "defaultPoints": 5 }` | 200 `{ "defaultPoints": 5 }` (chỉ ADMIN) |

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
- Nếu rút đồng thời với xử lý quyết định, chỉ một thao tác được commit; thao tác còn lại trả 409 `APPEAL_INVALID_STATE`.

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

### 2.5 Xử lý — `POST /appeals/{id}/decision`

Yêu cầu quyền `verification:decide` với scope `Dept / Branch / All` bao phủ phòng ban người nộp; ADMIN được xử lý. Không được xử lý appeal của mình hoặc submission do mình nộp (422 `MAKER_CANNOT_BE_CHECKER`).

```json
{ "decision": "ADJUST", "note": "Bổ sung chứng từ đối soát có chữ ký xác nhận.", "verifiedValue": null }
```

- `decision`: bắt buộc `ACCEPT | ADJUST | REJECT`, không phân biệt hoa thường, trim khoảng trắng.
- `note`: tối đa 2000 ký tự sau trim; bắt buộc 10–2000 ký tự với `ADJUST / REJECT`, tùy chọn với `ACCEPT`.
- `verifiedValue`: decimal tùy chọn, không âm, chỉ được gửi với `ACCEPT`; cùng cách ghi nhận KPI như duyệt submission thông thường. Bỏ trống vẫn tạo verified result nhưng không cộng sản lượng KPI.
- Chỉ xử lý appeal `OPEN / UNDER_REVIEW`, gắn với submission `REJECTED` mới nhất của work đang `REJECTED`. Appeal đã kết thúc, submission đã đổi trạng thái hoặc có lần nộp mới: 409 `APPEAL_INVALID_STATE`.
- `ACCEPT`: appeal `ACCEPTED`, submission `APPROVED`, work `VERIFIED`; thêm verification `APPROVED` và verified result, cộng KPI khi có sản lượng và target phù hợp.
- `ADJUST`: appeal `ACCEPTED`, submission `UNDER_REVIEW`, work `IN_PROGRESS`; thêm verification `NEED_MORE_INFO` (API history: `REVISION_REQUIRED`) với yêu cầu bổ sung trong `note`. Chưa duyệt submission và chưa ghi nhận KPI. Nhân viên bổ sung/nộp lại theo luồng submission hiện có.
- `REJECT`: appeal `REJECTED`, submission/work giữ `REJECTED`; không thêm verification, không thay đổi thời điểm từ chối submission.
- Cả ba lưu `note`, người xử lý và thời gian máy chủ vào `resolvedByEmployeeId`, `decidedAt`, `updatedAt`. Quyết định và các thay đổi liên quan lưu nguyên tử; xung đột xử lý đồng thời trả 409 `APPEAL_INVALID_STATE`.

Response `data` (`AppealDecisionResultDto`):
```json
{
  "appeal": { "id": "uuid", "status": "ACCEPTED", "note": "Bổ sung chứng từ đối soát có chữ ký xác nhận.", "...": "các trường AppealDetailDto" },
  "decision": "ADJUST",
  "submissionStatus": "UNDER_REVIEW",
  "workStatus": "IN_PROGRESS",
  "verificationId": "uuid"
}
```
`verificationId` là UUID với `ACCEPT / ADJUST`, `null` với `REJECT`. Envelope chuẩn `ApiResponse`, HTTP/status 200.

### 2.6 Điểm `Disputed_Pending` (BR-31)

Khi nộp / rút khiếu nại, điểm đánh giá (`performance_evaluation`) của **chính người nộp** trong kỳ chứa ngày nộp đợt kết quả (`submittedAt`, tính theo giờ UTC+7, mọi kỳ có ngày đó nằm trong `[startDate, endDate]`, không phân biệt loại kỳ) thay đổi như sau. `Disputed_Pending` được lưu bằng trạng thái `DISPUTED` của `performance_evaluation_status`; trạng thái trước đó nằm ở cột `pre_dispute_status`, thời điểm ở `disputed_at`.

| Sự kiện | Điểm đánh giá |
|---|---|
| Nộp khiếu nại (201) | `CALCULATED` / `IN_REVIEW` / `APPROVED` → `DISPUTED`; `pre_dispute_status` = trạng thái cũ; `total_score` giữ nguyên |
| Nộp khiếu nại | `DRAFT`, `FINAL`, `DISPUTED` hoặc kỳ `LOCKED` / `CLOSED`: không đổi |
| Rút khiếu nại (200) | `DISPUTED` → `pre_dispute_status` (mặc định `CALCULATED`), xóa `pre_dispute_status` / `disputed_at` |
| Rút khiếu nại khi còn khiếu nại `OPEN` / `UNDER_REVIEW` khác của người đó trong cùng kỳ | Giữ `DISPUTED` |

Thay đổi nằm trong cùng lần lưu với khiếu nại. Điểm `DISPUTED` bị ẩn khỏi bảng xếp hạng, hạn ngạch Tier A, đường cong chuông, điểm trung bình và số người đã được đánh giá (xem `CALIBRATION_APPROVAL_CONTRACT.md` và `PERFORMANCE_DASHBOARD_CONTRACT.md`), đồng thời chặn khóa kỳ (`EVALUATION_DISPUTE_PENDING`).

Lưu form đánh giá (`POST /evaluations/submit`) cho điểm `DISPUTED` trả 409 `EVALUATION_DISPUTE_PENDING` để không vô tình xóa trạng thái tranh chấp.

Xử lý khiếu nại (mục 2.5) hiện chưa đưa điểm ra khỏi `DISPUTED`; điểm chỉ quay lại khi nhân viên rút khiếu nại.
### 2.7 Sổ điểm: tạm giữ và hoàn điểm (`ScoreEntry`)

Khi xét khiếu nại, điểm KPI hiện tại của nhân viên có thể bị **tạm giữ**, và được **hoàn** bằng một bút toán đảo (reversal) khi khiếu nại được chấp nhận. Sổ điểm chỉ ghi thêm, không sửa hay xóa dòng cũ; điểm đánh giá = điểm tính từ kết quả + tổng `points` của sổ.

Một dòng sổ (`ScoreEntryDto`), trong `ApiResponse`:
```json
{
  "id": "uuid",
  "appealId": "uuid | null",
  "workId": "uuid | null",
  "evaluationId": "uuid",
  "points": -5,
  "kind": "HOLD",
  "source": "MANAGER",
  "reason": "string",
  "recordedByEmployeeId": "uuid",
  "createdAt": "2026-10-27T10:00:00+00:00",
  "reversesEntryId": "uuid | null"
}
```

| `kind` | Dấu `points` | Ý nghĩa | `appealId` / `workId` / `reversesEntryId` |
|---|---|---|---|
| `HOLD` | âm | Tạm giữ khi bắt đầu xét khiếu nại | có `appealId` |
| `TASK_DEDUCTION` | âm | Trừ điểm theo công việc | có `workId`, không có `appealId` |
| `REFUND` | dương | Hoàn khoản `HOLD` (bút toán đảo) | có `appealId` và `reversesEntryId` = dòng `HOLD` được hoàn |

`source`: `MANAGER` (người xử lý nhập điểm), `TASK_HISTORY` (lấy từ tổng điểm đã trừ theo công việc), `SYSTEM` (điểm mặc định cấu hình), `REVERSAL` (dòng `REFUND`). Các ràng buộc trên được database kiểm tra (check constraint); mỗi khiếu nại tối đa một `HOLD` và mỗi `HOLD` tối đa một `REFUND`.

**`POST /appeals/{id}/review`** (tạm giữ): quyền `verification:decide` Dept / Branch / All, phạm vi phải bao phủ người nộp khiếu nại và chủ đợt nộp, không tự xử lý của mình. Body `{ "points": 5, "reason": "..." }`: `points` tùy chọn, > 0, ≤ 1000, tối đa 4 chữ số thập phân; `reason` bắt buộc 10–2000 ký tự. Khiếu nại `OPEN` → `UNDER_REVIEW`.
- Số điểm giữ, theo thứ tự ưu tiên: `points` do người xử lý nhập → tổng điểm đã bị trừ theo công việc của nhân viên (`TASK_DEDUCTION`, không tính lại vào điểm) → điểm mặc định trong cấu hình (ban đầu 5).
- Áp vào đánh giá có điểm, thuộc kỳ `OPEN` / `IN_REVIEW`, có `startDate` muộn nhất trong kỳ hiện tại. Không có đánh giá phù hợp: 409. Điểm hiện tại nhỏ hơn số điểm giữ thì tổng điểm **được phép âm** (không cắt về 0, không từ chối).

**Hoàn điểm khi khiếu nại được chấp nhận:**
- `POST /appeals/{id}/decision` với `ACCEPT` ghi một dòng `REFUND` (`points` = đối của `HOLD`, `reversesEntryId` = id `HOLD`, `source = REVERSAL`) trong cùng giao dịch với quyết định. Chỉ hoàn một lần: khoản `HOLD` đã có dòng đảo thì không ghi thêm.
- `ADJUST`: giữ nguyên `HOLD` cho đến khi đợt nộp được sửa và duyệt `APPROVED` qua `POST /verifications/...`, lúc đó mới ghi `REFUND`. `REJECT`: giữ nguyên khoản giữ, không trừ lần hai.
- Không sửa đánh giá của kỳ đã đóng. Kỳ chứa `HOLD` đã `LOCKED` / `CLOSED` hoặc đánh giá đã `APPROVED` / `FINAL`: khoản `HOLD` ở lại kỳ đó, còn dòng `REFUND` được ghi vào đánh giá hiện tại của nhân viên (kỳ đang mở chứa ngày hôm nay, cùng quy tắc chọn như khi giữ điểm) và cộng vào KPI của kỳ đó. Không có đánh giá hiện tại: 409 và quyết định bị hoàn tác cùng lúc.

**`GET /appeals/{id}/score-entries`**: cùng phạm vi nhìn thấy với chi tiết khiếu nại; sắp theo `createdAt`, rồi `id`.

**`POST /appeals/{id}/score-dispute`**: chỉ người nộp khiếu nại, và chỉ khi khiếu nại đã có `HOLD` (nếu không: 409). Body `{ "reason": "..." }` 10–2000 ký tự. Lưu phản hồi mới nhất vào `scoreDisputeReason` của `AppealDetailDto`; không tự hoàn điểm.

**`POST /works/{id}/score-deductions`**: quyền `verification:decide` trong phạm vi, không tự trừ điểm của mình. Body như `review` nhưng `points` bắt buộc. Ghi một dòng `TASK_DEDUCTION` (`source = MANAGER`) cho người đang phụ trách công việc.

**`GET` / `PUT /system-parameters/appeal-score-hold`**: chỉ ADMIN. `PUT { "defaultPoints": 5 }` (> 0, ≤ 1000, tối đa 4 chữ số thập phân); chỉ áp cho các lần giữ sau, khoản đã giữ không đổi.

Tính lại điểm đánh giá luôn cộng tổng sổ; kết quả có thể âm và không bị cắt về 0, để khoản hoàn sau này khôi phục đúng điểm ban đầu. Đây là chính sách tạm giữ điểm đã thống nhất, không phải bút toán điều chỉnh âm của BR-16.

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
| Decision/note/verifiedValue không hợp lệ | 400 | `VALIDATION_ERROR` |
| Không có quyền xử lý hoặc ngoài scope | 403 | `FORBIDDEN` |
| Tự xử lý appeal/submission của mình | 422 | `MAKER_CANNOT_BE_CHECKER` |
| Appeal/submission/work không còn hợp lệ hoặc xử lý đồng thời | 409 | `APPEAL_INVALID_STATE` |
| Work đã được ghi nhận KPI | 409 | `KPI_ALREADY_CONTRIBUTED` |
| `review` / `score-deductions` / `score-dispute` / cấu hình: `points` hoặc `reason` không hợp lệ | 400 | `VALIDATION_ERROR` |
| Giữ / trừ điểm ngoài phạm vi hoặc cho chính mình; người không phải người nộp phản hồi; không phải ADMIN khi đổi cấu hình | 403 | `FORBIDDEN` |
| Không có điểm hiện tại để giữ / trừ, kỳ đã khóa khi hoàn điểm, khiếu nại chưa có `HOLD`, xử lý đồng thời | 409 | `CONFLICT` hoặc `APPEAL_INVALID_STATE` |

Mã lỗi định nghĩa tại `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs` (message VI/EN qua `ErrorCodes.GetDefaultMessage`).

## 5. Ghi chú triển khai

- Quy tắc "một khiếu nại đang xử lý trên mỗi đợt nộp" do service kiểm tra, chưa có ràng buộc ở database. Hai yêu cầu đồng thời cho cùng một đợt nộp có thể cùng được chấp nhận.
- Audit nộp / rút / xử lý khiếu nại hiện ghi qua log có cấu trúc (giống các service khác), chưa có bảng audit bền vững.

## 6. Kiểm chứng backend (2026-10-27)

- `AppealHttpTests` khởi động bản build API mới bằng Kestrel trên cổng loopback riêng, với PostgreSQL đã migrate và Redis thật. Dữ liệu test nằm trong database tạm, được xóa sau khi chạy.
- Gọi HTTP: readiness, nộp appeal multipart (201 + Location), quyết định ACCEPT/ADJUST/REJECT, chi tiết và danh sách, lịch sử verification, duyệt tiếp submission sau ADJUST.
- ACCEPT với `verifiedValue = 12.5` được kiểm tra trực tiếp: verified result, result item và actual progress lưu đúng 12.5 cho người nộp.
- Negative cases: không token 401; thiếu quyền hoặc ngoài phòng ban 403; decision/note/value sai 400; appeal không tồn tại 404; quyết định lặp 409; tài khoản khóa với JWT còn hiệu lực và permission đã cache 401.
- Gọi rút và accept đồng thời: chỉ một request thành công, request còn lại 409; kiểm tra trạng thái appeal/submission trong PostgreSQL không mâu thuẫn.
- JWT test được ký bằng khóa ngẫu nhiên của riêng API test; test này không kiểm tra màn hình FE hoặc thao tác đăng nhập bằng UI.
- Score hold extension: kiểm tra HTTP thay đổi cấu hình 9 điểm bằng ADMIN, review fallback trừ 9, chặn review lặp, ghi phản hồi nhân viên, ACCEPT hoàn 9 và tổng trở lại 100; PostgreSQL chặn UPDATE ledger. Unit tests kiểm tra ba mức ưu tiên, REJECT/ADJUST, hoàn sau duyệt, thiếu quyền/thiếu điểm và tính lại không mất khoản trừ.
