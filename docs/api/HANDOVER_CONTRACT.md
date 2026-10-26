# API Contract & Error Mapping — Handover (Yêu cầu bàn giao công việc)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: endpoint tạo yêu cầu **đã implement ở BE**, FE chưa có.

## 1. Phạm vi

- Người đang được giao công việc tạo một **yêu cầu bàn giao** (`task_handover`, `PENDING`) cho nhân viên khác.
- Chưa có endpoint chấp nhận / từ chối / hủy yêu cầu. Tạo yêu cầu **không** đổi trạng thái công việc (`HANDED_OVER` sẽ do luồng chấp nhận quyết định).

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Body | Thành công |
|---|---|---|---|
| POST | `/works/{workId}/handovers` | JSON `CreateHandoverRequestDto` | 201 `HandoverDetailDto`, message `MSG05` |

Yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động và quyền `handover:create` (scope `Own`).

| Role | Scope `handover:create` |
|---|---|
| ADMIN | `All` |
| MANAGER | `Own` |
| EMPLOYEE | `Own` |

Quyền `handover:set_share` (đặt `referrerSharePercent`): ADMIN `All`, MANAGER `Own`; EMPLOYEE không có.

Hai quyền được seed bởi migration `SeedHandoverCreateAccess` (và `seed_dev_data.sql` cho môi trường dev); chỉ gán cho role đã tồn tại. Quyền chỉ cho phép gọi API — việc chỉ assignee mới được bàn giao vẫn kiểm tra trong service (403 `FORBIDDEN`).

### Body

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Nhân viên nhận, khác người giao, đang `ACTIVE` |
| `reason` | có | Sau khi cắt khoảng trắng: 1–2000 ký tự |
| `amount` | có | Giá trị tiền của việc bàn giao, `>= 0`. Chỉ dùng để kiểm tra ngưỡng, **không lưu** |
| `referrerSharePercent` | không | 0–100: tỷ lệ điểm KPI người giao (người giới thiệu) giữ lại. **Chỉ Manager/Admin** (quyền `handover:set_share`) được gửi trường này, người khác gửi sẽ bị 403. Không gửi thì người nhận bàn giao nhận 100% (lưu `0`) |

### Quy tắc nghiệp vụ

1. Chỉ **assignee hiện tại** của công việc được yêu cầu bàn giao.
2. Công việc phải ở `NEW` hoặc `IN_PROGRESS` (theo `WorkStateMachine`, action `Handover`).
3. **BR-01 — ngưỡng số tiền:** `amount` lớn hơn 500.000.000 thì bị chặn (hằng số trong `HandoverService`). `amount` đúng bằng ngưỡng vẫn hợp lệ.
4. Công việc không được có yêu cầu bàn giao `PENDING` khác. Ràng buộc được bảo đảm cả ở DB bằng partial unique index `ux_task_handover_pending_work`, nên hai request đồng thời cũng chỉ một request thành công, request còn lại nhận 409.

## 3. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / sai `toEmployeeId`, `reason`, `amount`, `referrerSharePercent`; tự bàn giao cho mình; người nhận không `ACTIVE` | 400 | `VALIDATION_ERROR` |
| Không có quyền `handover:create`, không phải assignee, hoặc gửi `referrerSharePercent` mà không có quyền `handover:set_share` | 403 | `FORBIDDEN` |
| Không có công việc / người nhận | 404 | `NOT_FOUND` |
| Công việc không ở trạng thái cho phép bàn giao | 409 | `WORK_INVALID_STATE` |
| Đã có yêu cầu `PENDING` | 409 | `HANDOVER_ALREADY_PENDING` |
| `amount` vượt ngưỡng (BR-01) | 422 | `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD` |

## 4. Ví dụ

```http
POST /works/6c1f.../handovers
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000005", "reason": "Khách hàng ở khu vực nhóm 2", "amount": 120000000 }
```
