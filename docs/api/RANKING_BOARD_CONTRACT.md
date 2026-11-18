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

### GET /evaluations/ranking-board?periodId={guid}&comparePeriodId={guid}&departmentId={guid}

Trả về bảng hạng theo phòng cho kỳ đã chọn: slot từng hạng (`headcount`, `actualCount`), danh sách nhân viên xếp theo điểm giảm dần (SLA, đạt chỉ tiêu, sự kiện rủi ro, phạt, điểm định lượng, hạng hệ thống, hạng chốt, lý do điều chỉnh) và chênh lệch so với kỳ so sánh. Đính kèm `rankingLockDay`, `isLocked` (đã tới ngày chốt), `canSendToDirector` và `sendBlockers` (lý do bị chặn).

### POST /evaluations/ranking-board/draft

Lưu nháp hạng chốt và lý do điều chỉnh. Request: `{ periodId, departmentId, adjustments: [{ employeeId, finalTier, reason }] }`. Không kiểm tra slot và không yêu cầu đủ xác nhận.

### POST /evaluations/ranking-board/send-to-director

Gửi giám đốc chi nhánh. Chỉ được gọi khi đã tới ngày chốt. Bị chặn (409 `CONFLICT`) khi: A1/A2/A3/B chưa đủ đúng slot, có nhân viên chưa xác nhận, hoặc hạng chốt khác hạng hệ thống nhưng thiếu lý do.

### GET /evaluations/ranking-board/export?periodId={guid}&departmentId={guid}

File Excel `.xlsx` của bảng xếp hạng.

### GET /evaluations/ranking-compare?periodId={guid}&employeeIds={guid}&employeeIds={guid}

So sánh hai nhân viên theo điểm định lượng, chỉ tiêu, SLA, rủi ro và nhóm điểm (kinh doanh, chất lượng dịch vụ, tuân thủ). Chỉ dùng để tham khảo.

Lỗi chung: 400 `VALIDATION_ERROR`, 401, 403, 404 `NOT_FOUND`, 409 `CONFLICT`.
