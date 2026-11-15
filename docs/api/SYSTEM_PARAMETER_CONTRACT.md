# System Parameter Contract (SCR-ADM-04)

Trạng thái: backend + frontend triển khai. Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`), không
phân trang. Bảng `system_parameters` (`key` unique, `value` text, cột audit từ `BaseEntity`). Mọi thay đổi được ghi tự
động vào `audit_logs` (`entityName = SystemParameter`, xem `AUDIT_LOG_CONTRACT.md`).

> Lịch sử: bản đầu (GB-326) đã bị gỡ để dùng `appsettings`; contract này đưa lại màn hình quản trị nhưng giữ
> `appsettings` (`SystemParameters:*`) làm giá trị mặc định khi chưa có dòng DB.

## 1. Danh mục tham số

Danh mục (key, khoảng hợp lệ, khóa) nằm trong code (`SystemParameterCatalog`); DB chỉ lưu giá trị admin đã đặt. Dòng DB sai
kiểu/ngoài khoảng bị bỏ qua khi nạp.

| key | đơn vị | khoảng / quy tắc | Consumer |
|---|---|---|---|
| `manualApprovalHours` | giờ | 1–168 | chưa có (lưu + audit) |
| `importMaxFileMb` | MB | 1–10 | `DataImportsController`, `TargetAssignmentController` |
| `importFormats` | text | khóa, `.xlsx` | — |
| `kpiLockTime` | giờ | khóa, `23:59:59` (BR-29) | — |
| `appealWindowDays` | ngày | 1–30 | chưa có (lưu + audit) |
| `preCloseCheckDays` | ngày | 1–30 | chưa có (lưu + audit) |
| `appealRejectedHours` | giờ | khóa, giá trị từ `appsettings` (mặc định 48, BR-31) | `AppealService` |
| `handoverThresholdVnd` | VND | số nguyên > 0 | `HandoverService` (BR-01; 422 `HANDOVER_AMOUNT_EXCEEDS_THRESHOLD`) |

Giá trị đổi có hiệu lực ngay trên instance xử lý `PUT`; instance khác nạp lại khi khởi động hoặc khi gọi `GET`.

## 2. Quyền

| Endpoint | Quyền (`resource:action`, scope) |
|---|---|
| `GET /system-parameters` | `system_parameter:view`, `All` |
| `PUT /system-parameters` | `system_parameter:update`, `All` |

Seed: `ADMIN`, `SYSTEM_ADMIN`. Lỗi chung: 401 không token; 403 thiếu quyền.

## 3. `GET /system-parameters`

200 `data` là mảng theo thứ tự danh mục:

```json
[
  { "key": "importMaxFileMb", "value": 5, "min": 1, "max": 10, "locked": false,
    "updatedAt": "2026-11-05T03:00:00+00:00", "updatedBy": "sa.admin" },
  { "key": "importFormats", "value": ".xlsx", "min": null, "max": null, "locked": true,
    "updatedAt": null, "updatedBy": null }
]
```

`value`: số cho tham số sửa được, chuỗi cho tham số khóa (trừ `appealRejectedHours` là số). `max = null` nghĩa là không
giới hạn trên. `updatedAt`/`updatedBy` (username) là `null` khi chưa từng chỉnh.

## 4. `PUT /system-parameters`

Cập nhật nguyên tử (tất cả hợp lệ mới lưu); tham số không có trong yêu cầu giữ nguyên.

```json
{ "changes": [ { "key": "importMaxFileMb", "value": 8 } ] }
```

- 200: `data` như `GET` (đầy đủ sau cập nhật).
- 400 `VALIDATION_ERROR` (`errors[]` từng lỗi): danh sách rỗng, key không tồn tại hoặc trùng, key bị khóa, giá trị không
  nguyên hoặc ngoài `[min, max]`.
