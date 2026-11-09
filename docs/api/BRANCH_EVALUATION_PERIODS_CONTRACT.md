# Branch Evaluation Periods Contract (SCR-EVL-03 list)

Trạng thái: `GET /evaluations/branch-periods` **đã triển khai** (backend + frontend `branch-evaluation-list`). Khóa kỳ dùng lại
`POST /evaluations/approvals/finalize` (xem `CALIBRATION_APPROVAL_CONTRACT.md` mục 2.5).

Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`). Nguồn dữ liệu: `evaluation_period`,
`performance_evaluation`, `employee`, `department`. Không có dữ liệu suy diễn hay mặc định: thứ gì chưa có nguồn thì trả `null`.

## 1. `GET /evaluations/branch-periods`

Danh sách kỳ đánh giá (trừ kỳ `DAY`) kèm tiến độ duyệt của các phòng trong chi nhánh của người gọi.

| Mục | Giá trị |
|---|---|
| Quyền | `evaluation:read`, scope `Branch` |
| Phạm vi phòng | Chỉ đếm phòng ban `DEPARTMENT` đang `ACTIVE` thuộc cây của chi nhánh người gọi **và** nằm trong scope `evaluation:read` của họ (`All` = toàn chi nhánh) |
| Chi nhánh | Chi nhánh (nearest `BRANCH` ancestor) của người gọi; nếu không có thì chi nhánh `ACTIVE` đầu tiên theo tên; không có chi nhánh nào là 404 `NOT_FOUND` |

### Query

| Tham số | Kiểu | Mô tả |
|---|---|---|
| `year` | `int?` | Kỳ có `startDate` hoặc `endDate` nằm trong năm này |
| `periodType` | `string?` | `YEAR`, `HALF_YEAR`, `QUARTER`, `MONTH`, `WEEK`; `ALL`/rỗng = không lọc. Giá trị lạ là 400 `VALIDATION_ERROR` |
| `status` | `string?` | Lọc theo `displayStatus`: `IN_PROGRESS`, `EVALUATING`, `IN_REVIEW`, `PUBLISHED`, `LOCKED`; `ALL`/rỗng = không lọc. Giá trị lạ là 400 `VALIDATION_ERROR` |

### 200 OK (`BranchEvaluationPeriodsOverviewDto`)

```json
{
  "success": true,
  "status": 200,
  "data": {
    "branchName": "Chi nhánh Hà Đông",
    "branchCode": "BR-HADONG",
    "availableYears": [2026, 2025],
    "periods": [
      {
        "id": "b2c3d4e5-0000-0000-0000-000000000002",
        "code": "2026-Q3",
        "name": "Quý 3/2026",
        "startDate": "2026-07-01",
        "endDate": "2026-09-30",
        "periodType": "QUARTER",
        "status": "IN_REVIEW",
        "displayStatus": "IN_REVIEW",
        "qualityRatio": null,
        "submittedDepartmentCount": 6,
        "approvedDepartmentCount": 4,
        "totalDepartmentCount": 8,
        "appealWindow": null,
        "canOpen": true,
        "canLock": false,
        "canViewHistory": false
      }
    ]
  }
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `availableYears` | `int[]` | Mọi năm có kỳ (kỳ không phải `DAY`), giảm dần, **không phụ thuộc** các bộ lọc; dùng cho ô chọn năm |
| `periodType` | enum | `YEAR` `HALF_YEAR` `QUARTER` `MONTH` `WEEK` (PgName của `PeriodType`) |
| `status` | enum | Trạng thái thật của kỳ: `OPEN` `IN_REVIEW` `LOCKED` `CLOSED` |
| `displayStatus` | enum | Xem mục 2 |
| `qualityRatio` | `number?` | **Luôn `null`** cho tới khi có cột lưu trong `evaluation_period` (chưa có nguồn). FE hiển thị "-" |
| `appealWindow` | object? | **Luôn `null`** cho tới khi có cột lưu cửa sổ khiếu nại của kỳ (chưa có nguồn). FE hiển thị "-" |
| `submittedDepartmentCount` | int | Số phòng có ít nhất 1 đánh giá `IN_REVIEW`, `APPROVED` hoặc `FINAL` |
| `approvedDepartmentCount` | int | Số phòng có đánh giá và tất cả đều `APPROVED` hoặc `FINAL` |
| `totalDepartmentCount` | int | Số phòng trong phạm vi |
| `canOpen` | bool | `displayStatus == IN_REVIEW` |
| `canLock` | bool | Xem mục 2 (đủ điều kiện finalize) |
| `canViewHistory` | bool | Kỳ đã `LOCKED` hoặc `CLOSED` |

`appealWindow` khi có dữ liệu thật sẽ có dạng `{ "startDate": "yyyy-MM-dd|null", "endDate": "yyyy-MM-dd|null", "isExpired": bool, "isClosed": bool }`.

Lỗi: 401 (chưa đăng nhập), 403 (thiếu `evaluation:read` Branch), 400 `VALIDATION_ERROR`, 404 `NOT_FOUND`.

## 2. Quy tắc `displayStatus`

Thứ tự ưu tiên:

1. `LOCKED`: kỳ `LOCKED` hoặc `CLOSED`.
2. `PUBLISHED`: kỳ chưa khóa, không có đánh giá `DISPUTED` (BR-31), có ít nhất 1 phòng trong phạm vi và `approvedDepartmentCount == totalDepartmentCount`. Đây là trạng thái "sẵn sàng khóa". Domain chưa có bước publish riêng (xem `docs/BE_STATUS.md`).
3. `IN_REVIEW`: có phòng đã gửi (`submittedDepartmentCount > 0`) hoặc kỳ ở `IN_REVIEW`.
4. `EVALUATING`: đã có đánh giá của nhân sự trong phạm vi nhưng chưa phòng nào gửi.
5. `IN_PROGRESS`: chưa có đánh giá nào.

`canLock` = `PUBLISHED`. Khóa kỳ gọi `POST /evaluations/approvals/finalize` (quyền `evaluation:finalize`, scope `Branch`), có thể trả 409
`EVALUATION_DISPUTE_PENDING` hoặc 409 `CONFLICT` nếu kỳ đã khóa; FE phải hiển thị lỗi thật, không giả lập thành công.

## 3. Phần chưa có nguồn dữ liệu (chờ xác nhận sản phẩm)

- **Quality ratio** theo kỳ do System Admin đặt: cần cột mới trên `evaluation_period` (cần migration).
- **Cửa sổ khiếu nại** của kỳ (ngày mở/đóng): cần cột mới hoặc cấu hình; hiện chỉ có hạn 48 giờ theo từng khiếu nại (`APPEAL_WINDOW_EXPIRED`).
- **Trạng thái Published thật** và **màn hình SCR-EVL-03 workspace / lịch sử**: chưa có route frontend. Nút "Mở" và "Xem" điều hướng tới
  `/evaluations?periodId={id}` (danh sách đánh giá của kỳ đó).

## 4. Frontend

`EvaluationService.branchPeriods(year, periodType, status)` truyền các bộ lọc lên API; frontend không lọc lại phía client. Năm mặc định là năm hiện tại,
danh sách năm lấy từ chính các kỳ trả về cộng năm hiện tại. Lỗi hiển thị theo loại: mất mạng (status 0), 403, lỗi khác.
