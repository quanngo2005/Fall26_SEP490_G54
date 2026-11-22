# System Parameter & Ranking Policy Studio Contract (SCR-ADM-04 / GB-910)

Trạng thái: **Triển khai đầy đủ Backend + Frontend (GB-910)**.
Mọi response dùng envelope `ApiResponse<T>` chuẩn (xem `API_CONVENTIONS.md`).
Toàn bộ tham số nghiệp vụ và quy tắc gợi ý xếp hạng được lưu trong PostgreSQL (`system_parameters`, `evaluation_comparison_setting`, `tier_criterion`, `ranking_policy`), đồng bộ tức thời qua Redis (`bpms:system-parameters:snapshot`) trên mọi API instance/background worker và tự động ghi `audit_logs` (`SystemParameter`, `EvaluationComparisonSetting`, `TierCriterion`, `RankingPolicy`). Không còn cấu hình tham số nghiệp vụ trong `appsettings.json`.

---

## 1. Phân quyền

| Method & Endpoint | Quyền (`resource:action`, scope) | Mô tả |
|---|---|---|
| `GET /system-parameters` | `system_parameter:view`, `All` | Lấy toàn bộ danh mục tham số hệ thống kèm metadata kiểu dữ liệu và nhóm |
| `PUT /system-parameters` | `system_parameter:update`, `All` | Cập nhật nguyên tử một hoặc nhiều tham số hệ thống |
| `GET /system-parameters/evaluation-comparison` | `system_parameter:view`, `All` | Lấy cấu hình mốc so sánh kỳ đánh giá |
| `PUT /system-parameters/evaluation-comparison` | `system_parameter:update`, `All` | Cập nhật mốc so sánh kỳ đánh giá |
| `GET /system-parameters/tier-criteria` | `system_parameter:view`, `All` | Lấy ma trận điều kiện bắt buộc theo hạng |
| `PUT /system-parameters/tier-criteria` | `system_parameter:update`, `All` | Cập nhật ma trận điều kiện bắt buộc theo hạng |
| `GET /system-parameters/ranking-policy` | `system_parameter:view`, `All` | Lấy bộ quy tắc gợi ý xếp hạng (mặc định hoặc theo `periodId`) |
| `PUT /system-parameters/ranking-policy` | `system_parameter:update`, `All` | Lưu bộ quy tắc gợi ý xếp hạng (tăng `version`, đồng bộ thang điểm gốc) |
| `POST /system-parameters/ranking-policy/simulate` | `system_parameter:view`, `All` | Chạy thử (Dry-Run Sandbox) bộ quy tắc nháp trên dữ liệu kỳ đánh giá mà không lưu DB |

Seed mặc định: `ADMIN`, `SYSTEM_ADMIN`. Lỗi chung: `401` không token; `403` thiếu quyền.

---

## 2. Danh mục tham số hệ thống (`GET / PUT /system-parameters`)

### 2.1 Kiểu dữ liệu (`valueType`) và Nhóm (`group`)
- `valueType`: `"INTEGER"` | `"DECIMAL"` | `"PERCENT"` | `"TIME"` (`HH:mm:ss`) | `"BOOLEAN"` | `"STRING"`.
- `group`:
  - `"DEADLINES"` — Kỳ đánh giá, Chốt sổ & Thời hạn
  - `"APPEALS"` — Khiếu nại & Phúc khảo
  - `"SCORING_QA"` — Trọng số chấm điểm, Rủi ro & QA
  - `"QUOTA_RANKING"` — Hạn ngạch Xếp hạng & Phân phối chuẩn
  - `"IMPORT_HANDOVER"` — Import dữ liệu, Giao dịch & Bàn giao
  - `"DASHBOARD_SECURITY"` — Ngưỡng cảnh báo Dashboard & Bảo mật đăng nhập

### 2.2 Bảng danh mục tham số đầy đủ

| Key | Group | ValueType | Mặc định | Khoảng hợp lệ | Consumer trực tiếp |
|---|---|---|---|---|---|
| `kpiLockTime` | `DEADLINES` | `TIME` | `"23:59:59"` | `00:00:00`–`23:59:59` | `QaDashboardService`, khóa sổ cuối kỳ (BR-29) |
| `preCloseCheckDays` | `DEADLINES` | `INTEGER` | `3` | `1`–`30` | Cảnh báo trước ngày đóng kỳ |
| `preCloseDeadlineDays` | `DEADLINES` | `INTEGER` | `12` | `1`–`60` | `EvaluationService.GetPreCloseCheckAsync` |
| `preCloseDeadlineHour` | `DEADLINES` | `INTEGER` | `17` | `0`–`23` | `EvaluationService.GetPreCloseCheckAsync` |
| `manualApprovalHours` | `DEADLINES` | `INTEGER` | `48` | `1`–`168` | SLA duyệt tay việc ngoài WorkType |
| `workOverdueEnabled` | `DEADLINES` | `BOOLEAN` | `true` | `true`/`false` | `WorkOverdueBackgroundService` |
| `workOverdueSweepMinutes` | `DEADLINES` | `INTEGER` | `15` | `1`–`1440` | `WorkOverdueBackgroundService` |
| `rankingLockDay` | `DEADLINES` | `INTEGER` | `20` | `1`–`28` | `EvaluationService` (ngày chốt xếp hạng: đóng kỳ tháng, quý, năm) |
| `campaignExclusivityDays` | `DEADLINES` | `INTEGER` | `7` | `1`–`90` | `CampaignService` (BR-25) |
| `appealRejectedHours` | `APPEALS` | `INTEGER` | `48` | `1`–`720` | `AppealService`, `MySubmissionService` (BR-31) |
| `appealWindowDays` | `APPEALS` | `INTEGER` | `7` | `1`–`30` | Cửa sổ phúc khảo sau công bố kỳ đánh giá |
| `minReasonLength` | `APPEALS` | `INTEGER` | `10` | `5`–`200` | `AppealService`, `RiskEventScreenService` |
| `maxEvidenceFiles` | `APPEALS` | `INTEGER` | `10` | `1`–`20` | `AppealService` |
| `qaSampleRatePercent` | `SCORING_QA` | `PERCENT` | `5` | `0`–`100` | `RandomQaSamplingPolicy` (BR-32) |
| `riskPenaltyLow` | `SCORING_QA` | `DECIMAL` | `2` | `0`–`100` | `EvaluationService`, `PerformanceScoreCalculator`, `QaDashboardService` |
| `riskPenaltyMedium` | `SCORING_QA` | `DECIMAL` | `5` | `0`–`100` | `EvaluationService`, `PerformanceScoreCalculator`, `QaDashboardService` |
| `riskPenaltyHigh` | `SCORING_QA` | `DECIMAL` | `10` | `0`–`100` | `EvaluationService`, `PerformanceScoreCalculator`, `QaDashboardService` |
| `riskPenaltyCritical` | `SCORING_QA` | `DECIMAL` | `20` | `0`–`100` | `EvaluationService`, `PerformanceScoreCalculator`, `QaDashboardService` |
| `unitUnderperformanceThresholdPercent` | `SCORING_QA` | `PERCENT` | `70` | `0`–`100` | BR-41 ngưỡng điểm đơn vị |
| `unitPenaltyMultiplier` | `SCORING_QA` | `DECIMAL` | `0.8` | `0.1`–`1` | BR-41 hệ số điều chỉnh khi đơn vị dưới ngưỡng |
| `evalKpiMetThresholdPercent` | `SCORING_QA` | `PERCENT` | `100` | `0`–`200` | `EvaluationService` (ngưỡng KPI Đạt & tô màu tốt trên `/evaluations/form`) |
| `evalKpiNearThresholdPercent` | `SCORING_QA` | `PERCENT` | `85` | `0`–`200` | `EvaluationService` (ngưỡng KPI Cận đích & tô màu cảnh báo trên `/evaluations/form`) |
| `evalCommentMaxLength` | `SCORING_QA` | `INTEGER` | `1000` | `50`–`5000` | `EvaluationService` (giới hạn độ dài nhận xét trên `/evaluations/form`) |
| `tierAQuotaPercent` | `QUOTA_RANKING` | `PERCENT` | `20` | `1`–`100` | `EvaluationService` (Calibration, Submit, Branch Approval, Bell Curve) |
| `tierBTargetPercent` | `QUOTA_RANKING` | `PERCENT` | `60` | `0`–`100` | `EvaluationService` (Bell Curve) |
| `tierCTargetPercent` | `QUOTA_RANKING` | `PERCENT` | `15` | `0`–`100` | `EvaluationService` (Bell Curve) |
| `tierDTargetPercent` | `QUOTA_RANKING` | `PERCENT` | `5` | `0`–`100` | `EvaluationService` (Bell Curve) |
| `minDeptSizeForOwnQuota` | `QUOTA_RANKING` | `INTEGER` | `5` | `1`–`50` | `EvaluationService` (quy mô tối thiểu phòng ban tính hạn ngạch riêng) |
| `importMaxFileMb` | `IMPORT_HANDOVER` | `INTEGER` | `5` | `1`–`50` | `DataImportsController`, `TransactionImportsController` |
| `targetImportMaxFileMb` | `IMPORT_HANDOVER` | `INTEGER` | `10` | `1`–`50` | `TargetAssignmentController` |
| `importMaxRows` | `IMPORT_HANDOVER` | `INTEGER` | `5000` | `100`–`100000` | `EmployeeImportService`, `TargetImportService` |
| `maxTransactionsPerWork` | `IMPORT_HANDOVER` | `INTEGER` | `10` | `1`–`50` | `WorkTransactionService` |
| `handoverThresholdVnd` | `IMPORT_HANDOVER` | `INTEGER` | `500000000` | `>= 1` (`max = null`) | `HandoverService` (BR-01) |
| `importFormats` | `IMPORT_HANDOVER` | `STRING` | `".xlsx"` | `locked = true` | Cố định định dạng chuẩn Excel |
| `perfAttentionDropPoints` | `DASHBOARD_SECURITY` | `DECIMAL` | `1.5` | `0.1`–`50` | `DepartmentService` (SCR-PRF-02/03) |
| `perfAttentionLowScore` | `DASHBOARD_SECURITY` | `DECIMAL` | `80` | `0`–`100` | `DepartmentService` (SCR-PRF-02/03) |
| `perfAttentionTopN` | `DASHBOARD_SECURITY` | `INTEGER` | `5` | `1`–`50` | `DepartmentService` (SCR-PRF-02/03) |
| `perfWeakGroupScore` | `DASHBOARD_SECURITY` | `DECIMAL` | `85` | `0`–`100` | `DepartmentService` (SCR-PRF-02/03) |
| `perfTrendPeriodCount` | `DASHBOARD_SECURITY` | `INTEGER` | `6` | `2`–`24` | `DepartmentService` (SCR-PRF-02/03) |
| `authMaxFailedAttempts` | `DASHBOARD_SECURITY` | `INTEGER` | `5` | `3`–`20` | `AuthService` |
| `authLockoutMinutes` | `DASHBOARD_SECURITY` | `INTEGER` | `15` | `1`–`1440` | `AuthService` |
| `loginRateLimitPerMinute` | `DASHBOARD_SECURITY` | `INTEGER` | `5` | `3`–`120` | `Program.cs` Login Rate Limiter |

### 2.3 Ví dụ `GET /system-parameters` (200 OK)
```json
{
  "success": true,
  "status": 200,
  "data": [
    {
      "key": "kpiLockTime",
      "value": "23:59:59",
      "min": null,
      "max": null,
      "locked": false,
      "updatedAt": null,
      "updatedBy": null,
      "valueType": "TIME",
      "group": "DEADLINES"
    },
    {
      "key": "perfAttentionDropPoints",
      "value": 1.5,
      "min": 0.1,
      "max": 50,
      "locked": false,
      "updatedAt": "2026-11-15T04:00:00+00:00",
      "updatedBy": "admin@bpms.com",
      "valueType": "DECIMAL",
      "group": "DASHBOARD_SECURITY"
    }
  ]
}
```

### 2.4 `PUT /system-parameters`
```json
{
  "changes": [
    { "key": "kpiLockTime", "value": "22:30:00" },
    { "key": "perfAttentionDropPoints", "value": 2.5 },
    { "key": "workOverdueEnabled", "value": true },
    { "key": "tierAQuotaPercent", "value": 25 }
  ]
}
```
- `200 OK`: trả danh sách đầy đủ như `GET` sau khi lưu DB và đồng bộ Redis.
- `400 VALIDATION_ERROR`: danh sách trống, key không tồn tại/trùng/bị khóa, sai kiểu dữ liệu hoặc ngoài khoảng `[min, max]`.
- `409 CONFLICT`: xung đột cập nhật đồng thời.

---

## 3. Ranking Policy Studio (`GET / PUT / POST /system-parameters/ranking-policy`)

Cho phép Admin kiểm soát toàn diện thuật toán gợi ý xếp hạng theo từng quý hoặc mặc định toàn hệ thống thông qua cấu trúc **JSON AST (Visual Rule Pipeline kiểu Jira Automation / Scratch)**, tuyệt đối không dùng `eval` hay biên dịch mã động. Khi kỳ đánh giá chuyển sang `LOCKED`, bản chụp `ranking_policy_snapshot_json` được đóng băng vào `evaluation_period` để bảo toàn lịch sử.

### 3.1 `GET /system-parameters/ranking-policy?periodId={optionalGuid}`
- Nếu truyền `periodId`:
  - Nếu kỳ đã `LOCKED` và có `ranking_policy_snapshot_json`, trả bản snapshot kèm `isLockedSnapshot: true`.
  - Nếu kỳ có chính sách riêng trong `ranking_policy`, trả chính sách của kỳ đó; nếu chưa có, trả chính sách mặc định đang active (`hasPeriodOverride: false`).
- Nếu không truyền `periodId`: trả chính sách mặc định toàn hệ thống.

#### Response `RankingPolicyDto` (200 OK)
```json
{
  "success": true,
  "status": 200,
  "data": {
    "id": "8a1b2c3d-0000-0000-0000-000000000001",
    "code": "DEFAULT_POLICY",
    "name": "Chính sách gợi ý xếp hạng chuẩn Chi nhánh",
    "description": "Áp dụng công thức điểm hiệu lực, điều kiện điểm liệt và kiểm soát hạn ngạch Tier A.",
    "version": 3,
    "evaluationPeriodId": null,
    "hasPeriodOverride": false,
    "isLockedSnapshot": false,
    "updatedAt": "2026-11-15T05:00:00+00:00",
    "updatedBy": "admin@bpms.com",
    "definition": {
      "scoreFormula": "IF(DepartmentAvgScore < 70, ROUND(TotalScore * 0.8, 2), TotalScore)",
      "tiers": [
        { "tierCode": "A1", "rankOrder": 1, "minScore": 95 },
        { "tierCode": "A2", "rankOrder": 2, "minScore": 90 },
        { "tierCode": "A3", "rankOrder": 3, "minScore": 85 },
        { "tierCode": "B", "rankOrder": 4, "minScore": 70 },
        { "tierCode": "C", "rankOrder": 5, "minScore": 50 },
        { "tierCode": "D", "rankOrder": 6, "minScore": 0 }
      ],
      "tieBreakers": [
        "EFFECTIVE_SCORE_DESC",
        "BUSINESS_SCORE_DESC",
        "SERVICE_QUALITY_SCORE_DESC",
        "OPERATIONS_SCORE_DESC",
        "EMPLOYEE_CODE_ASC"
      ],
      "rules": [
        {
          "id": "rule-critical-risk",
          "name": "Hạ trần kỷ luật khi có sự kiện rủi ro Nghiêm trọng",
          "enabled": true,
          "stopOnMatch": false,
          "conditions": {
            "logicalOperator": "OR",
            "items": [
              { "metric": "MAX_RISK_SEVERITY", "operator": "GTE", "value": 4, "secondaryValue": null, "values": null }
            ],
            "groups": []
          },
          "actions": [
            { "type": "CAP_MAX_TIER", "tierCode": "C", "numericValue": null, "flagValue": null, "message": "Giới hạn tối đa hạng C do vi phạm rủi ro CRITICAL (BR-20)" },
            { "type": "SET_COACHING_FLAG", "tierCode": null, "numericValue": null, "flagValue": true, "message": null }
          ]
        },
        {
          "id": "rule-tier-a-quota",
          "name": "Cắt ngọn hạn ngạch Tier A theo phân phối chuẩn",
          "enabled": true,
          "stopOnMatch": false,
          "conditions": {
            "logicalOperator": "AND",
            "items": [
              { "metric": "CURRENT_TIER", "operator": "IN", "value": null, "secondaryValue": null, "values": ["A1", "A2", "A3"] }
            ],
            "groups": []
          },
          "actions": [
            { "type": "ENFORCE_GROUP_QUOTA_PERCENT", "tierCode": "B", "numericValue": 20, "flagValue": null, "message": "Chuyển xuống hạng B do vượt hạn ngạch Top 20% Tier A của đơn vị (BR-37)" }
          ]
        }
      ]
    },
    "catalog": {
      "formulaVariables": ["TotalScore", "WeightedScore", "QuantitativeScore", "QualitativeScore", "BusinessScore", "ServiceQualityScore", "OperationsScore", "RiskPenalty", "ScoreChangeVsPrev", "OpenRiskEvents", "CriticalRiskCount", "HighRiskCount", "DepartmentAvgScore"],
      "formulaFunctions": ["MIN", "MAX", "ROUND", "ABS", "CLAMP", "IF"],
      "metrics": ["TOTAL_SCORE", "EFFECTIVE_SCORE", "QUANTITATIVE_SCORE", "QUALITATIVE_SCORE", "BUSINESS_SCORE", "SERVICE_QUALITY_SCORE", "OPERATIONS_SCORE", "MIN_COMPONENT_SCORE", "SCORE_CHANGE_VS_PREV", "OPEN_RISK_EVENTS", "MAX_RISK_SEVERITY", "CRITICAL_RISK_COUNT", "HIGH_OR_ABOVE_RISK_COUNT", "PERCENTILE_IN_GROUP", "RANK_IN_GROUP", "GROUP_SIZE", "CURRENT_TIER", "JOB_POSITION_CODE"],
      "operators": ["GTE", "GT", "LTE", "LT", "EQ", "NEQ", "BETWEEN", "IN"],
      "actionTypes": ["CAP_MAX_TIER", "SET_MIN_TIER", "SET_EXACT_TIER", "DEMOTE_STEPS", "PROMOTE_STEPS", "ENFORCE_GROUP_QUOTA_PERCENT", "SET_COACHING_FLAG", "SET_WARNING_LABEL"],
      "tieBreakerFields": ["EFFECTIVE_SCORE_DESC", "TOTAL_SCORE_DESC", "BUSINESS_SCORE_DESC", "SERVICE_QUALITY_SCORE_DESC", "OPERATIONS_SCORE_DESC", "QUALITATIVE_SCORE_DESC", "RISK_PENALTY_ASC", "EMPLOYEE_CODE_ASC"]
    }
  }
}
```

### 3.2 `PUT /system-parameters/ranking-policy`
Lưu chính sách mặc định (`evaluationPeriodId = null`) hoặc lưu bản override riêng cho một kỳ đang mở (`evaluationPeriodId = <Guid>`).
- Trả `409 PERIOD_LOCKED` nếu cập nhật chính sách của một kỳ đã `LOCKED` / `CLOSED`.
- Trả `400 VALIDATION_ERROR` nếu:
  - Công thức `scoreFormula` sai cú pháp, dùng biến/hàm ngoài danh mục cho phép, hoặc quá 500 ký tự / độ sâu > 10.
  - Danh sách `tiers` rỗng, trùng mã, điểm không giảm dần theo `rankOrder`, hoặc ngoài `0..100`.
  - Rule thiếu tên, dùng metric/operator/action không hợp lệ, tham chiếu `tierCode` không có trong `tiers`, hoặc lồng nhóm điều kiện quá 3 cấp.

### 3.3 `POST /system-parameters/ranking-policy/simulate` (Sandbox Dry-Run)
Chạy thử `definition` trên dữ liệu kỳ `periodId` (tùy chọn lọc `departmentId`; nếu kỳ chưa có dữ liệu chấm điểm thì tự động sinh bộ mẫu mô phỏng chuẩn `isSyntheticSample: true` để Admin vẫn kiểm chứng được mọi nhánh logic).

#### Request `SimulateRankingPolicyRequestDto`
```json
{
  "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
  "departmentId": null,
  "definition": { "...": "như RankingPolicyDefinitionDto" }
}
```

#### Response `SimulateRankingPolicyResultDto` (200 OK)
Trả thống kê phân bổ hạng Trước vs Sau (`tierSummary`), số nhân viên thay đổi hạng (`changedCount`), cờ `isSyntheticSample`, và bảng chi tiết từng nhân viên (`rows`: điểm gốc, điểm công thức, thứ hạng cũ/mới, hạng cũ/mới, danh sách rule kích hoạt, và các bước giải thích `explanation`).
