# API Contract & Error Mapping — Notification (Thông báo)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi / sự kiện SignalR phải cập nhật file này.
> Trạng thái (GB-914): BE đã implement (REST + SignalR + thông báo tự động + cảnh báo hệ thống), FE đã có chuông ở header, popup, trang `/notifications`.

## 1. Phạm vi

Thông báo **trong ứng dụng** (chưa có email). Mỗi thông báo là một dòng cho **một người nhận**, nên trạng thái đã đọc là của riêng từng người.

Hai nguồn:

| Nguồn | Ai tạo | Ví dụ |
|---|---|---|
| **Thông cáo** (`ANNOUNCEMENT`) | Quản lý / Admin gọi `POST /notifications` | Họp phòng, nhắc nộp báo cáo |
| **Tự động** | Hệ thống, khi có sự kiện hoặc ngoại lệ | Giao việc, thẩm định, đối soát lệch, khiếu nại, cảnh báo hệ thống |

Thông báo đã gửi **không sửa, không thu hồi** được. Gửi thành công là xong.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Quyền | Mô tả |
|---|---|---|---|
| POST | `/notifications` | `notification:send` (`Dept`) | Gửi thông cáo. 201 `SendNotificationResultDto` |
| GET | `/notifications/recipients` | `notification:send` (`Dept`) | Danh sách nhân viên `ACTIVE` trong phạm vi gửi của người gọi (không gồm chính họ), để chọn người nhận. 200 `NotificationRecipientOptionDto[]` |
| GET | `/notifications` | `notification:read` (`Own`) | Hộp thư của người gọi, mới nhất trước. Query: `unread`, `type`, `severity`, `keyword`, `page`, `pageSize` (tối đa 100). 200 `PagedResult<NotificationDto>` |
| GET | `/notifications/unread-count` | `notification:read` | 200 `{ "count": n }` |
| POST | `/notifications/{id}/read` | `notification:read` | Đánh dấu đã đọc, gọi lại không đổi gì. 200 `NotificationDto`. 404 nếu không phải thông báo của người gọi |
| POST | `/notifications/read-all` | `notification:read` | Đánh dấu tất cả đã đọc. 200 `{ "count": số đã đổi }` |

### Bộ lọc `GET /notifications`

| Tham số | Quy tắc |
|---|---|
| `unread` | `true` chỉ chưa đọc, `false` chỉ đã đọc, bỏ trống = tất cả |
| `type` | Một mã `type` ở mục 4. Sai mã: 400 `VALIDATION_ERROR` |
| `severity` | `NORMAL` hoặc `IMPORTANT`. Sai: 400 |
| `keyword` | Khớp một phần, không phân biệt hoa thường, trong `title` hoặc `body` (ký tự `%` `_` được hiểu là chữ thường) |

### Quyền

| Quyền | SYSTEM_ADMIN | BRANCH_MANAGER | DEPT_MANAGER | VERIFIER / HR_OFFICER | EMPLOYEE |
|---|---|---|---|---|---|
| `notification:send` | `All` | `Branch` | `Dept` | không có | không có |
| `notification:read` | `All` | `Own` | `Own` | `Own` | `Own` |

Theo thiết kế, `notification:read` chỉ mở hộp thư **của chính người gọi**; scope `All` của Admin chỉ để khớp quy ước admin có mọi quyền ở `All`, không cho đọc thư của người khác.

Quyền được seed bởi migration `AddNotifications`; `ExpandNotificationsAndPermissions` cấp thêm `notification:read` (`Own`) cho `VERIFIER`, `HR_OFFICER` (và `seed_dev_data.sql`).

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
| `type` | Xem bảng loại bên dưới |
| `severity` | `NORMAL`, `IMPORTANT`. Client dùng `IMPORTANT` để hiện popup nổi bật |
| `title`, `body` | `body` có thể `null` |
| `relatedType`, `relatedId` | `WORK`, `CAMPAIGN`, `HANDOVER`, `KPI`, `TARGET`, `SUBMISSION`, `APPEAL`, `RISK_EVENT`, `QUALITY_FLAG`, `EVALUATION_PERIOD`, `EVALUATION_PROFILE`, `IMPORT_BATCH`, `AUDIT_LOG` hoặc `null`; để mở bản ghi liên quan |
| `senderEmployeeId`, `senderName` | Chỉ có với thông cáo hoặc khi có người thực hiện; `null` với thông báo của hệ thống |
| `createdAt`, `isRead`, `readAt` | |

`NotificationRecipientOptionDto`: `id`, `employeeCode`, `fullName`, `departmentId`, `departmentName`, `jobPositionTitle`.

## 5. Thông báo tự động

Các sự kiện **đã có** thông báo (người nhận → `type`). Người ra quyết định/thực hiện không tự nhận thông báo của chính mình.

| Sự kiện | Người nhận | `type` (độ quan trọng) |
|---|---|---|
| Nhân viên tạo yêu cầu bàn giao | Người nhận bàn giao (`IMPORTANT`), quản lý trực tiếp của người giao | `HANDOVER_REQUESTED` |
| Người nhận/quản lý quyết định bàn giao | Người giao, người nhận mới | `HANDOVER_ACCEPTED` / `HANDOVER_REJECTED` / `HANDOVER_TRANSFERRED` |
| Job quá hạn chuyển công việc sang `OVERDUE` | Người thực hiện (`IMPORTANT`), quản lý trực tiếp | `WORK_OVERDUE` |
| Quản lý sửa kế hoạch công việc | Người thực hiện (`IMPORTANT`) | `WORK_PLAN_UPDATED` |
| Quản lý giao công việc mới (`POST /works`) | Người được giao | `WORK_ASSIGNED` |
| Sinh hàng loạt đầu việc chiến dịch | Mỗi người tham gia (một thông báo tóm tắt) | `WORK_ASSIGNED`, `relatedType = CAMPAIGN` |
| Nhân viên nộp kết quả | Quản lý trực tiếp của người nộp | `SUBMISSION_SUBMITTED` |
| Thẩm định kết quả (duyệt / yêu cầu bổ sung / từ chối) | Người nộp (`IMPORTANT` khi bổ sung hoặc từ chối) | `SUBMISSION_DECIDED` |
| Mã giao dịch đối soát **không khớp** Core Banking (khi nhân viên thêm mã hoặc sau khi import) | Người thực hiện (`IMPORTANT`), quản lý trực tiếp | `TRANSACTION_MISMATCH`, `relatedType = WORK` |
| Import giao dịch / nhân sự xong nhưng có dòng lỗi | Người import (giao dịch) hoặc mọi Admin đang hoạt động (nhân sự) | `IMPORT_COMPLETED_WITH_ERRORS` (`IMPORTANT`) |
| QA ghi nhận / xác nhận sự kiện rủi ro | Nhân viên bị ghi nhận, quản lý trực tiếp (`IMPORTANT`) | `RISK_EVENT_RECORDED` |
| Đảo sự kiện rủi ro (gỡ điểm phạt) | Nhân viên bị ghi nhận, quản lý trực tiếp | `RISK_EVENT_REVERSED` |
| QA gắn cờ chất lượng (trực tiếp hoặc mẫu 5% không đạt) | Nhân viên, quản lý trực tiếp | `QUALITY_FLAG_RAISED` |
| QA yêu cầu làm lại | Nhân viên và người thực hiện | `REWORK_REQUESTED` |
| Nhân viên gửi khiếu nại kết quả | Quản lý trực tiếp của người gửi (`IMPORTANT`) | `APPEAL_FILED` |
| Quyết định khiếu nại (chấp nhận / yêu cầu bổ sung / từ chối) | Người gửi khiếu nại (`IMPORTANT`) | `APPEAL_DECIDED` |
| Trưởng phòng trình duyệt bảng hiệu chuẩn | Mọi Giám đốc chi nhánh đang hoạt động và quản lý trực tiếp của người trình (`IMPORTANT`) | `CALIBRATION_SUBMITTED`, `relatedType = EVALUATION_PERIOD` |
| Giám đốc chi nhánh duyệt / trả lại hiệu chuẩn | Các Trưởng phòng của phòng đó (`IMPORTANT`) | `CALIBRATION_DECIDED` |
| Hệ thống tính xong đánh giá, chuyển `DRAFT` → `CALCULATED` | Nhân viên được đánh giá (`IMPORTANT`) | `EVALUATION_CALCULATED`, `relatedType = EVALUATION_PERIOD` |
| Trưởng phòng nộp đánh giá lên giám đốc (`SUBMITTED`) | Mọi Giám đốc chi nhánh đang hoạt động (`IMPORTANT`) | `EVALUATION_SUBMITTED_TO_BRANCH` |
| Giám đốc trả về để kiểm tra lại (`CALCULATED`) | Trưởng phòng của nhân viên, kèm lý do (`IMPORTANT`) | `EVALUATION_RETURNED` |
| Giám đốc xác nhận hạng chốt (`FINAL`) | Trưởng phòng của nhân viên (`IMPORTANT`) | `EVALUATION_FINALIZED` |
| Khóa kỳ đánh giá (công bố kết quả) | Mọi nhân viên đang hoạt động có đánh giá trong kỳ (`IMPORTANT`) | `RESULT_PUBLISHED` |
| Kỳ đánh giá bị khóa khi kết quả còn `SUBMITTED` / `UNDER_REVIEW`, kết quả được chuyển sang kỳ kế tiếp | Người nộp kết quả (`IMPORTANT`), mỗi người một thông báo | `RESULT_CARRIED_OVER`, `relatedType = EVALUATION_PERIOD` (kỳ nhận kết quả) |
| Duyệt / từ chối kế hoạch chiến dịch | Người tham gia chiến dịch (`IMPORTANT` khi từ chối) | `CAMPAIGN_DECIDED` |
| Duyệt KPI | Người trình và người yêu cầu KPI (`IMPORTANT`) | `KPI_APPROVED` / `KPI_REJECTED` |
| Duyệt / từ chối hồ sơ đánh giá | Mọi Admin đang hoạt động (`IMPORTANT`) | `EVALUATION_PROFILE_DECIDED` |
| Điều chỉnh chỉ tiêu giữa kỳ | Nhân viên bị ảnh hưởng (`IMPORTANT`) | `TARGET_REVISED` |
| **Ngoại lệ**: lỗi chưa xử lý (HTTP 500) | Mọi Admin đang hoạt động (`IMPORTANT`), kèm `traceId` | `SYSTEM_ALERT` |
| **Ngoại lệ**: tài khoản bị khóa vì đăng nhập / đổi mật khẩu sai quá số lần | Mọi Admin đang hoạt động (`IMPORTANT`), kèm mã nhân viên, IP | `SYSTEM_ALERT` |

`RATING_ONE_STAR` mới chỉ là loại thông báo (SCR-NTF-01), chưa có luồng nào phát hành. Không có loại thông báo escalation.

Thông báo tự động là **best effort**: nếu không ghi được thì chỉ log cảnh báo và **không** làm hỏng thao tác chính đã lưu. Ngoại lệ ở bảng trên chỉ là cảnh báo cho Admin, không đổi phản hồi lỗi trả cho người gọi.

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

### Phía Angular

- `NotificationService` (`core/notification.service.ts`) gọi REST và giữ kết nối SignalR (`@microsoft/signalr`); kết nối mở khi đăng nhập, đóng khi đăng xuất, tự lấy lại token hết hạn trước khi (tái) kết nối.
- Chuông ở header (`app-notification-bell`): số chưa đọc, danh sách 8 thông báo mới nhất, "Đánh dấu tất cả đã đọc", "Xem tất cả".
- Popup (`app-notification-toasts`) cho thông báo `IMPORTANT` vừa đến, tự tắt sau 8 giây.
- Trang `/notifications`: lọc theo trạng thái đọc, loại, mức độ, từ khóa; phân trang; mở thông báo = đánh dấu đã đọc + đi tới màn hình liên quan; người có `notification:send` gửi thông cáo (tất cả trong phạm vi hoặc chọn người nhận).
- Màn hình mở ra theo `type`/`relatedType` (hàm `notificationLink`): `WORK` → `/my-work?workId=`, `CAMPAIGN` → `/campaigns/:id`, `SUBMISSION_SUBMITTED` → `/review-verify/:id`, `APPEAL_FILED` → `/appeals/department/:id`, `APPEAL_DECIDED` → `/appeals/mine`, `CALIBRATION_SUBMITTED` → `/branch-performance/evaluations/:id/approval`, `RESULT_PUBLISHED` → `/my-evaluation`, v.v.

## 7. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / quá dài `title`, `body`; `severity`, `type` hoặc `relatedType` không hợp lệ; `relatedType` và `relatedId` không đi cùng nhau; người nhận không tồn tại hoặc không `ACTIVE`; không có người nhận nào | 400 | `VALIDATION_ERROR` |
| Chưa đăng nhập | 401 | `UNAUTHORIZED` |
| Không có quyền `notification:send` / `notification:read`; gửi cho người ngoài phạm vi | 403 | `FORBIDDEN` |
| `relatedId` không tồn tại; thông báo không có hoặc không phải của người gọi | 404 | `NOT_FOUND` |
