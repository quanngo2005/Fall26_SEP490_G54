# API Contract & Error Mapping — Handover (Yêu cầu bàn giao công việc)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: các endpoint tạo, chấp nhận và từ chối yêu cầu **đã implement ở BE**, FE chưa có.

## 1. Phạm vi

Có hai cách bàn giao, cùng dùng `POST /works/{workId}/handovers`:

| Ai tạo | Kết quả | Người nhận |
|---|---|---|
| **Nhân viên** (assignee hiện tại, hoặc Manager bàn giao việc của chính mình) | Yêu cầu `PENDING`, công việc chưa đổi | **Chấp nhận hoặc từ chối** (`accept` / `reject`) |
| **Quản lý trực tiếp** của assignee (có quyền `handover:set_share`) | **Chuyển ngay**: yêu cầu `ACCEPTED`, công việc sang người nhận và `HANDED_OVER` | **Bắt buộc nhận**, không có accept/reject |

- **Quản lý trực tiếp của người giao** cũng có thể xử lý một yêu cầu `PENDING` mà không cần người nhận: **duyệt** (chuyển ngay), **đổi người nhận** rồi chuyển ngay, hoặc **từ chối**.
- Khi người nhận **chấp nhận** (hoặc quản lý chuyển ngay) thì công việc chuyển sang người nhận và sang `HANDED_OVER`.
- Chưa có endpoint hủy yêu cầu (người giao tự rút) và chưa có danh sách / chi tiết yêu cầu.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Body | Thành công |
|---|---|---|---|
| POST | `/works/{workId}/handovers` | JSON `CreateHandoverRequestDto` | 201 `HandoverDetailDto`. Nhân viên: `status = PENDING`, message `MSG05`. Quản lý chuyển ngay: `status = ACCEPTED`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/accept` | — | 200 `HandoverDetailDto` |
| POST | `/handovers/{id}/reject` | — | 200 `HandoverDetailDto` |
| POST | `/handovers/{id}/manager/approve` | — | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reassign` | JSON `ReassignHandoverRequestDto` | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reject` | — | 200 `HandoverDetailDto`, message `HANDOVER_REJECTED` |

Tất cả yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động.

### Quyền

| Quyền | Dùng cho | ADMIN | MANAGER | EMPLOYEE |
|---|---|---|---|---|
| `handover:create` | Tạo yêu cầu | `All` | `Own` | `Own` |
| `handover:set_share` | Gửi `referrerSharePercent` khi tạo | `All` | `Own` | không có |
| `handover:respond` | Người nhận chấp nhận / từ chối | `All` | `Own` | `Own` |
| `handover:manage` | Quản lý duyệt / đổi người nhận / từ chối | `All` | `Own` | không có |

Các quyền được seed bởi migration `SeedHandoverCreateAccess`, `SeedHandoverRespondAccess` và `SeedHandoverManageAccess` (và `seed_dev_data.sql` cho môi trường dev); chỉ gán cho role đã tồn tại. Quyền chỉ cho phép gọi API — việc chỉ assignee mới được tạo yêu cầu và chỉ người nhận mới được trả lời vẫn kiểm tra trong service (403 `FORBIDDEN`).

## 3. Tạo yêu cầu — `POST /works/{workId}/handovers`

### Body

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Nhân viên nhận, khác assignee hiện tại, đang `ACTIVE` |
| `reason` | có | Sau khi cắt khoảng trắng: 1–2000 ký tự |
| `amount` | có | Giá trị tiền của việc bàn giao, `>= 0`. Chỉ dùng để kiểm tra ngưỡng, **không lưu** |
| `referrerSharePercent` | không | 0–100: tỷ lệ điểm KPI người giao (người giới thiệu) giữ lại. **Chỉ Manager/Admin** (quyền `handover:set_share`) được gửi trường này, người khác gửi sẽ bị 403. Không gửi thì người nhận bàn giao nhận 100% (lưu `0`) |

### Quy tắc nghiệp vụ

1. Người tạo phải là **assignee hiện tại**, hoặc **quản lý trực tiếp** của assignee (`employee.managerId` của assignee là người gọi) **và** có quyền `handover:set_share` (Manager). Trường hợp thứ hai là **chuyển ngay**: yêu cầu lưu `ACCEPTED`, `acceptedAt` = giờ máy chủ, `approvedBy` = quản lý; `fromEmployeeId` là assignee cũ; công việc đổi `assigneeEmployeeId` sang người nhận và sang `HANDED_OVER` trong cùng lần lưu. Admin không phải quản lý trực tiếp thì không chuyển được (giống hủy / hoàn tất công việc).
2. Công việc phải ở `NEW` hoặc `IN_PROGRESS` (theo `WorkStateMachine`, action `Handover`).
3. **BR-01 — ngưỡng số tiền:** `amount` lớn hơn `Handover:AmountThreshold` thì bị chặn (cấu hình trong `appsettings.json` hoặc biến môi trường `Handover__AmountThreshold`; mặc định 500.000.000). `amount` đúng bằng ngưỡng vẫn hợp lệ.
4. Công việc không được có yêu cầu bàn giao `PENDING` khác (kể cả khi quản lý muốn chuyển ngay: phải đợi yêu cầu đó được trả lời).  Ràng buộc được bảo đảm cả ở DB bằng partial unique index `ux_task_handover_pending_work`, nên hai request đồng thời cũng chỉ một request thành công, request còn lại nhận 409.

## 4. Chấp nhận / từ chối — `POST /handovers/{id}/accept|reject`

Không có body. Response `data` là `HandoverDetailDto` (có `acceptedAt`, `null` khi chưa chấp nhận).

### Quy tắc

1. Chỉ **người nhận** (`toEmployeeId`) được trả lời, người khác (kể cả người giao) nhận 403. Bàn giao do quản lý chuyển ngay đã `ACCEPTED` nên không còn gì để trả lời (409 `HANDOVER_NOT_PENDING`).
2. Yêu cầu phải đang `PENDING`; đã `ACCEPTED` / `REJECTED` thì 409 `HANDOVER_NOT_PENDING`. Hai request trả lời đồng thời chỉ một request thành công (kiểm tra bằng `xmin` của PostgreSQL), request còn lại nhận 409 `HANDOVER_NOT_PENDING`.
3. **Accept** (trong một lần lưu):
   - yêu cầu → `ACCEPTED`, `acceptedAt` = giờ máy chủ;
   - công việc chuyển `assigneeEmployeeId` sang người nhận và sang `HANDED_OVER`; người nhận sau đó bắt đầu làm bằng `PATCH /works/{id}/status` (`Start`, `HANDED_OVER` → `IN_PROGRESS`);
   - điều kiện: công việc vẫn đang do người giao phụ trách và vẫn ở `NEW` / `IN_PROGRESS`, nếu không 409 `WORK_INVALID_STATE` (ví dụ công việc đã bị hủy hoặc quá hạn từ lúc yêu cầu được tạo; yêu cầu giữ nguyên `PENDING`).
4. **Reject:** yêu cầu → `REJECTED`; công việc không đổi, người giao có thể tạo yêu cầu mới. Vẫn từ chối được khi công việc đã đổi trạng thái.
5. Điểm KPI sau này được chia theo `referrerSharePercent` của yêu cầu `ACCEPTED` mới nhất (xem `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`).

## 5. Quản lý duyệt / đổi người nhận / từ chối — `POST /handovers/{id}/manager/approve|reassign|reject`

### Body

Chỉ `reassign` có body:

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Người nhận mới: tồn tại (404 nếu không), đang `ACTIVE`, khác người đang phụ trách công việc |

### Quy tắc

1. Người gọi phải là **quản lý trực tiếp của người giao** (`fromEmployee.managerId`), không phải của người nhận; người khác (kể cả người giao, người nhận, Admin không phải quản lý trực tiếp) nhận 403.
2. Yêu cầu phải đang `PENDING`, nếu không 409 `HANDOVER_NOT_PENDING`.
3. **approve:** như người nhận chấp nhận nhưng không cần họ: yêu cầu `ACCEPTED`, `acceptedAt` = giờ máy chủ, `approvedBy` = quản lý; công việc sang người nhận và `HANDED_OVER`. Điều kiện về công việc giống mục 4 (409 `WORK_INVALID_STATE` nếu công việc đã đổi người phụ trách hoặc không còn ở `NEW` / `IN_PROGRESS`).
4. **reassign:** đổi `toEmployeeId` của yêu cầu sang người nhận mới rồi duyệt như `approve` (người nhận mới bắt buộc nhận, không có accept/reject). Người nhận cũ không còn liên quan.
5. **reject:** yêu cầu `REJECTED`, công việc không đổi, `approvedBy` để trống; vẫn từ chối được khi công việc đã đổi trạng thái. Người giao có thể tạo yêu cầu mới.
6. Cùng cơ chế chống trả lời đồng thời với mục 4 (`xmin`): chỉ một quyết định thành công.

## 6. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / sai `toEmployeeId`, `reason`, `amount`, `referrerSharePercent`; bàn giao cho chính assignee hiện tại; người nhận không `ACTIVE` | 400 | `VALIDATION_ERROR` |
| Không có quyền `handover:create` / `handover:respond` / `handover:manage`; tạo mà không phải assignee hoặc quản lý trực tiếp của assignee; trả lời mà không phải người nhận; xử lý kiểu quản lý mà không phải quản lý trực tiếp của người giao; gửi `referrerSharePercent` mà không có `handover:set_share` | 403 | `FORBIDDEN` |
| Không có công việc / người nhận / yêu cầu bàn giao | 404 | `NOT_FOUND` |
| Công việc không ở trạng thái cho phép bàn giao (lúc tạo hoặc lúc chấp nhận), hoặc đã đổi người phụ trách | 409 | `WORK_INVALID_STATE` |
| Đã có yêu cầu `PENDING` | 409 | `HANDOVER_ALREADY_PENDING` |
| Yêu cầu không còn `PENDING` (đã được trả lời) | 409 | `HANDOVER_NOT_PENDING` |
| `reassign`: thiếu `toEmployeeId`, người nhận không `ACTIVE` hoặc là người đang phụ trách | 400 | `VALIDATION_ERROR` |
| `amount` vượt ngưỡng (BR-01) | 422 | `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD` |

## 7. Ví dụ

```http
POST /works/6c1f.../handovers
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000005", "reason": "Khách hàng ở khu vực nhóm 2", "amount": 120000000 }

POST /handovers/9c58bb72-8aef-4908-83e9-3604469e5369/accept

POST /handovers/9c58bb72-8aef-4908-83e9-3604469e5369/manager/reassign
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000006" }
```
