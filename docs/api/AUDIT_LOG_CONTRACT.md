# Audit Log Contract

Trạng thái: ghi log tự động (EF Core `SaveChangesInterceptor`) + API chỉ đọc (`GET` danh sách, `GET` chi tiết)
**đã triển khai** (backend + màn hình SCR-ADM-05 / SCR-ADM-05-D1). Không có POST/PUT/DELETE.

Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`). Entity: `audit_logs`.

## 1. Cơ chế ghi log

Mỗi lần `AppDbContext.SaveChanges`/`SaveChangesAsync` thành công, với mọi entity ở trạng thái Added/Modified/Deleted
(trừ entity có `[AuditIgnore]` như `AuditLog`, `AuthAuditLog`), hệ thống ghi một dòng `audit_logs`:

- `action`: `Create` | `Update` | `Delete` | `SoftDelete` (Modified mà `IsDeleted` chuyển `false` → `true`).
- `oldValues`/`newValues`: JSON các cột; `Update` chỉ ghi cột thay đổi, `Create`/`Delete` ghi toàn bộ.
- Thuộc tính tên `PasswordHash`, `*Token`, `*Secret` (không phân biệt hoa thường) bị thay bằng `"***"`.
- `actorId`/`actorName`/`ipAddress`/`userAgent`/`httpMethod`/`requestPath`/`traceId`: lấy từ request HTTP hiện tại
  (`null`/`"system"` khi chạy ngoài HTTP, ví dụ DbMigrator/seeder).
- Ghi audit là best-effort: lỗi khi ghi audit chỉ log Warning, không làm hỏng thao tác gốc.
- Entity có `IAuditableEntity` (`CreatedBy/CreatedAt`, `UpdatedBy/UpdatedAt`) được tự điền từ actor hiện tại.

## 2. Quyền

| Endpoint | Quyền (`resource:action`, scope) |
|---|---|
| `GET /audit-logs` | `audit_log:view`, `All` |
| `GET /audit-logs/{id}` | `audit_log:view`, `All` |

`ADMIN` có quyền qua `AUDIT_LOG_VIEW` (seed) hoặc wildcard `FULL_ACCESS`. Không có `POST`/`PUT`/`DELETE /audit-logs`
(404/405 theo routing mặc định — route không tồn tại).

Lỗi chung: 401 không token hoặc tài khoản khóa; 403 thiếu quyền `audit_log:view`; 404 không có bản ghi.

## 3. Danh sách — `GET /audit-logs`

Query params, tất cả tùy chọn:

| Tham số | Kiểu | Mặc định | Ghi chú |
|---|---|---|---|
| `page` | int | 1 | |
| `pageSize` | int | 20 | tối đa 100, vượt quá → 400 `VALIDATION_ERROR` |
| `entityName` | string | — | so khớp đúng (ví dụ `RiskEvent`) |
| `entityId` | string | — | so khớp đúng (dạng chuỗi của khóa chính) |
| `action` | string | — | `Create`/`Update`/`Delete`/`SoftDelete`; giá trị khác → 400 |
| `actorId` | UUID | — | |
| `from`, `to` | ISO 8601 có offset | — | lọc theo `occurredAt`; `from > to` → 400 `VALIDATION_ERROR` |
| `keyword` | string | — | ILIKE trên `entityName`, `entityId`, `actorName` |
| `sort` | string | `occurredAt desc` | hoặc `occurredAt asc` |

200 với `data` là `PagedResult<AuditLogListItemDto>`: `{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }`.

```json
{
  "id": "b6c1a2d3-...",
  "entityName": "RiskEvent",
  "entityId": "6b2b6a4e-...",
  "action": "Create",
  "actorId": "0c1d2e3f-...",
  "actorName": "admin",
  "ipAddress": "127.0.0.1",
  "occurredAt": "2026-10-28T03:00:00+00:00",
  "changedColumns": null
}
```

## 4. Chi tiết — `GET /audit-logs/{id}`

200 với `data` là `AuditLogDetailDto` (các trường của `AuditLogListItemDto` cộng):

```json
{
  "...": "...",
  "userAgent": "curl/8.0",
  "httpMethod": "POST",
  "requestPath": "/risk-events",
  "traceId": "0HN...",
  "changedColumns": ["status", "updatedAt"],
  "oldValues": { "status": "OPEN" },
  "newValues": { "status": "CONFIRMED" }
}
```

404 `NOT_FOUND` nếu `id` không tồn tại.
