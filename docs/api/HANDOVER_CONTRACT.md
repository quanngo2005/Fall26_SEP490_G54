# API Contract & Error Mapping — Handover (Bàn giao công việc)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: endpoint tạo bàn giao và danh sách người nhận **đã implement ở BE**. FE chỉ có dialog tạo bàn giao.

## 1. Phạm vi

**BR-21: không có bước duyệt và không có hạn chấp nhận.** Bàn giao có hiệu lực ngay khi tạo, người nhận bắt buộc nhận.

Hai người được phép bàn giao, cùng dùng `POST /works/{workId}/handovers`:

| Ai tạo | Điều kiện |
|---|---|
| **Nhân viên** là assignee hiện tại | Quyền `handover:create` |
| **Quản lý trực tiếp** của assignee | Quyền `handover:create` và `handover:set_share` |

- Kết quả luôn như nhau: yêu cầu lưu `ACCEPTED`, công việc chuyển sang người nhận và sang `HANDED_OVER` trong cùng lần lưu.
- Không có endpoint chấp nhận, từ chối, duyệt, đổi người nhận hay hủy. Chưa có danh sách / chi tiết yêu cầu.
- Bản ghi `PENDING` / `REJECTED` / `CANCELLED` còn trong DB từ trước không còn endpoint nào xử lý; DB giữ nguyên.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Route

| Method | Route | Body | Thành công |
|---|---|---|---|
| GET | `/handovers/recipients` | — | 200 `HandoverRecipientDto[]`: nhân viên `ACTIVE` cùng phòng ban với người gọi (trừ chính họ), sắp theo tên. Quyền `handover:create`. Trường: `id`, `employeeCode`, `fullName`, `jobPositionTitle` |
| POST | `/works/{workId}/handovers` | JSON `CreateHandoverRequestDto` | 201 `HandoverDetailDto`, `status = ACCEPTED`, message `HANDOVER_TRANSFERRED` |

Tất cả yêu cầu `Authorization: Bearer` của nhân viên đang hoạt động.

### Quyền

| Quyền | Dùng cho | ADMIN | MANAGER | EMPLOYEE |
|---|---|---|---|---|
| `handover:create` | Bàn giao công việc | `All` | `Own` | `Own` |
| `handover:set_share` | Gửi `referrerSharePercent`; quản lý bàn giao việc của nhân viên | `All` | `Own` | không có |

Các quyền được seed bởi migration `SeedHandoverCreateAccess` (và `seed_dev_data.sql` cho môi trường dev); chỉ gán cho role đã tồn tại. Quyền chỉ cho phép gọi API — việc chỉ assignee hoặc quản lý trực tiếp của assignee mới được bàn giao vẫn kiểm tra trong service (403 `FORBIDDEN`).

## 3. Bàn giao — `POST /works/{workId}/handovers`

### Body

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| `toEmployeeId` | có | Nhân viên nhận, khác assignee hiện tại, đang `ACTIVE` |
| `reason` | có | Sau khi cắt khoảng trắng: 1–2000 ký tự |
| `amount` | có | Giá trị tiền của việc bàn giao, `>= 0`. Chỉ dùng để kiểm tra ngưỡng, **không lưu** |
| `referrerSharePercent` | không | 0–100: tỷ lệ điểm KPI người giao (người giới thiệu) giữ lại. **Chỉ Manager/Admin** (quyền `handover:set_share`) được gửi trường này, người khác gửi sẽ bị 403. Không gửi thì người nhận bàn giao nhận 100% (lưu `0`) |

### Quy tắc nghiệp vụ

1. Người gọi phải là **assignee hiện tại**, hoặc **quản lý trực tiếp** của assignee (`employee.managerId` của assignee là người gọi) **và** có quyền `handover:set_share` (Manager). Admin không phải quản lý trực tiếp thì không bàn giao được (giống hủy / hoàn tất công việc).
2. Công việc phải ở `NEW` hoặc `IN_PROGRESS` (theo `WorkStateMachine`, action `Handover`).
3. **BR-01 — ngưỡng số tiền:** `amount` lớn hơn `Handover:AmountThreshold` thì bị chặn (cấu hình trong `appsettings.json` hoặc biến môi trường `Handover__AmountThreshold`; mặc định 500.000.000). `amount` đúng bằng ngưỡng vẫn hợp lệ.
4. Trong cùng một lần lưu:
   - lưu yêu cầu `ACCEPTED`, `acceptedAt` = giờ máy chủ; `fromEmployeeId` là assignee cũ; `approvedBy` để trống;
   - công việc đổi `assigneeEmployeeId` sang người nhận và sang `HANDED_OVER`; người nhận sau đó bắt đầu làm bằng `PATCH /works/{id}/status` (`Start`, `HANDED_OVER` → `IN_PROGRESS`).
5. Điểm KPI sau này được chia theo `referrerSharePercent` của yêu cầu `ACCEPTED` mới nhất (xem `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`).

## 4. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| Thiếu / sai `toEmployeeId`, `reason`, `amount`, `referrerSharePercent`; bàn giao cho chính assignee hiện tại; người nhận không `ACTIVE` | 400 | `VALIDATION_ERROR` |
| Không có quyền `handover:create`; không phải assignee hoặc quản lý trực tiếp của assignee; gửi `referrerSharePercent` mà không có `handover:set_share` | 403 | `FORBIDDEN` |
| Không có công việc / người nhận | 404 | `NOT_FOUND` |
| Công việc không ở trạng thái cho phép bàn giao | 409 | `WORK_INVALID_STATE` |
| `amount` vượt ngưỡng (BR-01) | 422 | `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD` |

## 5. Ví dụ

```http
POST /works/6c1f.../handovers
{ "toEmployeeId": "5eed0000-0000-0000-0007-000000000005", "reason": "Khách hàng ở khu vực nhóm 2", "amount": 120000000 }
```
