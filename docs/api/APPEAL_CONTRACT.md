# API Contract & Error Mapping — Appeal (Khiếu nại kết quả bị từ chối và khiếu nại điểm đánh giá)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: các endpoint dưới đây **đã implement ở BE**. Phần khiếu nại điểm (mục 2.8) mới build, chưa có test.

## 1. Phạm vi

- Có hai loại khiếu nại, phân biệt bằng trường `type`:
  - `RESULT`: nhân viên khiếu nại một **đợt nộp kết quả (`ResultSubmission`) đã bị từ chối** (`REJECTED`). Xử lý bằng `ACCEPT / ADJUST / REJECT`.
  - `SCORE`: nhân viên khiếu nại **điểm đánh giá** (`performanceEvaluationId`) của chính mình (mục 2.8). Xử lý bằng `ADJUST / UPHOLD`.
- `GET /appeals`, `GET /appeals/{id}`, `POST /appeals/{id}/withdraw`, `POST /appeals/{id}/decision` dùng chung cho cả hai loại; các trường của đợt nộp (`resultSubmissionId`, `workId`, `workCode`, `workTitle`, `submissionNo`) là `null` với `SCORE`, còn `performanceEvaluationId` và `periodCode` là `null` với `RESULT`.
- `/review` và `/score-dispute` (tạm giữ điểm, mục 2.7) chỉ làm việc với khiếu nại `RESULT`; với `SCORE` trả 404. `GET /appeals/{id}/score-entries` dùng được với cả hai loại (khiếu nại `SCORE` trả dòng `ADJUSTMENT` nếu có). `/works/{id}/score-deductions` không gắn với khiếu nại.
- Điểm đánh giá của kỳ liên quan chuyển sang `Disputed_Pending` khi nộp khiếu nại và được khôi phục khi rút (mục 2.6, BR-31). Với `RESULT`, xử lý khiếu nại (`/decision`) chưa đưa điểm ra khỏi `Disputed_Pending`; với `SCORE` thì có (mục 2.8).

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md` và mục 1 của `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`.

## 2. Routes

| Method | Route | Body / Query | Thành công |
|---|---|---|---|
| POST | `/submissions/{submissionId}/appeals` | multipart: `reason`, `files[]` | 201 `AppealDetailDto` + header `Location: /appeals/{id}`, message `CREATED` (`SuccessMessages`) |
| POST | `/performance-evaluations/{evaluationId}/appeals` | multipart: `reason`, `files[]` | 201 `AppealDetailDto` (`type = SCORE`) + header `Location: /appeals/{id}` (mục 2.8) |
| POST | `/appeals/{id}/withdraw` | — | 200 `AppealDetailDto` |
| POST | `/appeals/{id}/decision` | JSON `DecideAppealRequestDto` | 200 `AppealDecisionResultDto` |
| GET | `/appeals` | query `status`, `type`, `page`, `pageSize` | 200 `PagedResult<AppealListItemDto>` |
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

Query: `status` (một hoặc nhiều giá trị cách nhau bằng dấu phẩy: `OPEN | UNDER_REVIEW | ACCEPTED | REJECTED | WITHDRAWN`, không phân biệt hoa thường, bỏ trống là tất cả), `type` (`RESULT | SCORE`, không phân biệt hoa thường, bỏ trống là cả hai, giá trị khác là 400 `VALIDATION_ERROR`), `scope` (`MINE` chỉ khiếu nại của chính người gọi, `TEAM` chỉ khiếu nại của người khác trong phạm vi nhìn thấy; bỏ trống là cả hai; giá trị khác là 400), `periodId` (GUID kỳ đánh giá: khiếu nại điểm của kỳ đó và khiếu nại kết quả nộp trong khoảng ngày của kỳ; kỳ không tồn tại là 404), `jobPositionId` (GUID vị trí của nhân viên khiếu nại), `page` (mặc định 1), `pageSize` (mặc định 20, kẹp 1..100). Sắp xếp `filedAt` giảm dần, rồi `id`. Số kết quả `totalCount` tính sau mọi bộ lọc, nên màn hình có thể đếm theo nhóm trạng thái bằng `pageSize` nhỏ.

Phạm vi nhìn thấy (theo quyền `verification:decide` của người gọi, cùng cách tính với evidence):
| Quyền `verification:decide` | Thấy |
|---|---|
| Không có hoặc `Own` | Chỉ khiếu nại của mình |
| `Dept` | Của mình và của nhân sự thuộc phòng ban của mình (gồm phòng ban con) |
| `Branch` | Của mình và của nhân sự thuộc chi nhánh gần nhất của mình (gồm phòng ban con) |
| `All` hoặc ADMIN | Tất cả |

Migration `GrantAppealDecisionToBranchManager` cấp `verification:decide` ở scope `Branch` cho role `BRANCH_MANAGER`, nên Branch Manager thấy toàn bộ khiếu nại của chi nhánh. Seed phát triển (`seed_dev_data.sql`) đã được sao chép từ role `MANAGER` với scope `Dept` nâng thành `Branch`, nên cũng có quyền này.

### 2.4 Chi tiết — `GET /appeals/{id}`

Cùng phạm vi như danh sách. Ngoài phạm vi: 403. Không tồn tại: 404.

### 2.5 Xử lý — `POST /appeals/{id}/decision`

Mục này mô tả khiếu nại `RESULT`; khiếu nại `SCORE` xem mục 2.8. Body có thêm `points` chỉ dùng cho `SCORE`; gửi `points` hoặc `UPHOLD` cho khiếu nại `RESULT` là 400 `VALIDATION_ERROR`. Khiếu nại không tồn tại luôn là 404, kể cả khi `decision` hoặc `points` không phù hợp.

Yêu cầu quyền `verification:decide` với scope `Dept / Branch / All` bao phủ phòng ban người nộp; ADMIN được xử lý. Không được xử lý appeal của mình hoặc submission do mình nộp (422 `MAKER_CANNOT_BE_CHECKER`).

```json
{ "decision": "ADJUST", "note": "Bổ sung chứng từ đối soát có chữ ký xác nhận." }
```

- `decision`: bắt buộc `ACCEPT | ADJUST | REJECT`, không phân biệt hoa thường, trim khoảng trắng.
- `note`: tối đa 2000 ký tự sau trim; bắt buộc 10–2000 ký tự với `ADJUST / REJECT`, tùy chọn với `ACCEPT`.
- Không còn `verifiedValue`: khi `ACCEPT`, KPI được ghi nhận theo số liệu từng KPI nhân viên đã nộp lúc submit, cùng cách như duyệt submission thông thường (xem SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md).
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
`verificationId` là UUID với `ACCEPT / ADJUST`, `null` với `REJECT`. Envelope chuẩn `ApiResponse`, HTTP/status 200. Với khiếu nại `SCORE`, `submissionStatus`, `workStatus` và `verificationId` là `null`, và có thêm `scoreEntryId` (mục 2.8).

### 2.6 Điểm `Disputed_Pending` (BR-31)

Mục này mô tả khiếu nại `RESULT` (khiếu nại `SCORE`: mục 2.8). Khi nộp / rút khiếu nại, điểm đánh giá (`performance_evaluation`) của **chính người nộp** trong kỳ chứa ngày nộp đợt kết quả (`submittedAt`, tính theo giờ UTC+7, mọi kỳ có ngày đó nằm trong `[startDate, endDate]`, không phân biệt loại kỳ) thay đổi như sau. `Disputed_Pending` được lưu bằng trạng thái `DISPUTED` của `performance_evaluation_status`; trạng thái trước đó nằm ở cột `pre_dispute_status`, thời điểm ở `disputed_at`.

| Sự kiện | Điểm đánh giá |
|---|---|
| Nộp khiếu nại (201) | `CALCULATED` / `IN_REVIEW` / `APPROVED` → `DISPUTED`; `pre_dispute_status` = trạng thái cũ; `total_score` giữ nguyên |
| Nộp khiếu nại | `DRAFT`, `FINAL`, `DISPUTED` hoặc kỳ `LOCKED` / `CLOSED`: không đổi |
| Rút khiếu nại (200) | `DISPUTED` → `pre_dispute_status` (mặc định `CALCULATED`), xóa `pre_dispute_status` / `disputed_at` |
| Rút khiếu nại khi còn khiếu nại `OPEN` / `UNDER_REVIEW` khác của người đó trong cùng kỳ | Giữ `DISPUTED` |

Thay đổi nằm trong cùng lần lưu với khiếu nại. Điểm `DISPUTED` bị ẩn khỏi bảng xếp hạng, hạn ngạch Tier A, đường cong chuông, điểm trung bình và số người đã được đánh giá (xem `CALIBRATION_APPROVAL_CONTRACT.md` và `PERFORMANCE_DASHBOARD_CONTRACT.md`), đồng thời chặn khóa kỳ (`EVALUATION_DISPUTE_PENDING`).

Lưu form đánh giá (`POST /evaluations/submit`) cho điểm `DISPUTED` trả 409 `EVALUATION_DISPUTE_PENDING` để không vô tình xóa trạng thái tranh chấp.

Xử lý khiếu nại `RESULT` (mục 2.5) hiện chưa đưa điểm ra khỏi `DISPUTED`; điểm chỉ quay lại khi nhân viên rút khiếu nại. Khiếu nại `SCORE` thì quyết định xử lý cũng đưa điểm ra khỏi `DISPUTED` (mục 2.8).

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
| `ADJUSTMENT` | khác 0 (database cho phép cả hai dấu; khiếu nại `SCORE` hiện chỉ ghi dòng dương) | Điều chỉnh điểm khi xử lý khiếu nại `SCORE` (mục 2.8) | có `appealId`, không có `workId` / `reversesEntryId` |

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

### 2.8 Khiếu nại điểm đánh giá (`type = SCORE`)

**Nộp — `POST /performance-evaluations/{evaluationId}/appeals`** (multipart `reason`, `files[]`, cùng giới hạn và cách kiểm tra file như mục 2.1). Chỉ **chính nhân viên được đánh giá** được nộp. Điều kiện:
1. Điểm đang `CALCULATED` hoặc `IN_REVIEW` và đã có `totalScore`, kỳ chưa `LOCKED` / `CLOSED`. Kỳ đã khóa: 409 `PERIOD_LOCKED`; điểm ở trạng thái khác (gồm `DISPUTED`, `FINAL`): 409 `APPEAL_INVALID_STATE`.
2. Còn trong hạn: tính từ `calculatedAt` của điểm. Số giờ là hằng `ScoreAppealWindow` trong `AppealService.ScoreAppeals.cs`, **không nằm ở cấu hình hay DB**; đây là ngoại lệ so với quy tắc không hardcode, do chủ dự án yêu cầu. Giá trị và nơi lưu **đợi confirm**. Tính theo giờ máy chủ; đúng hạn vẫn hợp lệ, quá một giây là hết hạn: 422 `APPEAL_SCORE_WINDOW_EXPIRED`.
3. Điểm chưa có khiếu nại đang `OPEN` / `UNDER_REVIEW` (409 `APPEAL_ALREADY_OPEN`) và chưa có khiếu nại nào đã được xử lý `ACCEPTED` / `REJECTED` (409 `APPEAL_INVALID_STATE`). Khiếu nại đã `WITHDRAWN` không chặn nộp lại (vẫn phải trong hạn).

Sau khi nộp: appeal `OPEN`, `type = SCORE`, điểm chuyển `DISPUTED` (`pre_dispute_status` = trạng thái cũ, `total_score` giữ nguyên) và ghi một dòng `performance_evaluation_history` (`DISPUTED`) trong cùng lần lưu. Một lần nộp không tạo `HOLD`.

**Rút — `POST /appeals/{id}/withdraw`**: như mục 2.2. Điểm trở về `pre_dispute_status` trừ khi nhân viên còn khiếu nại khác đang xử lý trên cùng điểm hoặc cùng kỳ; ghi lịch sử `DISPUTE_WITHDRAWN`.

**Xử lý — `POST /appeals/{id}/decision`**: quyền `verification:decide` ở mức `Branch` trở lên (Dept: 403) và phạm vi phải bao phủ phòng ban của người khiếu nại; ADMIN được xử lý. Không được xử lý khiếu nại của chính mình (422 `MAKER_CANNOT_BE_CHECKER`). Chỉ xử lý khiếu nại `OPEN / UNDER_REVIEW` mà điểm còn `DISPUTED`; kỳ đã `LOCKED` / `CLOSED` thì 409 `PERIOD_LOCKED`; trạng thái khác hoặc xử lý đồng thời: 409 `APPEAL_INVALID_STATE`.
```json
{ "decision": "ADJUST", "note": "Đối chiếu lại số liệu, điều chỉnh tăng điểm kinh doanh.", "points": 2.5 }
```
- `decision`: `ADJUST | UPHOLD` (không phân biệt hoa thường). `ACCEPT / REJECT` và `verifiedValue` không áp dụng: 400 `VALIDATION_ERROR`.
- `note`: bắt buộc 10–2000 ký tự sau trim, cả hai quyết định.
- `points`: bắt buộc với `ADJUST`, là số điểm **cộng thêm** (> 0, ≤ giới hạn điểm của mục 2.7, tối đa 4 chữ số thập phân); không được gửi với `UPHOLD`. Khiếu nại điểm không dùng để trừ thêm điểm; muốn giữ nguyên điểm thì dùng `UPHOLD`.
- `ADJUST`: appeal `ACCEPTED`; ghi một dòng sổ điểm `ADJUSTMENT` (`source = MANAGER`) vào chính điểm đang tranh chấp, nên `totalScore` đổi **chỉ khi Branch Manager duyệt**; sổ chỉ ghi thêm, không sửa dòng cũ. `UPHOLD`: appeal `REJECTED`, điểm giữ nguyên.
- Cả hai: lưu `note`, `resolvedByEmployeeId`, `decidedAt`, điểm trở về `pre_dispute_status` (trừ khi còn khiếu nại khác đang xử lý) và ghi lịch sử `APPEAL_RESOLVED` với điểm trước và sau, trong cùng giao dịch.

Response: `AppealDecisionResultDto` với `submissionStatus`, `workStatus`, `verificationId` là `null` và `scoreEntryId` là id dòng `ADJUSTMENT` (`null` với `UPHOLD`).

Nộp khiếu nại: các điều kiện trên được kiểm tra trước khi lưu file, rồi kiểm tra lại trong giao dịch `Serializable` cùng lúc ghi. Xung đột đồng thời trên cùng điểm trả 409 `CONFLICT` ("thử lại"), không còn báo nhầm `APPEAL_ALREADY_OPEN`. Rút khiếu nại mà điểm vẫn `DISPUTED` vì còn khiếu nại khác thì không ghi thêm dòng lịch sử `DISPUTE_WITHDRAWN`.

Rủi ro đã biết: quy tắc "một khiếu nại đang xử lý trên mỗi điểm" do service kiểm tra trong giao dịch `Serializable`, chưa có chỉ mục duy nhất ở database. Migration `AddScoreEntryAdjustmentKind` không rollback được khi đã có dòng `ADJUSTMENT` (sổ điểm chỉ ghi thêm); `Down` dừng với thông báo rõ ràng.

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
  "updatedAt": "2026-10-24T10:00:00+00:00",
  "type": "RESULT",
  "performanceEvaluationId": null,
  "periodCode": null,
  "jobPositionTitle": "Giao dịch viên",
  "resolvedByEmployeeName": null,
  "note": null,
  "evaluationScore": null
}
```

Với `type = SCORE`: `resultSubmissionId`, `workId`, `workCode`, `workTitle`, `submissionNo` là `null`; `performanceEvaluationId`, `periodCode` (mã kỳ đánh giá) và `evaluationScore` (`totalScore` hiện tại của điểm bị khiếu nại, `null` nếu chưa có) có giá trị. `evaluationScore` luôn `null` với `RESULT`. `jobPositionTitle` là chức danh của người khiếu nại; `resolvedByEmployeeName` và `note` là người xử lý và ghi chú xử lý, `null` cho đến khi khiếu nại được xử lý.

`AppealDetailDto` = các trường của `AppealListItemDto` cộng:
```json
{
  "scoreDisputeReason": "string | null",
  "reason": "string",
  "note": "string | null",
  "resolvedByEmployeeId": "uuid | null",
  "resolvedByEmployeeName": "string | null",
  "submittedAt": "2026-10-24T09:00:00+00:00 | null",
  "workTypeName": "string | null",
  "referrerEmployeeCode": "string | null",
  "referrerEmployeeName": "string | null",
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

Các trường chỉ có ở chi tiết, đều `null` với `SCORE` hoặc khi không có dữ liệu (FE hiển thị N/A):
- `submittedAt`: thời điểm nộp của đợt nộp bị khiếu nại (`ResultSubmission.SubmittedAt`).
- `workTypeName`: tên loại công việc (`WorkType.Name`) của công việc chứa đợt nộp; `null` khi công việc không có loại.
- `referrerEmployeeCode` / `referrerEmployeeName`: nhân viên chuyển giao (`FromEmployee`) của lần bàn giao `ACCEPTED` mới nhất của công việc; `null` khi công việc chưa có bàn giao được chấp nhận.
- `evaluationScore` cũng có trong chi tiết với `SCORE`.

Chưa có dữ liệu ở backend nên API không trả: mã giao dịch, mã CIF, dữ liệu đối soát Core Banking, hạn xử lý khiếu nại, trạng thái chuyển cấp lên Giám đốc chi nhánh. FE hiển thị N/A cho các mục này đến khi backend bổ sung.

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
| Decision/note không hợp lệ | 400 | `VALIDATION_ERROR` |
| Không có quyền xử lý hoặc ngoài scope | 403 | `FORBIDDEN` |
| Tự xử lý appeal/submission của mình | 422 | `MAKER_CANNOT_BE_CHECKER` |
| Appeal/submission/work không còn hợp lệ hoặc xử lý đồng thời | 409 | `APPEAL_INVALID_STATE` |
| Work đã được ghi nhận KPI | 409 | `KPI_ALREADY_CONTRIBUTED` |
| `ACCEPT` cho work có KPI nhưng một nhân sự nhận điểm chưa có target `ACTIVE` trong kỳ hiện tại; khiếu nại giữ nguyên trạng thái, không ghi gì | 409 | `KPI_TARGET_NOT_ASSIGNED` |
| `review` / `score-deductions` / `score-dispute` / cấu hình: `points` hoặc `reason` không hợp lệ | 400 | `VALIDATION_ERROR` |
| Giữ / trừ điểm ngoài phạm vi hoặc cho chính mình; người không phải người nộp phản hồi; không phải ADMIN khi đổi cấu hình | 403 | `FORBIDDEN` |
| Không có điểm hiện tại để giữ / trừ, kỳ đã khóa khi hoàn điểm, khiếu nại chưa có `HOLD`, xử lý đồng thời | 409 | `CONFLICT` hoặc `APPEAL_INVALID_STATE` |
| Khiếu nại điểm: người gọi không phải nhân viên được đánh giá; người xử lý dưới mức `Branch` hoặc ngoài phạm vi | 403 | `FORBIDDEN` |
| Khiếu nại điểm: điểm không ở `CALCULATED` / `IN_REVIEW`, đã có khiếu nại được xử lý, hoặc xử lý khiếu nại không còn `OPEN` / `UNDER_REVIEW` hay điểm không còn `DISPUTED` | 409 | `APPEAL_INVALID_STATE` |
| Khiếu nại điểm: điểm đã có khiếu nại đang xử lý | 409 | `APPEAL_ALREADY_OPEN` |
| Khiếu nại điểm: kỳ đã `LOCKED` / `CLOSED` (nộp hoặc xử lý) | 409 | `PERIOD_LOCKED` |
| Khiếu nại điểm: quá hạn nộp (mục 2.8, điều kiện 2) | 422 | `APPEAL_SCORE_WINDOW_EXPIRED` |
| Khiếu nại điểm: điểm bị thay đổi đồng thời khi nộp | 409 | `CONFLICT` |
| Xử lý khiếu nại điểm: `decision` không phải `ADJUST` / `UPHOLD`, thiếu `note` hoặc `points`, `points` không dương hoặc không hợp lệ, `points` gửi cùng `UPHOLD`, có `verifiedValue` | 400 | `VALIDATION_ERROR` |

Mã lỗi định nghĩa tại `backend/src/G54.BLL/Common/Constants/ErrorCodes.cs` (message VI/EN qua `ErrorCodes.GetDefaultMessage`).

## 5. Ghi chú triển khai

- Quy tắc "một khiếu nại đang xử lý trên mỗi đợt nộp" do service kiểm tra, chưa có ràng buộc ở database. Hai yêu cầu đồng thời cho cùng một đợt nộp có thể cùng được chấp nhận.
- Audit nộp / rút / xử lý khiếu nại hiện ghi qua log có cấu trúc (giống các service khác), chưa có bảng audit bền vững.

## 6. Kiểm chứng backend (2026-10-27)

- `AppealHttpTests` khởi động bản build API mới bằng Kestrel trên cổng loopback riêng, với PostgreSQL đã migrate và Redis thật. Dữ liệu test nằm trong database tạm, được xóa sau khi chạy.
- Gọi HTTP: readiness, nộp appeal multipart (201 + Location), quyết định ACCEPT/ADJUST/REJECT, chi tiết và danh sách, lịch sử verification, duyệt tiếp submission sau ADJUST.
- ACCEPT với số liệu nộp 12.5 được kiểm tra trực tiếp: verified result, result item và actual progress lưu đúng 12.5 cho người nộp.
- Negative cases: không token 401; thiếu quyền hoặc ngoài phòng ban 403; decision/note/value sai 400; appeal không tồn tại 404; quyết định lặp 409; tài khoản khóa với JWT còn hiệu lực và permission đã cache 401.
- Gọi rút và accept đồng thời: chỉ một request thành công, request còn lại 409; kiểm tra trạng thái appeal/submission trong PostgreSQL không mâu thuẫn.
- JWT test được ký bằng khóa ngẫu nhiên của riêng API test; test này không kiểm tra màn hình FE hoặc thao tác đăng nhập bằng UI.
- Score hold extension: kiểm tra HTTP thay đổi cấu hình 9 điểm bằng ADMIN, review fallback trừ 9, chặn review lặp, ghi phản hồi nhân viên, ACCEPT hoàn 9 và tổng trở lại 100; PostgreSQL chặn UPDATE ledger. Unit tests kiểm tra ba mức ưu tiên, REJECT/ADJUST, hoàn sau duyệt, thiếu quyền/thiếu điểm và tính lại không mất khoản trừ.
