# Ranking Board Contract (Xếp hạng Phòng)

Trạng thái: đang triển khai theo 7 bước trong `context.md`. Phần đã có: cấu hình slot theo phòng và ngày chốt.

## 1. Ngày chốt xếp hạng

Tham số hệ thống `rankingLockDay` (nhóm `DEADLINES`, `INTEGER`, mặc định `20`, khoảng `1`–`28`). Xem `SYSTEM_PARAMETER_CONTRACT.md`.

## 2. Slot xếp hạng theo phòng

Mỗi phòng có số người tối đa (headcount) cho từng hạng A1, A2, A3, B. Hạng C là phần còn lại của nhân viên đủ điều kiện. Không có hạng D. Slot cố định cho mọi kỳ.

Quyền: `system_parameter:view` để xem, `system_parameter:update` để sửa (scope `All`, chỉ Admin).

### GET /ranking-slot-quotas?departmentId={guid}

Response 200:

```json
{
  "data": {
    "departmentId": "5eed0000-0000-0000-0001-000000000001",
    "slots": [
      { "tierCode": "A1", "headcount": 2 },
      { "tierCode": "A2", "headcount": null },
      { "tierCode": "A3", "headcount": 4 },
      { "tierCode": "B", "headcount": 6 }
    ]
  }
}
```

`headcount: null` nghĩa là phòng chưa cấu hình hạng đó. Không được coi là 0.

Lỗi: 401 (chưa đăng nhập), 403 (không có quyền), 404 `NOT_FOUND` (phòng không tồn tại).

### PUT /ranking-slot-quotas

Request: phải gửi đủ đúng một slot cho mỗi hạng A1, A2, A3, B, và `headcount` là số nguyên không âm.

```json
{
  "departmentId": "5eed0000-0000-0000-0001-000000000001",
  "slots": [
    { "tierCode": "A1", "headcount": 2 },
    { "tierCode": "A2", "headcount": 3 },
    { "tierCode": "A3", "headcount": 4 },
    { "tierCode": "B", "headcount": 6 }
  ]
}
```

Response 200: cùng dạng với GET (đã lưu).

Lỗi: 400 `VALIDATION_ERROR` (thiếu, trùng hoặc số âm), 401, 403, 404 `NOT_FOUND`.

## 3. Màn hình xếp hạng

Quyền xem: `evaluation:read` (scope `Dept`). Quyền thao tác gửi giám đốc: `evaluation:finalize` (scope `Branch`).

### GET /evaluations/ranking-board?periodId={guid}&departmentId={guid?}&comparePeriodId={guid?}

Phạm vi: chỉ phòng của người đăng nhập. Bỏ trống `departmentId` thì dùng phòng của người gọi. Bỏ trống `comparePeriodId` thì dùng kỳ cùng loại ngay trước đó (`comparePeriodId` trong response là kỳ đã dùng, có thể `null`).

Response 200 (`data`):

```json
{
  "departmentId": "guid",
  "departmentName": "string",
  "periodId": "guid",
  "periodCode": "string",
  "periodName": "string",
  "comparePeriodId": "guid | null",
  "comparePeriodCode": "string | null",
  "rankingLockDay": 20,
  "rankingLockDate": "2026-11-20",
  "isLocked": true,
  "canSendToDirector": false,
  "isSentToDirector": false,
  "sendBlockers": ["string"],
  "tiers": [
    { "tierCode": "A1", "headcount": 2, "actualCount": 0, "isFilled": false }
  ],
  "staffRows": [
    {
      "rank": 1,
      "employeeId": "guid",
      "employeeCode": "string",
      "fullName": "string",
      "onTimeSla": { "value": 92.5, "previousValue": 90.0, "change": 2.5 },
      "achievedCriteria": 3,
      "totalCriteria": 4,
      "riskEventCount": { "value": 1, "previousValue": 2, "change": -1 },
      "riskPenalty": { "value": 1.5, "previousValue": 0, "change": 1.5 },
      "quantitativeScore": { "value": 88.2, "previousValue": 85.0, "change": 3.2 },
      "systemTier": "A3",
      "finalTier": "A3",
      "adjustmentReason": null
    }
  ],
  "generatedAt": "2026-11-19T00:00:00Z"
}
```

- `tiers` chỉ có A1, A2, A3, B. Hạng C không có dòng riêng; FE tính `C = tổng nhân viên − tổng actualCount của A1–B`.
- Delta `change = value − previousValue`, `null` khi thiếu một trong hai giá trị.
- `systemTier`, `finalTier`: `null` khi nhân viên chưa có điểm.
- `sendBlockers`: danh sách lý do bằng tiếng Việt, rỗng khi `canSendToDirector = true`. Gồm: còn nhân viên đang khiếu nại (`DISPUTED`); còn nhân viên chưa ở `CALCULATED`; thiếu lý do điều chỉnh hạng; slot hạng không khớp. Không còn cổng thời gian theo ngày khóa.
- `rankingLockDate`: ngày chốt cụ thể của kỳ (`yyyy-MM-dd`), tính từ `rankingLockDay` và loại kỳ (tháng, quý, năm).
- `isSentToDirector`: `true` khi đã gửi giám đốc chi nhánh, tức mọi đánh giá của nhân viên trong bảng đã ở `SUBMITTED` hoặc `FINAL` (có ít nhất một nhân viên).

Lỗi: 400 `VALIDATION_ERROR` (thiếu `periodId`; hiện đang trả 404, cần thống nhất), 401, 403, 404 `NOT_FOUND` (kỳ, kỳ so sánh hoặc phòng không tồn tại).

### POST /evaluations/ranking-board/draft

Lưu nháp hạng chốt và lý do điều chỉnh. Request: `{ departmentId, periodId, adjustments: [{ employeeId, finalTier, reason }] }`. Không kiểm tra slot và không yêu cầu đủ xác nhận. Response 200: cùng dạng với GET.

### POST /evaluations/ranking-board/auto-assign

Chỉ dùng khi đã tới ngày chốt (`isLocked = true`); chưa tới ngày chốt trả 409 `CONFLICT`. Request: `{ departmentId, periodId }`. BE xếp hạng chốt A1 → C theo thứ tự điểm định lượng giảm dần, mỗi hạng nhận tối đa `headcount` người; nhân viên vượt slot giữ hạng hệ thống. Kết quả được lưu ngay vào bản nháp và ghi đè toàn bộ hạng chốt cũ; lý do điều chỉnh để trống. Response 200: cùng dạng với GET. Quyền `evaluation:calibrate` (scope `Dept`).

### POST /evaluations/ranking-board/send-to-director

Request giống draft. BE lưu hạng chốt trước, sau đó kiểm tra điều kiện trên dữ liệu đã lưu. Chỉ được gửi khi đã tới ngày chốt. Bị chặn (409 `CONFLICT`) khi: A1/A2/A3/B chưa đủ đúng slot, có nhân viên chưa xác nhận, hoặc hạng chốt khác hạng hệ thống nhưng thiếu lý do. Quyền `evaluation:finalize` (scope `Branch`); DEPT_MANAGER nhận 403.

Response 200: `{ departmentId, periodId, status, sentAt, message }`.

### GET /evaluations/ranking-board/export?periodId={guid}&departmentId={guid?}

File Excel `.xlsx` của bảng xếp hạng (hai sheet: "Hạng" và "Nhân viên"). Dùng kỳ so sánh tự động.

### GET /evaluations/ranking-compare?periodId={guid}&employeeIds={guid}&employeeIds={guid}

So sánh hai nhân viên theo điểm định lượng, chỉ tiêu, SLA, rủi ro và nhóm điểm (kinh doanh, chất lượng dịch vụ, tuân thủ). Chỉ dùng để tham khảo.

Lỗi chung: 400 `VALIDATION_ERROR`, 401, 403, 404 `NOT_FOUND`, 409 `CONFLICT`.
