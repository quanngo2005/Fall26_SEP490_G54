# QA Dashboard Contract (SCR-PRF-04)

Status: **IMPLEMENTED** (2026-11-10) — supports `SCR-PRF-04 · QA Dashboard (QA)` screen.

Served by `QualityAssuranceController` (same module as `/qa-queue`, `/quality-flags`), logic in `QaDashboardService`.

Provides operational quality assurance metrics, transaction matching rates, QA review queues,
and risk event penalty aggregations for QA / Inspector and Branch Management roles.

## Authorization & Permissions

All endpoints require authentication (`[Authorize]`) and QA/Admin read permission:
- `[HasPermission("quality_flag", "read", PermissionScopes.All)]`
Granted to `QA` (Kiểm soát chất lượng) and `ADMIN` / `SYS_ADMIN` roles.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/qa-dashboard` | Get main QA dashboard summary, banner, charts, and metrics |
| `GET` | `/qa-dashboard/reconciliations` | Paginated reconciliation list for the "Xem đối soát" drill-down |
| `GET` | `/qa-dashboard/filters` | Filter dropdown options (periods, departments, job positions) |

---

### 1. GET `/qa-dashboard`

#### Query Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `periodId` | `uuid` | No | Evaluation period ID. If omitted, defaults to the current active **monthly** evaluation period (day/week/quarter/year periods are never used). |
| `departmentId` | `uuid` | No | Department ID. If specified, includes all child departments in the hierarchy. |
| `jobPositionId` | `uuid` | No | Filter by specific job position ID. |
| `jobPositionCode` | `string` | No | Filter by job position code (e.g. `TELLER`, `SALES`). |

#### Response `200 OK`

```json
{
  "success": true,
  "status": 200,
  "data": {
    "asOf": "2026-10-26T08:15:02Z",
    "period": {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "code": "PER-2026-10",
      "name": "10/2026",
      "startDate": "2026-10-01",
      "endDate": "2026-10-31",
      "lockDeadline": "2026-10-31T23:59:59+07:00",
      "isLocked": false
    },
    "banner": {
      "inReviewCount": 12,
      "message": "Còn 12 kết quả In Review chưa có giao dịch import",
      "warning": "Tháng 10/2026 khóa KPI lúc 23:59:59 31/10/2026 (BR-29). Sau thời điểm này không import lại được giao dịch của tháng.",
      "hasUnmatchedInReview": true
    },
    "kpiCards": {
      "notMatched": {
        "count": 7,
        "oldestDays": 4,
        "subtext": "cũ nhất 4 ngày"
      },
      "inReview": {
        "count": 12,
        "subtext": "chờ GD import (MSG13)"
      },
      "qaQueue": {
        "total": 9,
        "sampleCount": 5,
        "flagCount": 4,
        "subtext": "5 mẫu · 4 cờ"
      },
      "riskEvents": {
        "count": 3,
        "totalPenaltyPoints": 8.0,
        "subtext": "tổng phạt -8 điểm"
      }
    },
    "matchedRateChart": {
      "title": "Tỷ lệ Matched theo tuần",
      "subtitle": "8 tuần gần nhất",
      "currentRate": 95.6,
      "currentWeekLabel": "T43",
      "weeks": [
        {
          "weekLabel": "T35",
          "weekNumber": 35,
          "year": 2026,
          "startDate": "2026-08-24",
          "endDate": "2026-08-30",
          "matchedCount": 95,
          "totalCount": 100,
          "matchRate": 95.0
        },
        {
          "weekLabel": "T43",
          "weekNumber": 43,
          "year": 2026,
          "startDate": "2026-10-19",
          "endDate": "2026-10-25",
          "matchedCount": 195,
          "totalCount": 204,
          "matchRate": 95.6
        }
      ]
    },
    "topDiscrepancyEmployees": [
      {
        "employeeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "employeeCode": "NV0231",
        "fullName": "Nguyễn Minh Anh",
        "jobTitle": "Teller",
        "departmentName": "Phòng Dịch vụ Khách hàng",
        "discrepancyCount": 3,
        "flagCount": 1
      },
      {
        "employeeId": "7ca85f64-5717-4562-b3fc-2c963f66afa7",
        "employeeCode": "NV0310",
        "fullName": "Trần Quốc Bảo",
        "jobTitle": "Sales Officer",
        "departmentName": "Phòng Khách hàng Cá nhân",
        "discrepancyCount": 2,
        "flagCount": 2
      }
    ],
    "commonDiscrepancyReasons": [
      {
        "reasonCode": "EMPLOYEE_MISMATCH",
        "reason": "Sai mã nhân viên",
        "count": 4
      },
      {
        "reasonCode": "PRODUCT_MISMATCH",
        "reason": "Sai sản phẩm",
        "count": 2
      },
      {
        "reasonCode": "REFER_ID_MISSING",
        "reason": "Thiếu Refer ID",
        "count": 2
      },
      {
        "reasonCode": "AMOUNT_MISMATCH",
        "reason": "Lệch số tiền",
        "count": 1
      },
      {
        "reasonCode": "NO_IMPORTED_TRANSACTION",
        "reason": "Chưa có GD import",
        "count": 12
      }
    ]
  }
}
```

---

Notes: `weeks[].matchRate` / `currentRate` are `null` when a week has no MATCHED/NOT_MATCHED transactions; `currentRate` is the latest week with data. The 8-week window is anchored on the selected period (not today) and is independent of the period range. `topDiscrepancyEmployees[].flagCount` counts OPEN quality flags. `GET /qa-dashboard/reconciliations` returns `400` for an unknown `status`.

---

### 2. GET `/qa-dashboard/reconciliations`

Paginated list of work transactions for the Reconciliation workspace ("Đối soát" / "Xem đối soát").

#### Query Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `periodId` | `uuid` | No | Filter by evaluation period. |
| `departmentId` | `uuid` | No | Filter by department. |
| `jobPositionId` | `uuid` | No | Filter by job position. |
| `status` | `string` | No | Filter by status (`MATCHED`, `NOT_MATCHED`, `IN_REVIEW`). |
| `reason` | `string` | No | Filter by mismatch reason code. |
| `search` | `string` | No | Search text (transaction code, work code, employee code/name). |
| `page` | `int` | No | Page number (default: 1, min: 1). |
| `pageSize` | `int` | No | Page size (default: 20, max: 100). |

#### Response `200 OK`

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "transactionCode": "TX100234",
        "workId": "4ba85f64-5717-4562-b3fc-2c963f66afa7",
        "workCode": "WRK-2026-0012",
        "workTitle": "Mở thẻ tín dụng Platinum",
        "employeeId": "5ca85f64-5717-4562-b3fc-2c963f66afa8",
        "employeeCode": "NV0231",
        "employeeName": "Nguyễn Minh Anh",
        "jobTitle": "Teller",
        "departmentName": "Phòng Dịch vụ Khách hàng",
        "status": "NOT_MATCHED",
        "reason": "EMPLOYEE_MISMATCH",
        "reasonDescription": "Sai mã nhân viên",
        "productCode": "CC_PLATINUM",
        "amount": 50000000.0,
        "transactionDate": "2026-10-15",
        "createdAt": "2026-10-22T08:30:00Z",
        "updatedAt": "2026-10-22T08:35:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 7,
    "totalPages": 1
  }
}
```

---

### 3. GET `/qa-dashboard/filters`

Returns option lists for the dashboard header selectors.

#### Response `200 OK`

```json
{
  "success": true,
  "status": 200,
  "data": {
    "periods": [
      {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "code": "PER-2026-10",
        "name": "10/2026",
        "isCurrent": true
      }
    ],
    "departments": [
      {
        "id": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
        "code": "D01",
        "name": "Phòng Dịch vụ Khách hàng"
      }
    ],
    "jobPositions": [
      {
        "id": "5fa85f64-5717-4562-b3fc-2c963f66afa8",
        "code": "TELLER",
        "title": "Giao dịch viên (Teller)"
      }
    ]
  }
}
```

