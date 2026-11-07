# API Contract & Error Mapping — Notification (Thông báo)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi / sự kiện SignalR phải cập nhật file này.
> Trạng thái: BE đã implement (REST + SignalR), FE chưa có.

## 1. Phạm vi

Thông báo **trong ứng dụng** (chưa có email). Mỗi thông báo là một dòng cho **một người nhận**, nên trạng thái đã đọc là của riêng từng người.

Hai nguồn:

| Nguồn | Ai tạo | Ví dụ |
|---|---|---|
| **Thông cáo** (`ANNOUNCEMENT`) | Quản lý / Admin gọi `POST /notifications` | Họp phòng, nhắc nộp báo cáo |
| **Tự động** | Hệ thống, khi có sự kiện | Yêu cầu bàn giao, kết quả bàn giao, công việc quá hạn |

Thông báo đã gửi **không sửa, không thu hồi** được. Gửi thành công là xong.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Quyền | Mô tả |
|---|---|---|---|
| POST | `/notifications` | `notification:send` (`Dept`) | Gửi thông cáo. 201 `SendNotificationResultDto` |
| GET | `/notifications` | `notification:read` (`Own`) | Hộp thư của người gọi, mới nhất trước. Query: `unread`, `page`, `pageSize` (tối đa 100). 200 `PagedResult<NotificationDto>` |
| GET | `/notifications/unread-count` | `notification:read` | 200 `{ "count": n }` |
| POST | `/notifications/{id}/read` | `notification:read` | Đánh dấu đã đọc, gọi lại không đổi gì. 200 `NotificationDto`. 404 nếu không phải thông báo của người gọi |
| POST | `/notifications/read-all` | `notification:read` | Đánh dấu tất cả đã đọc. 200 `{ "count": số đã đổi }` |

### Quyền

| Quyền | ADMIN / SYSTEM_ADMIN | BRANCH_MANAGER | DEPT_MANAGER / MANAGER | EMPLOYEE |
|---|---|---|---|---|
| `notification:send` | `All` | `Branch` | `Dept` | không có |
| `notification:read` | `All` | `Own` | `Own` | `Own` |

Theo thiết kế, `notification:read` chỉ mở hộp thư **của chính người gọi**; scope `All` của Admin chỉ để khớp quy ước admin có mọi quyền ở `All`, không cho đọc thư của người khác.

Quyền được seed bởi migration `AddNotifications` (và `seed_dev_data.sql`).

## 3. Gửi thông cáo — `POST /notifications`

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `title` | có | Sau khi cắt khoảng trắng: 1–200 ký tự |
| `body` | không | Tối đa 2000 ký tự |
| `severity` | không | `NORMAL` (mặc định) hoặc `IMPORTANT` |
| `recipientEmployeeIds` | không | Chọn người nhận. Bỏ trống = **mọi nhân viên đang `ACTIVE` trong phạm vi** `notification:send` của người gửi |
| `relatedType` + `relatedId` | không | Đính kèm liên kết: `WORK` hoặc `CAMPAIGN` và id của nó. Phải gửi cùng nhau, bản ghi phải tồn tại |

Quy tắc:

1. Phạm vi: `Dept` = phòng của người gửi và các phòng con, `Branch` = chi nhánh gần nhất, `All` = toàn hệ thống.
2. Người gửi **không** nhận thông cáo của chính mình.
3. Có `recipientEmployeeIds`: mọi người nhận phải tồn tại và đang `ACTIVE`, và phải nằm trong phạm vi, nếu không toàn bộ yêu cầu bị từ chối và không gửi ai.
4. Phải có ít nhất một người nhận.
5. Response `data`: `{ "recipientCount": n }`.

```http
POST /notifications
{ "title": "Họp phòng 9h", "body": "Phòng họp 3", "severity": "IMPORTANT",
  "recipientEmployeeIds": ["5eed0000-0000-0000-0007-000000000005"],
  "relatedType": "WORK", "relatedId": "6c1f..." }
```

## 4. `NotificationDto`

| Trường | Ghi chú |
|---|---|
| `id` | |
| `type` | `ANNOUNCEMENT`, `HANDOVER_TRANSFERRED`, `WORK_OVERDUE` (các giá trị cũ `HANDOVER_REQUESTED`, `HANDOVER_ACCEPTED`, `HANDOVER_REJECTED` không còn được tạo mới; thông báo cũ trong DB vẫn có thể mang các giá trị này) |
| `severity` | `NORMAL`, `IMPORTANT`. Client dùng `IMPORTANT` để hiện popup nổi bật |
| `title`, `body` | `body` có thể `null` |
| `relatedType`, `relatedId` | `WORK`, `CAMPAIGN`, `HANDOVER` hoặc `null`; để mở bản ghi liên quan |
| `senderEmployeeId`, `senderName` | Chỉ có với thông cáo; `null` với thông báo của hệ thống |
| `createdAt`, `isRead`, `readAt` | |

## 5. Thông báo tự động

| Sự kiện | Người nhận | `type` |
|---|---|---|
| Công việc được bàn giao (BR-21: có hiệu lực ngay, không có bước duyệt) | Người nhận (`IMPORTANT`) và người giao (`NORMAL`); người tạo bàn giao không nhận, nên khi nhân viên tự bàn giao thì chỉ người nhận được báo | `HANDOVER_TRANSFERRED`, `relatedType = HANDOVER` |
| Job quá hạn chuyển công việc sang `OVERDUE` | Người thực hiện (`IMPORTANT`) và quản lý trực tiếp của họ | `WORK_OVERDUE`, `relatedType = WORK`. Mỗi công việc chỉ báo một lần, lúc nó chuyển sang quá hạn |

Thông báo tự động là **best effort**: nếu không ghi được thì chỉ log cảnh báo và **không** làm hỏng thao tác chính đã lưu.

## 6. Đẩy thời gian thực — SignalR

Để hiện popup ngay khi có thông báo mới.

- Hub: `/hubs/notifications`.
- Xác thực: JWT access token. WebSocket không gửi được header, nên client truyền token ở query: `/hubs/notifications?access_token=<jwt>` (chỉ nhận ở đường dẫn hub).
- Khi kết nối, server tự đưa người dùng vào nhóm riêng của họ; người dùng chỉ nhận thông báo của chính mình.
- Sự kiện server → client: **`notification`**, tham số là một `NotificationDto` (cùng cấu trúc với REST).
- Thông báo luôn được lưu **trước** khi đẩy, nên client offline vẫn thấy ở `GET /notifications` khi đăng nhập lại. Đẩy lỗi chỉ ghi log.
- CORS cho phép credentials với các origin đã cấu hình (`Cors:Origins`).
- Hiện chạy một instance API. Nếu nhân bản API phải thêm Redis backplane cho SignalR.

```ts
const connection = new HubConnectionBuilder()
  .withUrl(`${apiUrl}/hubs/notifications`, { accessTokenFactory: () => token })
  .withAutomaticReconnect()
  .build();
connection.on('notification', (n: NotificationDto) => showPopup(n));
await connection.start();
```

## 7. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / quá dài `title`, `body`; `severity` hoặc `relatedType` không hợp lệ; `relatedType` và `relatedId` không đi cùng nhau; người nhận không tồn tại hoặc không `ACTIVE`; không có người nhận nào | 400 | `VALIDATION_ERROR` |
| Không có quyền `notification:send` / `notification:read`; gửi cho người ngoài phạm vi | 403 | `FORBIDDEN` |
| `relatedId` không tồn tại; thông báo không có hoặc không phải của người gọi | 404 | `NOT_FOUND` |
