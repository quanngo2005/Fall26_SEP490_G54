# API Contract & Error Mapping — Handover (Yêu cầu bàn giao & chuyển việc)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: hai luồng bàn giao (`A -> A1` đổi việc cùng role, `A -> B` chuyển việc khác role), hàng chờ pool và cấu hình admin **đã implement ở BE**.

## 1. Phạm vi

Hệ thống phân biệt hai luồng bàn giao qua **vai trò (role)** của người giao (`A`) và người/nhóm nhận (`A1` hoặc `B`), cùng dùng `POST /works/{workId}/handovers`:

| Luồng | Điều kiện phân loại | Hành vi |
|---|---|---|
| **Luồng 1 — Đổi việc (`A -> A1`)** | Người nhận `A1` có **cùng tập role đang hoạt động** với `A` | Lưu yêu cầu `PENDING`, gửi thông báo đến quản lý trực tiếp của `A` và đến `A1`. `A1` (hoặc quản lý trực tiếp của `A`) có thể **chấp nhận** (`ACCEPTED`, việc chuyển sang `A1` và `HANDED_OVER`) hoặc **từ chối** (`REJECTED`, việc trả về giữ nguyên ở `A`). |
| **Luồng 2 — Chuyển việc (`A -> B`)** | Người nhận `B` (hoặc `targetRoleCode`) có **role khác** với `A` | Kiểm tra tiến độ công việc (`work.progressPercent`) của `A` phải đạt tối thiểu ngưỡng cấu hình (`transferMinProgressPercent`, mặc định `90%`; nếu thấp hơn trả 422 `HANDOVER_PROGRESS_BELOW_THRESHOLD`). `B` **không có quyền từ chối** (chuyển là chuyển). Cách phân công `B` phụ thuộc cấu hình Admin (`transferAssignmentMode`):<br/>- **`RANDOM`**: hệ thống chọn ngẫu nhiên một nhân viên `B` đang `ACTIVE` thuộc role đích (ưu tiên cùng phòng ban), lưu `ACCEPTED` và chuyển việc ngay (`HANDED_OVER`).<br/>- **`ASSIGNED`**: lưu yêu cầu ở trạng thái `IN_POOL` (`toEmployeeId = null`), công việc tạm giữ ở `A`, gửi thông báo đến quản lý trực tiếp của `A` để quản lý tự chỉ định `B` từ pool (`POST /handovers/{id}/manager/reassign`). |
| **Quản lý trực tiếp chuyển thẳng** | Người gọi là quản lý trực tiếp của `A` (có quyền `handover:set_share`) | Chuyển ngay (`ACCEPTED`, `approvedBy` = quản lý), người nhận bắt buộc nhận. |

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/handovers/recipients` | — | 200 `HandoverRecipientDto[]`: nhân viên `ACTIVE` cùng phòng ban với người gọi (trừ chính họ), sắp theo tên. Quyền `handover:create` |
| GET | `/handovers/pool` | — | 200 `HandoverDetailDto[]`: các yêu cầu `IN_POOL` của nhân viên cấp dưới trực tiếp chờ quản lý phân công. Quyền `handover:manage` (`Own`) |
| GET | `/handovers/settings` | — | 200 `HandoverSettingsDto`: cấu hình `transferAssignmentMode` (`RANDOM` / `ASSIGNED`) và `transferMinProgressPercent`. Quyền `handover:manage` (`All` — Admin) |
| PUT | `/handovers/settings` | JSON `UpdateHandoverSettingsRequestDto` | 200 `HandoverSettingsDto`, message `HANDOVER_SETTINGS_UPDATED`. Quyền `handover:manage` (`All` — Admin) |
| POST | `/works/{workId}/handovers` | JSON `CreateHandoverRequestDto` | 201 `HandoverDetailDto`. `PENDING` → `HANDOVER_REQUESTED`; `ACCEPTED` → `HANDOVER_TRANSFERRED`; `IN_POOL` → `HANDOVER_POOLED` |
| POST | `/handovers/{id}/accept` | — | 200 `HandoverDetailDto` |
| POST | `/handovers/{id}/reject` | — | 200 `HandoverDetailDto` |
| POST | `/handovers/{id}/manager/approve` | — | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reassign` | JSON `ReassignHandoverRequestDto` | 200 `HandoverDetailDto`, message `HANDOVER_TRANSFERRED` |
| POST | `/handovers/{id}/manager/reject` | — | 200 `HandoverDetailDto`, message `HANDOVER_REJECTED` |

Tất cả yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động.

### Quyền

| Quyền | Dùng cho | ADMIN | MANAGER | EMPLOYEE |
|---|---|---|---|---|
| `handover:create` | Tạo yêu cầu bàn giao / chuyển việc | `All` | `Own` | `Own` |
| `handover:set_share` | Gửi `referrerSharePercent` khi tạo | `All` | `Own` | không có |
| `handover:respond` | Người nhận `A1` chấp nhận / từ chối | `All` | `Own` | `Own` |
| `handover:manage` | Quản lý duyệt / chỉ định từ pool / từ chối (`Own`); Admin cấu hình (`All`) | `All` | `Own` | không có |

## 3. Tạo yêu cầu — `POST /works/{workId}/handovers`

### Body

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có (trừ khi gửi `targetRoleCode` ở luồng `A -> B`) | Nhân viên nhận, khác assignee hiện tại, đang `ACTIVE` |
| `targetRoleCode` | không | Mã role đích (ví dụ `VERIFIER`) khi chuyển việc khác role (`A -> B`) mà không chỉ định sẵn `toEmployeeId` |
| `reason` | có | Sau khi cắt khoảng trắng: 1–2000 ký tự |
| `amount` | có | Giá trị tiền của việc bàn giao, `>= 0`. Chỉ dùng để kiểm tra ngưỡng BR-01, **không lưu** |
| `referrerSharePercent` | không | 0–100: tỷ lệ điểm KPI người giao giữ lại. **Chỉ Manager/Admin** (`handover:set_share`) được gửi trường này |

## 4. Chấp nhận / từ chối (`A -> A1`) — `POST /handovers/{id}/accept|reject`

1. Chỉ **người nhận** (`toEmployeeId`) của yêu cầu `PENDING` được trả lời (403 nếu không phải). Yêu cầu luồng `A -> B` đã `ACCEPTED` hoặc `IN_POOL` không thể accept/reject (409 `HANDOVER_NOT_PENDING`).
2. **Accept:** yêu cầu → `ACCEPTED`, `acceptedAt` = giờ máy chủ; công việc chuyển `assigneeEmployeeId` sang `A1` và sang `HANDED_OVER`.
3. **Reject:** yêu cầu → `REJECTED`; công việc giữ nguyên ở `A`.

## 5. Quản lý duyệt / chỉ định từ pool / từ chối — `POST /handovers/{id}/manager/approve|reassign|reject`

1. Người gọi phải là **quản lý trực tiếp của người giao** (`fromEmployee.managerId`).
2. Yêu cầu phải đang ở `PENDING` hoặc `IN_POOL`, nếu không trả 409 `HANDOVER_NOT_PENDING`.
3. **approve:** áp dụng cho yêu cầu `PENDING` đã có `toEmployeeId` (400 nếu yêu cầu đang `IN_POOL` chưa có người nhận).
4. **reassign:** dùng cho cả yêu cầu `PENDING` (đổi người nhận) và `IN_POOL` (quản lý chỉ định `B` từ pool); chuyển trạng thái sang `ACCEPTED`, `approvedBy` = quản lý, công việc chuyển sang người nhận mới và `HANDED_OVER`.
5. **reject:** chuyển yêu cầu sang `REJECTED`, công việc giữ nguyên ở `A`.

## 6. Cấu hình Admin — `GET|PUT /handovers/settings`

| Trường | Quy tắc |
|---|---|
| `transferAssignmentMode` | `"RANDOM"` (mặc định) hoặc `"ASSIGNED"` |
| `transferMinProgressPercent` | `0`–`100` (mặc định `90`) |

## 7. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / sai dữ liệu đầu vào; bàn giao cho chính assignee hiện tại; người nhận không `ACTIVE`; approve yêu cầu `IN_POOL` chưa có người nhận | 400 | `VALIDATION_ERROR` |
| Không đủ quyền hoặc không phải assignee / quản lý trực tiếp / người nhận | 403 | `FORBIDDEN` |
| Không tìm thấy công việc / người nhận / yêu cầu bàn giao | 404 | `NOT_FOUND` |
| Công việc không ở trạng thái cho phép bàn giao (`NEW` / `IN_PROGRESS`) | 409 | `WORK_INVALID_STATE` |
| Đã có yêu cầu `PENDING` hoặc `IN_POOL` | 409 | `HANDOVER_ALREADY_PENDING` |
| Yêu cầu không còn ở trạng thái chờ xử lý (`PENDING` / `IN_POOL`) | 409 | `HANDOVER_NOT_PENDING` |
| `amount` vượt ngưỡng (BR-01) | 422 | `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD` |
| Tiến độ công việc (`progressPercent`) thấp hơn ngưỡng cấu hình khi chuyển việc khác role (`A -> B`) | 422 | `HANDOVER_PROGRESS_BELOW_THRESHOLD` |
| Không có nhân viên `ACTIVE` nào thuộc role đích khi chuyển việc chế độ `RANDOM` | 422 | `HANDOVER_NO_ELIGIBLE_RECIPIENT` |
