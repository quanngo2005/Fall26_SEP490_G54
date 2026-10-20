# Evaluation Ranking Calibration & Approval API Contract (GB-331)

This contract defines the API endpoints, permissions, request/response models, validation rules, and error envelopes for:
- **SCR-EVL-02: Department Ranking Calibration Workspace (UC104, UC105)**
- **SCR-EVL-03: Evaluation & Ranking Approval Workspace (UC106, UC107)**

Following [API_CONVENTIONS.md](API_CONVENTIONS.md):
- Resource root: `/evaluations`
- Envelope: `ApiResponse<T>` with standard error codes and trace identifiers
- Authentication: Bearer token (`[Authorize]`)

---

## 1. Endpoints Overview

| Method | Endpoint | Description | Role / Permission |
| --- | --- | --- | --- |
| `GET` | `/evaluations/calibration` | Get department calibration overview with ranked roster, Bell curve, and quota metrics | `MANAGER`, `ADMIN` |
| `POST` | `/evaluations/calibration/submit` | Save draft or submit calibrated rankings to Executive with Tier A quota check | `MANAGER`, `ADMIN` |
| `GET` | `/evaluations/approvals` | Get branch-wide evaluation approval overview and department comparative ratios | `ADMIN`, `MANAGER` |
| `POST` | `/evaluations/approvals/{departmentId}/recalibrate` | Reject department calibration proposal and return for re-calibration | `ADMIN` |
| `POST` | `/evaluations/approvals/finalize` | Executive approval and hard-lock of evaluation period | `ADMIN` |

---

## 2. Detailed Endpoint Specifications

### 2.1 `GET /evaluations/calibration`

Get department calibration overview for a specific evaluation period and optional job position.

#### Query Parameters
- `periodId` (`Guid`, required): Target evaluation period id.
- `departmentId` (`Guid`, optional): Target department id. Defaults to current user's department if null.
- `positionId` (`Guid`, optional): Optional job position filter.

#### Business Rules
- **BR-20**: Severe disciplinary violation downgrades maximum achievable tier.
- **BR-34**: Sales Priority for High Performers. Ranking sort:
  1. `TotalScore` DESC
  2. `BusinessScore` DESC (tie-breaker)
  3. `ServiceQualityScore` DESC
  4. `EmployeeCode` ASC
- **BR-37**: Department Tier A quota cap (max 20%, minimum 1 for small teams).
- Bell Curve Gaussian distribution $\mu$ (mean) and $\sigma$ (standard deviation) calculated dynamically from scores.

#### 200 OK Response Example
```json
{
  "success": true,
  "data": {
    "department": {
      "id": "a1b2c3d4-0000-0000-0000-000000000001",
      "code": "D-SALES",
      "name": "Phòng Khách hàng Cá nhân",
      "managerName": "Lê Thị Thảo"
    },
    "period": {
      "id": "b2c3d4e5-0000-0000-0000-000000000002",
      "code": "Q3-2026",
      "name": "Kỳ đánh giá Quý 3/2026",
      "startDate": "2026-07-01",
      "endDate": "2026-09-30",
      "periodType": "QUARTER",
      "status": "OPEN"
    },
    "quota": {
      "totalEmployees": 20,
      "maxTierAQuota": 4,
      "proposedTierACount": 4,
      "calibratedTierACount": 4,
      "calibratedTierAPercent": 20.0,
      "calibratedTierBCount": 13,
      "calibratedTierBPercent": 65.0,
      "calibratedTierCCount": 2,
      "calibratedTierCPercent": 10.0,
      "calibratedTierDCount": 1,
      "calibratedTierDPercent": 5.0,
      "isCompliant": true,
      "complianceNote": "Đạt trần định mức phân bổ Tier A (≤ 20%)"
    },
    "bellCurve": {
      "meanScore": 84.5,
      "standardDeviation": 8.2,
      "points": [
        { "x": 0, "y": 0.01 },
        { "x": 50, "y": 2.15 },
        { "x": 85, "y": 4.82 },
        { "x": 100, "y": 1.20 }
      ],
      "targetTierAPercent": 20.0,
      "targetTierBPercent": 60.0,
      "targetTierCPercent": 15.0,
      "targetTierDPercent": 5.0
    },
    "rankedRoster": [
      {
        "employeeId": "e1f2a3b4-0000-0000-0000-000000000001",
        "employeeCode": "NV-0214",
        "fullName": "Nguyễn Thu Thảo",
        "jobPositionId": "j1k2l3m4-0000-0000-0000-000000000001",
        "jobPositionTitle": "Giao dịch viên (UB)",
        "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
        "departmentName": "Phòng Khách hàng Cá nhân",
        "businessScore": 96.0,
        "serviceQualityScore": 94.0,
        "operationsScore": 95.0,
        "qualitativeScore": 92.0,
        "totalScore": 95.0,
        "rank": 1,
        "proposedTier": "A1",
        "calibratedTier": "A1",
        "isAdjusted": false,
        "adjustmentReason": null,
        "coachingFlag": false,
        "disciplinaryFlag": null,
        "evaluationStatus": "CALCULATED",
        "evaluationId": "pe1-0000-0000-0000-000000000001",
        "calculatedAt": "2026-10-10T08:30:00Z"
      }
    ],
    "canSubmit": true,
    "overallStatus": "CALCULATED"
  },
  "error": null,
  "traceId": "0HMS12345678"
}
```

---

### 2.2 `POST /evaluations/calibration/submit`

Save draft or submit department calibration proposal to Executive.

#### Request Body (`SubmitCalibrationRequestDto`)
```json
{
  "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
  "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
  "adjustments": [
    {
      "employeeId": "e1f2a3b4-0000-0000-0000-000000000001",
      "calibratedTier": "A1",
      "reason": null,
      "coachingFlag": false
    },
    {
      "employeeId": "e1f2a3b4-0000-0000-0000-000000000002",
      "calibratedTier": "B",
      "reason": "Điều chỉnh do vi phạm tác phong giao dịch",
      "coachingFlag": true
    }
  ],
  "notes": "Đã hoàn thành rà soát hiệu chuẩn Quý 3/2026",
  "isDraft": false
}
```

#### Validation & Business Rules
- If `isDraft == false`:
  - Tier A count (A1, A2, A3) cannot exceed `maxTierAQuota`.
  - Throws `BusinessRuleException` with 400 Bad Request if quota exceeded.
  - Reason is required when `calibratedTier != proposedTier`.
  - Evaluations status transitioned to `IN_REVIEW`.
- If `isDraft == true`:
  - Saves adjustments without transitioning status to `IN_REVIEW`.

#### 200 OK Response
```json
{
  "success": true,
  "data": {
    "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
    "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
    "status": "IN_REVIEW",
    "totalAdjusted": 2,
    "tierACount": 4,
    "isQuotaCompliant": true,
    "submittedAt": "2026-10-11T09:30:00Z",
    "message": "Đã lưu và trình duyệt bảng hiệu chuẩn thứ hạng phòng ban lên Ban Giám đốc thành công."
  },
  "error": null,
  "traceId": "0HMS12345678"
}
```

---

### 2.3 `GET /evaluations/approvals`

Executive overview of evaluation submissions across departments with Tier A comparative analytics.

#### Query Parameters
- `periodId` (`Guid`, required): Evaluation period id.

#### 200 OK Response
```json
{
  "success": true,
  "data": {
    "period": {
      "id": "b2c3d4e5-0000-0000-0000-000000000002",
      "code": "Q3-2026",
      "name": "Kỳ đánh giá Quý 3/2026",
      "startDate": "2026-07-01",
      "endDate": "2026-09-30",
      "periodType": "QUARTER",
      "status": "OPEN"
    },
    "totalEmployees": 92,
    "totalEvaluated": 92,
    "totalTierACount": 18,
    "branchTierAPercent": 19.5,
    "branchCapPercent": 20.0,
    "isBranchCapCompliant": true,
    "canFinalize": true,
    "departments": [
      {
        "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
        "departmentCode": "D-CS",
        "departmentName": "P. Dịch vụ Khách hàng",
        "managerName": "Lê Thị Thảo",
        "totalEmployees": 20,
        "evaluatedEmployees": 20,
        "status": "IN_REVIEW",
        "averageScore": 86.2,
        "tierACount": 4,
        "tierAPercent": 20.0,
        "tierBCount": 13,
        "tierCCount": 2,
        "tierDCount": 1,
        "isQuotaCompliant": true,
        "reviewerComment": null,
        "submittedAt": "2026-10-11T09:30:00Z"
      }
    ]
  },
  "error": null,
  "traceId": "0HMS12345678"
}
```

---

### 2.4 `POST /evaluations/approvals/{departmentId}/recalibrate`

Reject department proposal and request re-calibration (UC106 Alt).

#### Route Parameter
- `departmentId` (`Guid`, required): Department to reject.

#### Request Body (`RejectCalibrationRequestDto`)
```json
{
  "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
  "reason": "Tỷ lệ Tier A vượt ngân sách và chưa giải trình thỏa đáng trường hợp đồng điểm."
}
```

#### Behavior
- Reverts department evaluations from `IN_REVIEW` back to `CALCULATED`.
- Records rejection feedback in evaluations.

#### 200 OK Response
```json
{
  "success": true,
  "data": {
    "departmentId": "a1b2c3d4-0000-0000-0000-000000000001",
    "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
    "status": "REVISION_REQUIRED",
    "message": "Đã trả hồ sơ hiệu chuẩn về Trưởng phòng để yêu cầu hiệu chuẩn lại kèm ý kiến chỉ đạo."
  },
  "error": null,
  "traceId": "0HMS12345678"
}
```

---

### 2.5 `POST /evaluations/approvals/finalize`

Executive digital sign-off and hard-lock of evaluation period (UC106, UC107, BR-29).

#### Request Body (`FinalizeEvaluationPeriodRequestDto`)
```json
{
  "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
  "signatureToken": "DIGITAL_PIN_OR_TOKEN",
  "approvalComments": "Phê duyệt chính thức kết quả đánh giá và xếp hạng toàn chi nhánh."
}
```

#### Behavior
- Sets `evaluation_period.status = LOCKED`.
- Sets all evaluations in period: `status = FINAL`, `approved_at = NOW()`.
- Immutable lock prevents subsequent modifications.

#### 200 OK Response
```json
{
  "success": true,
  "data": {
    "periodId": "b2c3d4e5-0000-0000-0000-000000000002",
    "periodStatus": "LOCKED",
    "finalizedAt": "2026-10-11T10:00:00Z",
    "totalEvaluationsFinalized": 92,
    "message": "Kỳ đánh giá đã được phê chuẩn chính thức và khóa dữ liệu (Hard-Lock) thành công."
  },
  "error": null,
  "traceId": "0HMS12345678"
}
```
