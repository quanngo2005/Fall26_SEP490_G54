# Business Campaigns & Batch Work Generator API Contract (SCR-CMP-01 & SCR-CMP-02)

**Ticket:** GB-332 / GN-332  
**Feature:** Business Campaigns Management & Batch Work Generator Workspace  
**Status:** IMPLEMENTED  
**Author:** G54 Engineering  
**Date:** 2026-10-20  

---

## 1. Architectural Overview & Compliance

This contract specifies the canonical backend API for **Business Campaigns (Chiến dịch Kinh doanh)** and the **Batch Work Generator (Công cụ Tự động Sinh Đầu việc Hàng loạt)**, addressing:
- `SCR-CMP-01`: Business Campaigns List & Batch Work Generator (UC20, UC21, UC22, UC24 / UC65, UC66, UC67, UC68, UC69, UC71)
- `SCR-CMP-02`: Campaign Plan Review & Approval (UC23 / UC70)

### Enforced Business Rules (BRs):
- **BR-02 (Maker-Checker Segregation):** Branch Manager reviews and approves campaign plans independently from Department Manager creators.
- **BR-08 (KPI Contribution Idempotency):** Every generated task receives an immutable unique UUID and `TSK-` code, ensuring distinct single-event traceability.
- **BR-11 (Server Time Authority):** Task deadlines and campaign periods are anchored to the authoritative server time. Date-only checks (campaign approval against `endDate`, the date in generated `TSK-` codes) use the Vietnam calendar day (UTC+7).
- **BR-12 / BR-12a (Lead Pools & Grace Period):** Campaigns serve as lead distribution pools. Work results submitted within deadlines or grace periods trace back to campaign leads.
- **BR-24 (100% Campaign Allocation Balance):** Campaign target allocations across participants must sum to 100%.
- **BR-25 (7-Day Campaign Lead Exclusivity):** Generated batch tasks record 7-day exclusive access for the assigned employee before being eligible for handover or revocation.
- **BR-26 (BAU vs. Campaign Progress Separation):** Campaign analytics isolate campaign work throughput from routine Business-As-Usual (BAU) operations.
- **BR-41 (Block Work for Inactive/Pending Resignation Staff):** Batch work generation strictly filters out employees whose status is `INACTIVE` or flagged for resignation.

### Conventions:
- **Root Resource:** `/campaigns` (kebab-case plural, no `/api/v1` prefix).
- **Envelope:** `ApiResponse<T>` with standard status codes.
- **Identifiers:** Primary keys are UUIDs (`Guid`); display codes are strings (e.g. `CMP-2026-AUTUMN`, `TSK-AUTUMN-20261020-0001`).
- **Date formats:** ISO-8601 UTC timestamps (`TIMESTAMPTZ`) and `YYYY-MM-DD` for `DateOnly`.

---

## 2. API Endpoints Summary

| Method | Endpoint | Description | Auth / Role |
| :--- | :--- | :--- | :--- |
| `GET` | `/campaigns` | Lists business campaigns with status/objective filters, pagination, and KPI metric summary cards. | `[Authorize]` Dept Mgr, Branch Mgr |
| `GET` | `/campaigns/{id}` | Gets campaign detail including linked objective, configured work types, participants, and progress metrics. | `[Authorize]` Dept Mgr, Branch Mgr |
| `POST` | `/campaigns` | Creates a new campaign plan with participants and work types (Status: `PLANNED`). | `[Authorize]` Dept Mgr |
| `PUT` | `/campaigns/{id}` | Updates an existing campaign plan before or during execution. | `[Authorize]` Dept Mgr |
| `DELETE` | `/campaigns/{id}` | Deletes a planned campaign that has not yet generated any tasks. | `[Authorize]` Dept Mgr |
| `POST` | `/campaigns/{id}/decision` | Reviews and approves or rejects a campaign plan (SCR-CMP-02 / UC23). | `[Authorize]` Branch Mgr |
| `POST` | `/campaigns/{id}/batch-generate-work` | Generates batch work items allocated across participants via Round-Robin, Load Balancing, or Equal Split (SCR-CMP-01 / UC24). | `[Authorize]` Dept Mgr |

---

## 3. Endpoint Specifications

### 3.1 GET `/campaigns`
Gets paginated list of campaigns with summary metrics.

**Query Parameters:**
- `search` (string, optional): Search keyword matching campaign code or name.
- `status` (string, optional): Filter by `PLANNED`, `RUNNING`, `PAUSED`, `COMPLETED`, `CANCELLED`.
- `objectiveId` (Guid, optional): Filter by Business Objective ID.
- `page` (int, optional, default: `1`): 1-based page number.
- `pageSize` (int, optional, default: `20`, max: `100`): Page size.

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "metrics": {
      "totalCampaigns": 4,
      "runningCampaigns": 3,
      "plannedCampaigns": 1,
      "completedCampaigns": 0,
      "totalTasksGenerated": 1420,
      "totalCompletedTasks": 972
    },
    "campaigns": {
      "items": [
        {
          "id": "c1f2a3b4-0000-0000-0000-000000000001",
          "code": "CMP-2026-AUTUMN",
          "name": "Chiến dịch Tiết kiệm Tri ân Mùa Thu 2026",
          "description": "Huy động vốn tiền gửi dân cư lãi suất ưu đãi quý 4",
          "startDate": "2026-08-01",
          "endDate": "2026-10-31",
          "status": "RUNNING",
          "businessObjectiveId": "b1a2c3d4-0000-0000-0000-000000000001",
          "businessObjectiveCode": "OBJ-2026-HDV",
          "businessObjectiveName": "Tăng trưởng Huy động vốn Bán lẻ 2026",
          "participantsCount": 15,
          "workTypesCount": 2,
          "generatedWorksCount": 650,
          "completedWorksCount": 490,
          "updatedAt": "2026-10-20T10:00:00Z"
        }
      ],
      "totalCount": 1,
      "page": 1,
      "pageSize": 20
    }
  },
  "message": "Lấy danh sách chiến dịch thành công.",
  "traceId": "trace-1234"
}
```

---

### 3.2 GET `/campaigns/{id}`
Gets complete campaign details, linked work types, participant rosters, and progress analytics.

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "id": "c1f2a3b4-0000-0000-0000-000000000001",
    "code": "CMP-2026-AUTUMN",
    "name": "Chiến dịch Tiết kiệm Tri ân Mùa Thu 2026",
    "description": "Huy động vốn tiền gửi dân cư lãi suất ưu đãi quý 4",
    "startDate": "2026-08-01",
    "endDate": "2026-10-31",
    "status": "RUNNING",
    "businessObjective": {
      "id": "b1a2c3d4-0000-0000-0000-000000000001",
      "code": "OBJ-2026-HDV",
      "name": "Tăng trưởng Huy động vốn Bán lẻ 2026",
      "description": "Mục tiêu huy động vốn toàn chi nhánh"
    },
    "workTypes": [
      {
        "workTypeId": "w1a2b3c4-0000-0000-0000-000000000001",
        "code": "WT-CALL-DEP",
        "name": "Tư vấn mở sổ tiết kiệm đến hạn",
        "expectedResult": "Sổ tiết kiệm tái tục hoặc gửi mới",
        "requiredEvidence": "Mã hợp đồng tiền gửi Core Banking"
      }
    ],
    "participants": [
      {
        "employeeId": "e1f2a3b4-0000-0000-0000-000000000001",
        "employeeCode": "NV-0214",
        "fullName": "Nguyễn Thu Thảo",
        "jobPositionTitle": "Giao dịch viên (UB)",
        "departmentName": "Phòng Khách hàng Cá nhân",
        "assignedTasksCount": 50,
        "completedTasksCount": 42
      }
    ],
    "metrics": {
      "totalWorksCount": 650,
      "completedWorksCount": 490,
      "inExecutionWorksCount": 130,
      "pendingWorksCount": 30,
      "progressPercent": 75.4
    },
    "canGenerateBatchWork": true,
    "canApprove": false,
    "updatedAt": "2026-10-20T10:00:00Z"
  },
  "message": "Lấy thông tin chi tiết chiến dịch thành công.",
  "traceId": "trace-1234"
}
```

---

### 3.3 POST `/campaigns`
Creates a new campaign plan.

**Request Body (`CreateCampaignRequestDto`):**
```json
{
  "businessObjectiveId": "b1a2c3d4-0000-0000-0000-000000000001",
  "code": "CMP-2026-HOME",
  "name": "Gói Vay Mua nhà An Cư Lãi suất Cố định",
  "description": "Tiếp cận khách hàng vay mua nhà dịp cuối năm",
  "startDate": "2026-11-01",
  "endDate": "2026-12-31",
  "workTypeIds": [
    "w1a2b3c4-0000-0000-0000-000000000001"
  ],
  "participantEmployeeIds": [
    "e1f2a3b4-0000-0000-0000-000000000001",
    "e1f2a3b4-0000-0000-0000-000000000002"
  ]
}
```

**Success Response (201 Created):**
- Header: `Location: /campaigns/c1f2a3b4-0000-0000-0000-000000000002`
- Body: `ApiResponse<CampaignDetailDto>`

---

### 3.4 POST `/campaigns/{id}/decision` (Approve / Reject Plan - SCR-CMP-02)
Allows Branch Manager to approve or reject proposed campaign plan.

**Request Body (`CampaignDecisionRequestDto`):**
```json
{
  "decision": "APPROVE",
  "note": "Phê duyệt kế hoạch triển khai chiến dịch và dự toán ngân sách khen thưởng 180 triệu VNĐ.",
  "approvedBudget": 180000000.0
}
```

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "campaignId": "c1f2a3b4-0000-0000-0000-000000000002",
    "status": "RUNNING",
    "decidedAt": "2026-10-20T12:00:00Z",
    "message": "Đã phê duyệt kế hoạch chiến dịch kinh doanh thành công."
  },
  "message": "Phê duyệt kế hoạch chiến dịch thành công."
}
```

---

### 3.5 POST `/campaigns/{id}/batch-generate-work` (Batch Work Generator - SCR-CMP-01)
Generates and idempotently distributes lead tasks to active campaign participants.

**Request Body (`BatchWorkGeneratorRequestDto`):**
```json
{
  "workTypeId": "w1a2b3c4-0000-0000-0000-000000000001",
  "leadPoolSource": "Pool KH Tiết kiệm đến hạn trong tháng (150 KH)",
  "allocationRule": "ROUND_ROBIN",
  "totalLeadsCount": 150,
  "workTitleTemplate": "Tiếp cận khách hàng tái tục sổ tiết kiệm - {CampaignName}",
  "estimatedHours": 2.0,
  "plannedStartDate": "2026-10-21",
  "plannedEndDate": "2026-10-31",
  "priority": "HIGH",
  "targetEmployeeIds": [],
  "leads": [
    {
      "customerCode": "CUST-8801",
      "customerName": "Trần Văn An",
      "note": "Sổ tiết kiệm 500 triệu đáo hạn ngày 25/10"
    }
  ]
}
```

**Supported Allocation Rules:**
- `ROUND_ROBIN`: Sequential alternating distribution across participants.
- `LOAD_BALANCING`: Dynamically assigns each task to the participant with the fewest active tasks.
- `EQUAL_SPLIT`: Evenly divides lead quota across all participants.

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "campaignId": "c1f2a3b4-0000-0000-0000-000000000001",
    "campaignCode": "CMP-2026-AUTUMN",
    "totalGenerated": 150,
    "participantCount": 15,
    "tasksPerParticipantAverage": 10.0,
    "generatedTaskCodes": [
      "TSK-AUTUMN-20261020-0001",
      "TSK-AUTUMN-20261020-0002"
    ],
    "leadPoolSource": "Pool KH Tiết kiệm đến hạn trong tháng (150 KH)",
    "generatedAt": "2026-10-20T12:00:00Z",
    "message": "Đã tự động sinh 150 đầu việc và phân bổ thành công cho 15 cán bộ theo quy tắc ROUND_ROBIN."
  },
  "message": "Sinh đầu việc hàng loạt thành công."
}
```

---

## 4. Error Codes & Edge Cases

| Error Code | HTTP Status | Context / Trigger Condition |
| :--- | :--- | :--- |
| `VALIDATION_ERROR` | 400 | Missing required fields, invalid date range (`endDate < startDate`), non-positive lead count. |
| `NOT_FOUND` | 404 | Campaign, BusinessObjective, WorkType, or Employee not found. |
| `CONFLICT` | 409 | Duplicate campaign code (`uq_business_campaign_code`) or campaign status already completed/cancelled. |
| `TARGET_ALLOCATION_NOT_100_PERCENT` | 400 | Total allocated target percentage does not sum to 100% (BR-24). |
| `WORK_DEADLINE_EXCEEDS_CAMPAIGN` | 400 | Work deadline exceeds campaign end date (BR-12). |
| `CAMPAIGN_END_DATE_BEFORE_WORK_DEADLINE` | 400 | Campaign end date is earlier than the deadline of already generated works (BR-12). |
| `KPI_ALREADY_CONTRIBUTED` | 409 | Work item already exists or duplicates an existing task ID (BR-08). |
| `MAKER_CANNOT_BE_CHECKER` | 400 | Attempt to approve own campaign plan (BR-02 Maker != Checker). |
