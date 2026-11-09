# Risk Event Contract

Trạng thái: tạo (có ghi nhận ngay), xem chi tiết, xác nhận, bác bỏ, giải quyết, **danh sách, chi tiết cho drawer, đính kèm và đảo sự kiện** (§9) **đã triển khai** (backend, unit-test; chưa chạy qua HTTP).

Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`). Entity: `risk_event`, `performance_contribution`.

## 1. Vòng đời

```
OPEN ──confirm──▶ CONFIRMED ──resolve──▶ RESOLVED
  └────dismiss──▶ DISMISSED
```

- Chỉ đi theo các mũi tên trên; mọi chuyển trạng thái khác là 409 `RISK_EVENT_INVALID_STATE`. `DISMISSED` và `RESOLVED` là trạng thái cuối.
- `CONFIRMED` không thể bác bỏ: dòng contribution âm là sổ ghi không sửa, không có bút toán đảo.
- Người thao tác (tạo / xác nhận / bác bỏ / giải quyết) **không được là nhân viên bị ghi nhận**: 403 `FORBIDDEN`.
- `risk_event` không có cột lưu người xác nhận hay lý do bác bỏ; hệ thống chỉ ghi log audit (`Risk event audit: Action=...`). Chỉ `recordedBy` (người tạo) được lưu.

## 2. Quyền

| Endpoint | Quyền (`resource:action`, scope) |
|---|---|
| `POST /risk-events` | `risk_event:create`, `All` |
| `GET /risk-events/{id}` | `risk_event:read`, `All` |
| `POST /risk-events/{id}/confirm` | `risk_event:confirm`, `All` |
| `POST /risk-events/{id}/dismiss` | `risk_event:dismiss`, `All` |
| `POST /risk-events/{id}/resolve` | `risk_event:resolve`, `All` |

`ADMIN` có quyền qua wildcard. Các quyền `risk_event:*` chưa được seed cho role QA nào, cần cấp riêng.

Lỗi chung: 401 không token hoặc tài khoản khóa; 403 thiếu quyền hoặc thao tác trên sự kiện của chính mình; 404 không có risk event.

## 3. DTO `RiskEventDto`

```json
{
  "id": "6b2b6a4e-5f1d-4f0e-9a39-0d0c3c5e7a11",
  "code": "RISK-20261028-3F9A1C07",
  "employeeId": "33a8b399-33ba-4116-a26d-b913c2703309",
  "recordedBy": "0c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f",
  "eventType": "FRAUD",
  "description": "Giao dịch sai quy định",
  "severity": "HIGH",
  "status": "OPEN",
  "occurredAt": "2026-10-20T03:00:00+00:00",
  "resolvedAt": null,
  "updatedAt": "2026-10-28T03:00:00+00:00",
  "contributionId": null
}
```

`severity` ∈ `LOW | MEDIUM | HIGH | CRITICAL`; `status` ∈ `OPEN | CONFIRMED | DISMISSED | RESOLVED`. `resolvedAt` chỉ có giá trị khi `RESOLVED`. `contributionId` là id dòng contribution âm, `null` khi chưa `CONFIRMED`. `code` do hệ thống sinh, duy nhất, dạng `RISK-yyyyMMdd-XXXXXXXX` (ngày UTC lúc tạo).

## 4. Tạo — `POST /risk-events`

Body (`CreateRiskEventRequestDto`), mọi trường bắt buộc:

| Trường | Kiểu | Quy tắc |
|---|---|---|
| `employeeId` | UUID | nhân viên bị ghi nhận; phải tồn tại; không được là người gọi |
| `eventType` | string | không rỗng, tối đa 50 ký tự (đã cắt khoảng trắng đầu/cuối) |
| `description` | string | không rỗng, tối đa 2000 ký tự |
| `severity` | string | `LOW / MEDIUM / HIGH / CRITICAL`, không phân biệt hoa thường |
| `occurredAt` | ISO 8601 có offset | không được ở tương lai |

Thành công: **201** với `Location: /risk-events/{id}`, envelope `status: 201`, `data` là `RiskEventDto` ở trạng thái `OPEN`, `recordedBy` là người gọi.

| Điều kiện | Kết quả |
|---|---|
| Thiếu / sai trường | 400 `VALIDATION_ERROR`, `errors[]` liệt kê mọi lỗi cùng lúc |
| `employeeId` là người gọi | 403 `FORBIDDEN` |
| `employeeId` không tồn tại | 404 `NOT_FOUND` |
| Trùng mã sinh ra (cực hiếm) | 409 `CONFLICT`, thử lại |

## 5. Chi tiết — `GET /risk-events/{id}`

200 với `data` là `RiskEventDto`. 404 nếu không có.

## 6. Xác nhận — `POST /risk-events/{id}/confirm`

Người có quyền QA xác nhận một risk event đang `OPEN` là đúng. Cùng một lần lưu, hệ thống chuyển `status` sang `CONFIRMED` và ghi **một dòng `performance_contribution` âm**. Không có body.

| Điều kiện | Kết quả |
|---|---|
| `status` khác `OPEN` | 409 `RISK_EVENT_INVALID_STATE` |
| Không có kỳ đánh giá chứa ngày xảy ra (`occurredAt`, tính theo giờ UTC+7), hoặc kỳ đó `LOCKED` / `CLOSED` | 409 `RISK_EVENT_PERIOD_UNAVAILABLE` |
| Hai yêu cầu xác nhận đồng thời | một thành công, một 409 `RISK_EVENT_INVALID_STATE` (ràng buộc unique `uq_performance_contribution_risk_event`) |

Nhiều kỳ chứa cùng một ngày: chọn kỳ có `startDate` muộn nhất, rồi `endDate`, rồi `id` (cùng quy tắc với giữ điểm khiếu nại).

### Dòng contribution được ghi

| Cột | Giá trị |
|---|---|
| `risk_event_id` | id risk event (mỗi risk event tối đa một dòng) |
| `employee_id` | nhân viên bị ghi nhận |
| `evaluation_period_id` | kỳ chọn ở trên |
| `performance_evaluation_id` | đánh giá của nhân viên trong kỳ đó, `null` nếu chưa có |
| `component_score` | `-(mức phạt theo severity)`: `CRITICAL` -20, `HIGH` -10, `MEDIUM` -5, `LOW` -2 (cùng bảng với `EvaluationService`) |
| các cột KPI (target, actual, achievement, weight, progress) | `null` |

Dòng này chỉ ghi nhận, không sửa `performance_evaluation.total_score`; điểm phạt rủi ro vẫn được trừ khi tính form đánh giá như hiện nay và không thuộc phép tính `POST /performance-evaluations/{id}/calculate` (xem `PERFORMANCE_EVALUATION_CONTRACT.md`). Dòng contribution gắn risk event không hiện trong danh sách KPI của dashboard.

Response 200 (`RiskEventConfirmationDto`):

```json
{
  "success": true,
  "status": 200,
  "data": {
    "id": "6b2b6a4e-5f1d-4f0e-9a39-0d0c3c5e7a11",
    "code": "RISK-20261028-3F9A1C07",
    "employeeId": "33a8b399-33ba-4116-a26d-b913c2703309",
    "severity": "HIGH",
    "status": "CONFIRMED",
    "contributionId": "3d1c52b8-2a7b-43f4-8a8f-0e7c2c6d9b02",
    "evaluationPeriodId": "9a0f4c1e-8d3b-4c55-a1b2-7e6f5d4c3b21",
    "performanceEvaluationId": "c1d2e3f4-0a1b-4c2d-8e3f-4a5b6c7d8e9f",
    "componentScore": -10,
    "updatedAt": "2026-10-28T10:00:00+00:00"
  },
  "traceId": "request-trace-id"
}
```

`performanceEvaluationId` là `null` khi nhân viên chưa có đánh giá trong kỳ.

## 7. Bác bỏ — `POST /risk-events/{id}/dismiss`

Sự kiện không hợp lệ: `OPEN` → `DISMISSED`. Không có body, không ghi contribution. Từ đây điểm phạt của sự kiện không còn được tính (các phép tính bỏ qua `DISMISSED`). 200 với `RiskEventDto`; `status` khác `OPEN` (kể cả `CONFIRMED`): 409 `RISK_EVENT_INVALID_STATE`.

## 8. Giải quyết — `POST /risk-events/{id}/resolve`

Sự kiện đã xử lý xong: `CONFIRMED` → `RESOLVED`, ghi `resolvedAt` và `updatedAt` bằng thời điểm hiện tại. Không có body. Dòng contribution âm được giữ nguyên và sự kiện vẫn bị tính điểm phạt như trước. 200 với `RiskEventDto` (có `contributionId`); `status` khác `CONFIRMED`: 409 `RISK_EVENT_INVALID_STATE`.

## 9. Màn Sự kiện rủi ro (SCR-RSK-01 / D1 / M1)

Bổ sung cho ba màn của QA: danh sách, drawer chi tiết và modal ghi sự kiện. Quyền mới (migration `AddRiskEventScreens`): `risk_event:read` (QA, Admin ở `All`; Trưởng phòng `Dept`, Giám đốc chi nhánh `Branch`), `risk_event:create` và `risk_event:reverse` (chỉ QA, Admin, `All`).

### 9.1 Ghi sự kiện — `POST /risk-events` (mở rộng)

Thêm các trường tùy chọn vào body (§4):

| Trường | Kiểu | Quy tắc |
|---|---|---|
| `penaltyPoints` | number | điểm phạt (dương, ≤ 100, tối đa 2 chữ số thập phân); bỏ trống thì dùng mức theo `severity` (§6) |
| `workCode` | string | mã công việc liên kết; không có công việc này thì 400 `VALIDATION_ERROR` |
| `record` | boolean | `true` = ghi nhận ngay: sự kiện tạo ở `CONFIRMED` và dòng contribution âm được ghi trong cùng một lần lưu (cùng điều kiện kỳ của §6: 409 `RISK_EVENT_PERIOD_UNAVAILABLE`). Mặc định `false` (giữ luồng `OPEN` → `confirm`) |

Điểm phạt hiệu lực của một sự kiện là `penaltyPoints` nếu có, ngược lại theo `severity`. Mọi phép tính điểm (đánh giá, dashboard, `component_score`) dùng điểm hiệu lực này. Sự kiện đã ghi **không sửa được**.

### 9.2 Danh sách — `GET /risk-events`

Quyền `risk_event:read`; người có `Dept` / `Branch` chỉ thấy nhân viên trong phạm vi phòng của mình. Query: `periodId` (sự kiện có ngày vi phạm trong kỳ), `departmentId` (gồm phòng con), `severity`, `state` (`RECORDED` | `REVERSED`), `search` (mã `RSK-0045` hoặc `45`, mã / tên nhân viên, mã công việc), `page`, `pageSize` (tối đa 100). Mới nhất (ngày vi phạm) trước.

```json
{ "items": [RiskEventListItem], "totalCount": 4, "page": 1, "pageSize": 20,
  "counts": { "recorded": 3, "reversed": 1 },
  "departments": [{ "id": "…", "name": "Phòng Khách hàng cá nhân" }] }
```

`counts` tính theo các bộ lọc khác nhưng không theo `state`; `departments` là mọi phòng có sự kiện (để bộ lọc phòng không cần quyền đọc phòng ban).

`RiskEventListItem`: `id`, `code` (hiển thị `RSK-0045`, số từ sequence `risk_event_code_seq`), `occurredAt`, `employeeId`, `employeeCode`, `employeeName`, `departmentId`, `departmentName`, `eventType`, `severity`, `penaltyPoints` (số dương, điểm hiệu lực; UI hiển thị `−N`), `workId`, `workCode` (nullable), `status` (`OPEN | CONFIRMED | DISMISSED | RESOLVED`), `state` (`REVERSED` khi `status` là `DISMISSED`, kể cả sự kiện bị bác bỏ từ `OPEN`; ngược lại `RECORDED`), `createdAt`.

### 9.3 Chi tiết — `GET /risk-events/{id}/details`

`{ item: RiskEventListItem, jobPositionTitle, description, periodCode, periodName, recordedByName, recordedAt, reversedAt, reversedByName, reversalReason, attachments: [{ id, fileName, fileSize, mimeType }], canReverse }`. `canReverse` = `true` khi `status` là `CONFIRMED` hoặc `RESOLVED`, chưa đảo và người gọi có `risk_event:reverse`. 404 nếu không có hoặc ngoài phạm vi.

### 9.4 Đính kèm — `POST /risk-events/{id}/evidence`

`multipart/form-data`, trường `files[]` (`.pdf .docx .xlsx .png .jpg`, ≤ 25 MB mỗi tệp). Quyền `risk_event:create`. 200 với danh sách đính kèm của sự kiện. Mở tệp qua `GET /evidence/{id}/preview` (`risk_event:read` đủ để xem).

### 9.5 Gỡ điểm phạt — `POST /risk-events/{id}/reverse`

Body `{ "reason": "…" }` (10–2000 ký tự). Quyền `risk_event:reverse` ("Gỡ điểm phạt sự kiện rủi ro"). Sự kiện `CONFIRMED` / `RESOLVED` chuyển sang `DISMISSED` (màn hình gọi là **gỡ điểm phạt**, trạng thái "Đã gỡ"; tên kỹ thuật `reverse` / `REVERSED` giữ nguyên) và lưu `reversedAt`, `reversedBy`, `reversalReason`; từ đây điểm phạt không còn được tính (các phép tính bỏ qua `DISMISSED`). Dòng contribution âm ban đầu và mọi thay đổi được giữ nguyên trong audit log, không xóa. 200 với dữ liệu §9.3.

| Điều kiện | Kết quả |
|---|---|
| Đã gỡ điểm phạt (`DISMISSED`), hoặc `status` là `OPEN` | 409 `RISK_EVENT_INVALID_STATE` |
| Kỳ chứa ngày vi phạm đã khóa | 409 `RISK_EVENT_PERIOD_UNAVAILABLE` |
| `reason` ngắn hơn 10 ký tự | 400 `VALIDATION_ERROR` |
| Sự kiện của chính người gọi | 403 `FORBIDDEN` |
