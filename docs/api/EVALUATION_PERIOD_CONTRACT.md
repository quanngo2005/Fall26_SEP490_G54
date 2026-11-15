# Evaluation Period Contract

Trạng thái: Tạo kỳ, đóng kỳ (pre-close) và truy vấn danh sách/chi tiết kỳ (`GET /evaluation-periods`) **đã triển khai** (backend). Khóa kỳ vẫn là `POST /evaluations/approvals/finalize` (xem `EVALUATION_CONTRACT.md`). Chưa có sửa/xóa kỳ, chưa có carry-over kết quả In Review sang kỳ sau, chưa có màn hình frontend.

Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`). Entity: `evaluation_period`.

## 1. Vòng đời

```
OPEN ──close──▶ IN_REVIEW ──finalize──▶ LOCKED
```

- Kỳ mới tạo luôn ở `OPEN`. `close` chỉ nhận kỳ `OPEN`; trạng thái khác là 409 `EVALUATION_PERIOD_INVALID_STATE`.
- Lock (finalize) đóng băng `performance_evaluation` của kỳ: `status = FINAL`, `approvedAt` được ghi, `total_score` giữ nguyên. Không có bảng `performance_summary` riêng; snapshot chính là các dòng `performance_evaluation` đã `FINAL`.

## 2. Quyền

| Endpoint | Quyền (`resource:action`, scope) |
|---|---|
| `GET /evaluation-periods` | `evaluation:read`, `Branch` |
| `GET /evaluation-periods/{id}` | `evaluation:read`, `Branch` |
| `POST /evaluation-periods` | `evaluation:finalize`, `Branch` |
| `POST /evaluation-periods/{id}/close` | `evaluation:finalize`, `Branch` |

Lỗi chung: 401 không token; 403 thiếu quyền.

## 3. `GET /evaluation-periods`

Truy vấn danh sách kỳ đánh giá có phân trang, tìm kiếm và lọc.

### Query Parameters

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `keyword` | `string` | `null` | Tìm kiếm không phân biệt hoa thường theo `code` hoặc `name`. |
| `periodType` | `string` | `null` | Lọc theo loại kỳ (`YEAR`, `HALF_YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`). Sai giá trị trả về 400 `VALIDATION_ERROR`. |
| `status` | `string` | `null` | Lọc theo trạng thái (`OPEN`, `IN_REVIEW`, `LOCKED`, `CLOSED`). Sai giá trị trả về 400 `VALIDATION_ERROR`. |
| `parentPeriodId` | `uuid` | `null` | Lọc danh sách kỳ con trực tiếp của một kỳ cha. |
| `year` | `int` | `null` | Lọc các kỳ có ngày bắt đầu hoặc kết thúc thuộc năm chỉ định (1900 - 2100). |
| `page` | `int` | `1` | Trang hiện tại (tối thiểu 1). |
| `pageSize` | `int` | `20` | Số lượng phần tử mỗi trang (1 - 100). |

Sắp xếp mặc định: `startDate DESC`, `code ASC` (xác định, nhất quán).

### Response 200 OK

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "5eed0000-0000-0000-000f-000000000003",
        "code": "2026-M03",
        "name": "Kỳ đánh giá tháng 03/2026",
        "periodType": "MONTH",
        "parentPeriodId": "5eed0000-0000-0000-000f-000000000002",
        "parentPeriodCode": "2026-Q1",
        "parentPeriodName": "Quý 1/2026",
        "startDate": "2026-03-01",
        "endDate": "2026-03-31",
        "status": "OPEN",
        "updatedAt": "2026-01-01T00:00:00Z"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "trace-id"
}
```

## 4. `GET /evaluation-periods/{id}`

Lấy chi tiết một kỳ đánh giá theo ID.

### Response 200 OK

```json
{
  "success": true,
  "status": 200,
  "data": {
    "id": "5eed0000-0000-0000-000f-000000000003",
    "code": "2026-M03",
    "name": "Kỳ đánh giá tháng 03/2026",
    "periodType": "MONTH",
    "parentPeriodId": "5eed0000-0000-0000-000f-000000000002",
    "parentPeriodCode": "2026-Q1",
    "parentPeriodName": "Quý 1/2026",
    "startDate": "2026-03-01",
    "endDate": "2026-03-31",
    "status": "OPEN",
    "updatedAt": "2026-01-01T00:00:00Z"
  },
  "traceId": "trace-id"
}
```

Lỗi: 404 `NOT_FOUND` nếu không tồn tại kỳ đánh giá với `id` yêu cầu.

## 5. `POST /evaluation-periods`

Body:

```json
{ "code": "2026-10", "name": "Tháng 10/2026", "periodType": "MONTH", "parentPeriodId": null, "startDate": "2026-10-01", "endDate": "2026-10-31" }
```

- `code` ≤ 50 ký tự, duy nhất; `name` ≤ 150; `periodType` ∈ `YEAR | HALF_YEAR | QUARTER | MONTH | WEEK | DAY`; `endDate` ≥ `startDate`. Sai là 400 `VALIDATION_ERROR`.
- Phân cấp (khớp trigger `validate_evaluation_period`): `YEAR` không có kỳ cha; loại khác bắt buộc có `parentPeriodId`, kỳ cha phải đúng cấp trực tiếp (`HALF_YEAR`→`YEAR`, `QUARTER`→`HALF_YEAR`, `MONTH`→`QUARTER`, `DAY`→`MONTH`; `WEEK` chỉ cần có kỳ cha) và ngày của kỳ nằm trong ngày của kỳ cha. Vi phạm là 400 `VALIDATION_ERROR`.
- `parentPeriodId` không tồn tại: 404.
- Trùng `code`: 409 `CONFLICT`.

201: `data` = `{ id, code, name, periodType, parentPeriodId, startDate, endDate, status: "OPEN" }`.

## 6. `POST /evaluation-periods/{id}/close`

Không có body. Chuyển `OPEN` → `IN_REVIEW` và trả kết quả pre-close check:

```json
{ "periodId": "…", "status": "IN_REVIEW", "pendingInReviewResults": 2, "openDisputes": 1 }
```

| Trường | Ý nghĩa |
|---|---|
| `pendingInReviewResults` | result submission `SUBMITTED` / `UNDER_REVIEW` nộp trong khoảng ngày của kỳ (UTC+7). Không chặn việc đóng kỳ |
| `openDisputes` | appeal `OPEN` / `UNDER_REVIEW` gắn với evaluation của kỳ, hoặc với result nộp trong kỳ. Không chặn việc đóng kỳ, nhưng lock vẫn bị chặn bởi BR-31 (409 `EVALUATION_DISPUTE_PENDING`) cho đến khi xử lý hết |

Lỗi: 404 không có kỳ; 409 `EVALUATION_PERIOD_INVALID_STATE`.
