# API Contract & Error Mapping — Handover (Yêu cầu bàn giao công việc)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: các endpoint tạo, danh sách, chi tiết và quản lý duyệt **đã implement ở BE**; người nhận không có endpoint chấp nhận / từ chối.

## 1. Phạm vi

Có hai cách bàn giao, cùng dùng `POST /works/{workId}/handovers`:

| Ai tạo | Kết quả | Ai quyết định |
|---|---|---|
| **Nhân viên** (assignee hiện tại, hoặc Manager bàn giao việc của chính mình) | Yêu cầu `PENDING`, công việc chưa đổi | **Quản lý trực tiếp của người giao** (xem mục 5) |
| **Quản lý trực tiếp** của assignee (có quyền `handover:set_share`) | **Chuyển ngay**: yêu cầu `ACCEPTED`, công việc sang người nhận và `HANDED_OVER` | Không cần ai |

- **Người nhận không được chấp nhận hay từ chối.** Họ chỉ được báo khi công việc đã chuyển cho họ. Không có endpoint `accept` / `reject` của người nhận.
- **Quản lý trực tiếp của người giao** xử lý một yêu cầu `PENDING`: **duyệt** (chuyển ngay), **đổi người nhận** rồi chuyển ngay, hoặc **từ chối**.
- Khi công việc chuyển (duyệt, đổi người nhận hoặc quản lý chuyển ngay) thì công việc sang người nhận và sang `HANDED_OVER`, và **kỳ vọng KPI của công việc** (`Work.targetValue`) chuyển từ target tháng của người giao sang target tháng của người nhận, xem mục 3.1.
- Chưa có endpoint hủy yêu cầu (người giao tự rút).
- Màn `SCR-WRK-05` & `SCR-WRK-05-D1` dùng `GET /handovers` (phân trang, lọc theo chiều `INCOMING`/`OUTGOING`/`ALL`, trạng thái, người chuyển, trạng thái KPI, từ khóa) và `GET /handovers/{id}`.
Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/handovers/recipients` | — | 200 `HandoverRecipientDto[]`: nhân viên `ACTIVE` cùng phòng ban với người gọi (trừ chính họ), sắp theo tên. Quyền `handover:create`. Trường: `id`, `employeeCode`, `fullName`, `jobPositionTitle` |
| POST | `/works/{workId}/handovers` | JSON `CreateHandoverRequestDto` | 201 `HandoverDetailDto`. Nhân viên: `status = PENDING`, message `MSG05` (quản lý trực tiếp được báo). Quản lý chuyển ngay: `status = ACCEPTED`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/approve` | — | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reassign` | JSON `ReassignHandoverRequestDto` | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reject` | — | 200 `HandoverDetailDto`, message `HANDOVER_REJECTED` |

Tất cả yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động.

### Quyền

| Quyền | Dùng cho | SYSTEM_ADMIN | DEPT_MANAGER | EMPLOYEE |
|---|---|---|---|---|
| `handover:create` | Tạo yêu cầu | `All` | `Own` | `Own` |
| `handover:set_share` | Gửi `referrerSharePercent` khi tạo | `All` | `Own` | không có |
| `handover:respond` | Xem danh sách và chi tiết bàn giao của mình | `All` | `Own` | `Own` |
| `handover:manage` | Quản lý duyệt / đổi người nhận / từ chối | `All` | `Own` | không có |

Các quyền được seed bởi migration `SeedHandoverCreateAccess`, `SeedHandoverRespondAccess` và `SeedHandoverManageAccess` (và `seed_dev_data.sql` cho môi trường dev); chỉ gán cho role đã tồn tại. Quyền chỉ cho phép gọi API: việc chỉ assignee (hoặc quản lý trực tiếp của họ) mới được tạo và chỉ quản lý trực tiếp của người giao mới được duyệt vẫn kiểm tra trong service (403 `FORBIDDEN`).

## 3. Tạo yêu cầu — `POST /works/{workId}/handovers`

### Body

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Nhân viên nhận, khác assignee hiện tại, đang `ACTIVE` |
| `reason` | có | Sau khi cắt khoảng trắng: 1–2000 ký tự |
| `amount` | có | Giá trị tiền của việc bàn giao, `>= 0`. Chỉ dùng để kiểm tra ngưỡng, **không lưu** |
| `referrerSharePercent` | không | 0–100: tỷ lệ điểm KPI người giao (người giới thiệu) giữ lại. **Chỉ Manager/Admin** (quyền `handover:set_share`) được gửi trường này, người khác gửi sẽ bị 403. Không gửi thì người nhận bàn giao nhận 100% (lưu `0`) |

### Quy tắc nghiệp vụ

1. Người tạo phải là **assignee hiện tại** (tạo yêu cầu `PENDING` để quản lý quyết định), hoặc **quản lý trực tiếp** của assignee (`employee.managerId` của assignee là người gọi) **và** có quyền `handover:set_share` (Manager). Trường hợp thứ hai là **chuyển ngay**: yêu cầu lưu `ACCEPTED`, `acceptedAt` = giờ máy chủ, `approvedBy` = quản lý; `fromEmployeeId` là assignee cũ; công việc đổi `assigneeEmployeeId` sang người nhận và sang `HANDED_OVER` trong cùng lần lưu. Người khác (kể cả Admin không phải quản lý trực tiếp) nhận 403.
2. Công việc phải ở `NEW` hoặc `IN_PROGRESS` (theo `WorkStateMachine`, action `Handover`).
3. **BR-01 — ngưỡng số tiền:** `amount` lớn hơn tham số hệ thống `handoverThresholdVnd` (SCR-ADM-04, mặc định 500.000.000) thì bị chặn. `amount` đúng bằng ngưỡng vẫn hợp lệ.
4. Công việc không được có yêu cầu bàn giao `PENDING` khác (kể cả khi quản lý muốn chuyển ngay: phải đợi yêu cầu đó được xử lý). Ràng buộc được bảo đảm cả ở DB bằng partial unique index `ux_task_handover_pending_work`, nên hai request đồng thời cũng chỉ một request thành công, request còn lại nhận 409.
5. Khi tạo `PENDING`, **chỉ quản lý trực tiếp của người giao** được báo ("cần duyệt"); người nhận không được báo. Người giao không có quản lý thì không ai được báo và không ai duyệt được.

### 3.1. Chuyển kỳ vọng KPI giữa hai target tháng

Khi công việc chuyển (chuyển ngay, duyệt hoặc đổi người nhận), trong cùng lần lưu, với `X = Work.targetValue` (bỏ qua khi `null` hoặc `<= 0`) và mỗi KPI `SUM` đang có hiệu lực của loại công việc (`WorkTypeKpi`), ở **tháng hiện tại** (giờ UTC+7) và **tháng sau**:

- target `ACTIVE` của người giao giảm `min(X, mức nhỏ nhất trong chuỗi target của họ)` (không xuống dưới 0), target của người nhận tăng đúng lượng đó;
- **chuỗi target:** target tháng nằm dưới target quý (và năm ...) **của chính nhân viên đó**, và DB bắt buộc các target con cộng lại bằng target cha. Vì vậy mọi target cha cùng chủ (quý, năm) của người giao giảm, của người nhận tăng đúng lượng đó. Target của phòng ban bên trên không đổi vì tổng hai người giữ nguyên;
- target tháng đang có hiệu lực (BR-15) được đóng đến hôm qua (`SUPERSEDED`) và thay bằng version mới có hiệu lực từ hôm nay; target tháng bắt đầu từ hôm nay trở đi (thường là tháng sau) và các target quý / năm được sửa tại chỗ (không tạo version). `changeReason` = `Bàn giao công việc <mã>`, `changedByEmployeeId` = quản lý;
- **bỏ qua tháng/KPI đó, vẫn chuyển công việc** khi: không có `targetValue` hoặc loại công việc, KPI không có hiệu lực hoặc không phải `SUM`, người giao hoặc người nhận chưa có target, hai chuỗi khác hình dạng (khác kỳ hoặc `splitDimension` ở từng tầng) hoặc đỉnh chuỗi thuộc hai target phòng ban khác nhau, một target trong chuỗi không còn `ACTIVE`, target tháng có target con `ACTIVE`, một target cha có con chia theo kiểu khác chuỗi, kỳ đã khóa (`LOCKED` / `CLOSED`, hard-lock). Yêu cầu `reject` không đổi target nào.

## 4. Quản lý duyệt / đổi người nhận / từ chối — `POST /handovers/{id}/manager/approve|reassign|reject`

### Body

Chỉ `reassign` có body:

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Người nhận mới: tồn tại (404 nếu không), đang `ACTIVE`, khác người đang phụ trách công việc |

### Quy tắc

1. Người gọi phải là **quản lý trực tiếp của người giao** (`fromEmployee.managerId`), không phải của người nhận; người khác (kể cả người giao, người nhận, Admin không phải quản lý trực tiếp) nhận 403.
2. Yêu cầu phải đang `PENDING`, nếu không 409 `HANDOVER_NOT_PENDING`.
3. **approve:** chuyển công việc ngay, không cần người nhận: yêu cầu `ACCEPTED`, `acceptedAt` = giờ máy chủ, `approvedBy` = quản lý; công việc sang người nhận và `HANDED_OVER`. Điều kiện về công việc: công việc vẫn do người giao phụ trách và ở `NEW` / `IN_PROGRESS` (409 `WORK_INVALID_STATE` nếu công việc đã đổi người phụ trách hoặc không còn ở `NEW` / `IN_PROGRESS`).
4. **reassign:** đổi `toEmployeeId` của yêu cầu sang người nhận mới rồi duyệt như `approve` (người nhận mới bắt buộc nhận). Người nhận cũ không còn liên quan.
5. **reject:** yêu cầu `REJECTED`, công việc không đổi, `approvedBy` để trống; vẫn từ chối được khi công việc đã đổi trạng thái. Người giao có thể tạo yêu cầu mới.
6. Hai quyết định đồng thời trên cùng một yêu cầu chỉ một quyết định thành công (kiểm tra bằng `xmin` của PostgreSQL), quyết định còn lại nhận 409 `HANDOVER_NOT_PENDING`.

## 5. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / sai `toEmployeeId`, `reason`, `amount`, `referrerSharePercent`; bàn giao cho chính assignee hiện tại; người nhận không `ACTIVE` | 400 | `VALIDATION_ERROR` |
| Không có quyền `handover:create` / `handover:respond` / `handover:manage`; tạo mà không phải assignee hoặc quản lý trực tiếp của assignee; xử lý kiểu quản lý mà không phải quản lý trực tiếp của người giao; gửi `referrerSharePercent` mà không có `handover:set_share` | 403 | `FORBIDDEN` |
| Không có công việc / người nhận / yêu cầu bàn giao | 404 | `NOT_FOUND` |
| Công việc không ở trạng thái cho phép bàn giao (lúc tạo hoặc lúc quản lý duyệt), hoặc đã đổi người phụ trách | 409 | `WORK_INVALID_STATE` |
| Đã có yêu cầu `PENDING` | 409 | `HANDOVER_ALREADY_PENDING` |
| Yêu cầu không còn `PENDING` (đã được quản lý xử lý) | 409 | `HANDOVER_NOT_PENDING` |
| `reassign`: thiếu `toEmployeeId`, người nhận không `ACTIVE` hoặc là người đang phụ trách | 400 | `VALIDATION_ERROR` |
| `amount` vượt ngưỡng (BR-01) | 422 | `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD` |

## 6. Ví dụ

```http
POST /works/6c1f.../handovers
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000005", "reason": "Khách hàng ở khu vực nhóm 2", "amount": 120000000 }

POST /handovers/9c58bb72-8aef-4908-83e9-3604469e5369/manager/approve

POST /handovers/9c58bb72-8aef-4908-83e9-3604469e5369/manager/reassign
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000006" }
```
