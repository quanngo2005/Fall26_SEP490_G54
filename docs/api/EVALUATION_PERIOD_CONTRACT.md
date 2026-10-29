# Evaluation Period Contract

Trạng thái: tạo kỳ và đóng kỳ (pre-close) **đã triển khai** (backend). Khóa kỳ vẫn là `POST /evaluations/approvals/finalize` (xem `EVALUATION_CONTRACT.md`). Chưa có sửa/xóa kỳ, chưa có carry-over kết quả In Review sang kỳ sau, chưa có màn hình frontend.

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
| `POST /evaluation-periods` | `evaluation:finalize`, `Branch` |
| `POST /evaluation-periods/{id}/close` | `evaluation:finalize`, `Branch` |

Lỗi chung: 401 không token; 403 thiếu quyền.

## 3. `POST /evaluation-periods`

Body:

```json
{ "code": "2026-10", "name": "Tháng 10/2026", "periodType": "MONTH", "parentPeriodId": null, "startDate": "2026-10-01", "endDate": "2026-10-31" }
```

- `code` ≤ 50 ký tự, duy nhất; `name` ≤ 150; `periodType` ∈ `YEAR | HALF_YEAR | QUARTER | MONTH | WEEK | DAY`; `endDate` ≥ `startDate`. Sai là 400 `VALIDATION_ERROR`.
- Phân cấp (khớp trigger `validate_evaluation_period`): `YEAR` không có kỳ cha; loại khác bắt buộc có `parentPeriodId`, kỳ cha phải đúng cấp trực tiếp (`HALF_YEAR`→`YEAR`, `QUARTER`→`HALF_YEAR`, `MONTH`→`QUARTER`, `DAY`→`MONTH`; `WEEK` chỉ cần có kỳ cha) và ngày của kỳ nằm trong ngày của kỳ cha. Vi phạm là 400 `VALIDATION_ERROR`.
- `parentPeriodId` không tồn tại: 404.
- Trùng `code`: 409 `CONFLICT`.

201: `data` = `{ id, code, name, periodType, parentPeriodId, startDate, endDate, status: "OPEN" }`.

## 4. `POST /evaluation-periods/{id}/close`

Không có body. Chuyển `OPEN` → `IN_REVIEW` và trả kết quả pre-close check:

```json
{ "periodId": "…", "status": "IN_REVIEW", "pendingInReviewResults": 2, "openDisputes": 1 }
```

| Trường | Ý nghĩa |
|---|---|
| `pendingInReviewResults` | result submission `SUBMITTED` / `UNDER_REVIEW` nộp trong khoảng ngày của kỳ (UTC+7). Không chặn việc đóng kỳ |
| `openDisputes` | appeal `OPEN` / `UNDER_REVIEW` gắn với evaluation của kỳ, hoặc với result nộp trong kỳ. Không chặn việc đóng kỳ, nhưng lock vẫn bị chặn bởi BR-31 (409 `EVALUATION_DISPUTE_PENDING`) cho đến khi xử lý hết |

Lỗi: 404 không có kỳ; 409 `EVALUATION_PERIOD_INVALID_STATE`.
