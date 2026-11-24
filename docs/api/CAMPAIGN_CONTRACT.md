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
| `GET` | `/business-objectives` | Lists the business objectives a campaign can be linked to (everything but `CANCELLED`), ordered by code. Feeds the objective dropdown of the plan form. Now served by `BusinessObjectivesController`, see `BUSINESS_OBJECTIVE_CONTRACT.md`. | `[Authorize]` `objective:read` (Dept) |
| `GET` | `/campaigns` | Lists business campaigns with status/objective filters, pagination, and KPI metric summary cards. | `[Authorize]` Dept Mgr, Branch Mgr |
| `GET` | `/campaigns/{id}` | Gets campaign detail including linked objective, configured work types, participants, and progress metrics. | `[Authorize]` Dept Mgr, Branch Mgr |
| `POST` | `/campaigns` | Creates a new campaign plan with participants and work types (Status: `PLANNED`). | `[Authorize]` Dept Mgr |
| `PUT` | `/campaigns/{id}` | Updates an existing campaign plan before or during execution. | `[Authorize]` Dept Mgr |
| `DELETE` | `/campaigns/{id}` | Deletes a planned campaign that has not yet generated any tasks. | `[Authorize]` Dept Mgr |
| `POST` | `/campaigns/{id}/decision` | Reviews and approves or rejects a campaign plan (SCR-CMP-02 / UC23). | `[Authorize]` Branch Mgr |
| `POST` | `/campaigns/{id}/batch-generate-work` | Generates batch work items allocated across participants via Round-Robin, Load Balancing, or Equal Split (SCR-CMP-01 / UC24). | `[Authorize]` Dept Mgr |

---

## 3. Endpoint Specifications

### 3.0 GET `/business-objectives`
`data`: array of business objectives (the shape, the permission and the visibility rules are in `BUSINESS_OBJECTIVE_CONTRACT.md`; it still contains `id`, `code`, `name`, `description`), without `CANCELLED` ones unless `status` is given, ordered by `code`.

---

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
      "totalCompletedTasks": 972,
      "slowCampaignCount": 1
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
          "progressPercent": 75.4,
          "timeElapsedPercent": 88.0,
          "paceGap": -12.6,
          "kpiCode": "HDV",
          "kpiName": "Huy động vốn",
          "targetUnit": "tỷ VND",
          "targetValue": 300,
          "actualValue": 66,
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

`progressPercent`, `timeElapsedPercent`, `paceGap`, `kpiCode`, `kpiName`, `targetUnit`, `targetValue`, `actualValue` and
`metrics.slowCampaignCount` serve the SCR-PRF-03 campaign card; their definitions are in
`BRANCH_DASHBOARD_CONTRACT.md` section 3 (nullable fields are `null` when the source data is missing, never `0`).

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
        "requiredEvidence": "Mã hợp đồng tiền gửi Core Banking",
        "targetUnit": "tài khoản"
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
      "progressPercent": 75.4,
      "customerTotal": 200,
      "customerUnassigned": 15,
      "customerClosed": 42
    },
    "canGenerateBatchWork": true,
    "canApprove": false,
    "updatedAt": "2026-10-20T10:00:00Z"
  },
  "message": "Lấy thông tin chi tiết chiến dịch thành công.",
  "traceId": "trace-1234"
}
```

`customerTotal`, `customerUnassigned` and `customerClosed` (§5) are the campaign's customer counts: all rows,
rows with no `assigneeEmployeeId`, and rows with `contactStatus = CLOSED`. Additive fields; `0` when the campaign
has no imported customers.

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
  "targetValue": 600,
  "leads": [
    {
      "customerCode": "CUST-8801",
      "customerName": "Trần Văn An",
      "note": "Sổ tiết kiệm 500 triệu đáo hạn ngày 25/10"
    }
  ]
}
```

**Expected result (`targetValue`, optional):** the total result the generated works should reach, in the unit of the work type's KPI (`targetUnit` of the work type in `GET /campaigns/{id}`: accounts, VND billion, ...). It must be `>= 0` and at most `9,999,999,999,999`; otherwise `400 VALIDATION_ERROR` and nothing is created. It is split evenly across the generated works (rounded down to 4 decimals, the remainder goes on the last work so the shares add up exactly) and stored in `work.target_value`; each work description also ends with `Mức mong muốn: <value> <unit>.`. When omitted, `work.target_value` stays `null`. `targetUnit` is the unit of the current required KPI of the work type (otherwise the earliest current one), or `null` when it has none.

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
| `VALIDATION_ERROR` | 400 | Also: work deadline exceeds campaign end date, or campaign end date is earlier than the deadline of already generated works (BR-12). |
| `KPI_ALREADY_CONTRIBUTED` | 409 | Work item already exists or duplicates an existing task ID (BR-08). |
| `MAKER_CANNOT_BE_CHECKER` | 400 | Attempt to approve own campaign plan (BR-02 Maker != Checker). |

---

## 5. Campaign customers

**Status:** IMPLEMENTED (backend). Phase 1 shipped the DAL entity, migration, permission and error codes
(`G54.DAL.Entities.CampaignCustomer`, migration `AddCampaignCustomer`). Phase 2 shipped the Excel import
(`CampaignCustomerImportService`, `CampaignCustomersController` import endpoints). Phase 3 shipped the manager-side
list/allocate/reassign (`CampaignCustomerService`). Phase 4 shipped the employee ("My campaigns") side
(`MyCampaignService`, `MyCampaignsController`) and the campaign detail customer metrics. The frontend is deferred to
a later task per the contract-first rule in `API_CONVENTIONS.md`.

Business rules: to be confirmed by the team (lead rules option 3; BR-25/BR-43 open).

**Revision 2026-11-24 (contract only, backend not yet changed):** a campaign has exactly one product (work type); a `Work` is created for every lead at allocation and its status mirrors the contact status; closing needs a transaction code and submits the Work. §5.0, §5.1, §5.2, §5.4, §5.5 below are the target behaviour; §5.6 and §5.7 are new.

### 5.0 Concept

A campaign holds an imported customer list (CIF, name, phone, note). The Dept Manager (campaign owner) imports the
list and splits it across campaign participants (auto rule or manual reassignment). Each participant tracks their
own customers' contact status. A campaign targets **one product** (one work type). Allocating a customer creates its
`Work` (status `NEW`, the campaign's work type, CIF and name); closing the customer with a transaction code submits
that Work, which feeds KPI through the existing submission → verification / Core Banking reconciliation flow (see
`WORK_CONTRACT.md`, `WORK_TRANSACTION_CONTRACT.md`, `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`). Contact status
itself never writes KPI; only the reconciled Work does.

### 5.1 Endpoints summary

| Method | Endpoint | Description | Auth / Role |
| :--- | :--- | :--- | :--- |
| `GET` | `/campaigns/{campaignId}/customers` | Lists a campaign's customers with filters and per-assignee status counts. | `[Authorize]` `campaign:read\|manage` (Dept) |
| `GET` | `/campaigns/{campaignId}/customers/import/template` | Downloads the `.xlsx` import template. | `[Authorize]` `campaign:manage` (Dept) |
| `POST` | `/campaigns/{campaignId}/customers/import/preview` | Validates an uploaded customer file without writing. | `[Authorize]` `campaign:manage` (Dept) |
| `POST` | `/campaigns/{campaignId}/customers/import/commit` | Re-validates and commits the same file. | `[Authorize]` `campaign:manage` (Dept) |
| `POST` | `/campaigns/{campaignId}/customers/allocate` | Assigns unassigned customers to active participants by rule. | `[Authorize]` `campaign:manage` (Dept) |
| `PUT` | `/campaigns/{campaignId}/customers/{customerId}/assignee` | Reassigns one customer to another active participant. | `[Authorize]` `campaign:manage` (Dept) |
| `POST` | `/campaigns/{id}/complete` | Completes a `RUNNING` campaign (§5.7). | `[Authorize]` `campaign:manage` (Dept) |
| `POST` | `/campaigns/{id}/cancel` | Cancels a `PLANNED` or `RUNNING` campaign; body `{ "reason": "..." }` required (§5.7). | `[Authorize]` `campaign:manage` (Dept) |
| `GET` | `/my-campaigns` | Lists campaigns where the caller has assigned customers, with per-status counts. | `[Authorize]` `campaign_customer:update` (Own) |
| `GET` | `/my-campaigns/{campaignId}/customers` | Lists the caller's own customers in the campaign. | `[Authorize]` `campaign_customer:update` (Own) |
| `PATCH` | `/my-campaigns/{campaignId}/customers/{customerId}/contact` | Updates the contact status/result of the caller's own customer. | `[Authorize]` `campaign_customer:update` (Own) |

File endpoints (`import/template`, `import/preview`, `import/commit`) follow the same notation as
`DATA_IMPORT_CONTRACT.md` §`/data-imports/employees/*`: `template` returns `200` with an `.xlsx` body and no
envelope; `preview` and `commit` take a multipart `file` field (`.xlsx`, magic-byte checked, ≤
`SystemParameters:ImportMaxFileMb`, ≤ `ImportMaxRows` data rows) and return `200` with `ApiResponse<ImportPreviewDto>`
/ `ApiResponse<ImportCommitResultDto>`; `commit` adds `Cache-Control: no-store`. Nothing is stored between preview
and commit.

### 5.2 DTOs

`CampaignCustomerDto`:
```json
{
  "id": "cc000000-0000-0000-0000-000000000001",
  "customerCif": "CUST-8801",
  "customerName": "Trần Văn An",
  "phone": "0901234567",
  "note": "Sổ tiết kiệm 500 triệu đáo hạn ngày 25/10",
  "assigneeEmployeeId": "e1f2a3b4-0000-0000-0000-000000000001",
  "assigneeEmployeeName": "Nguyễn Thu Thảo",
  "contactStatus": "CALLED",
  "resultText": "Không nghe máy 2 lần",
  "missedCallCount": 2,
  "appointmentAt": null,
  "metAt": null,
  "closedWorkTypeId": null,
  "closedWorkTypeName": null,
  "workId": "f0000000-0000-0000-0000-000000000001",
  "workStatus": "IN_PROGRESS",
  "updatedAt": "2026-11-23T10:00:00Z"
}
```

`resultText` is derived, never stored verbatim:

| `contactStatus` | `resultText` |
| :--- | :--- |
| `CALLED`, `callAnswered = true` | `Gọi thành công` |
| any status with `missedCallCount > 0` and not answered | `Không nghe máy {n} lần` |
| `APPOINTMENT_SET` | `Đã hẹn gặp vào {appointmentAt}` |
| `MET` | `Đã gặp vào {metAt}` |
| `CLOSED` | `Đã chốt được sản phẩm {closedWorkTypeName}` |
| `NOT_CONTACTED` | `Chưa liên hệ` |

Request DTOs:
```json
// POST .../customers/allocate (AllocateCampaignCustomersRequestDto)
{ "rule": "ROUND_ROBIN", "employeeIds": ["e1f2a3b4-0000-0000-0000-000000000001"] }

// PUT .../customers/{customerId}/assignee (ReassignCampaignCustomerRequestDto)
{ "employeeId": "e1f2a3b4-0000-0000-0000-000000000002" }

// PATCH my-campaigns/{campaignId}/customers/{customerId}/contact (UpdateCampaignCustomerContactRequestDto)
{
  "status": "CLOSED",
  "callAnswered": null,
  "appointmentAt": null,
  "metAt": null,
  "transactionCodes": ["TX-90001"],
  "note": "KH gửi tiết kiệm 500 triệu"
}
```

`transactionCodes` and `note` apply to `status = CLOSED` only. `transactionCodes` is required (1–10 codes, same format and
rules as `POST /works/{workId}/transactions`); `note` is optional (≤1000 chars) and is appended to the Work description.
`workTypeId` is no longer accepted: the product is the campaign's work type. The `closedWorkTypeId` / `closedWorkTypeName`
fields of `CampaignCustomerDto` are kept and are set to the campaign's work type when the customer is closed.

`rule` is one of `ROUND_ROBIN | LOAD_BALANCING | EQUAL_SPLIT` (same semantics as
`BatchWorkGeneratorRequestDto.allocationRule` in §3.5); `employeeIds` optional (defaults to all active participants).
Import endpoints reuse `ImportPreviewDto` / `ImportCommitResultDto` (`G54.BLL/Dtos/DataImport/DataImportDtos.cs`);
the import row columns are `Customer CIF*`, `Tên khách hàng*`, `Số điện thoại`, `Ghi chú`.

### 5.3 Enum wire values

`contactStatus` (`campaign_contact_status`): `NOT_CONTACTED | CALLED | APPOINTMENT_SET | MET | CLOSED`.

### 5.4 Error codes

| Error Code | HTTP Status | Context / Trigger Condition |
| :--- | :--- | :--- |
| `CAMPAIGN_CUSTOMER_CLOSED` | 409 | The customer row is already `CLOSED` (terminal) and a further contact update was attempted. |
| `CAMPAIGN_NOT_RUNNING` | 409 | Import/allocate/reassign: the campaign is not `PLANNED` or `RUNNING`. Contact update: the campaign is not `RUNNING`. |
| `WORK_TYPE_NOT_IN_CAMPAIGN` | 400 | The campaign's work type is inactive at close time. |
| `CAMPAIGN_HAS_ASSIGNMENTS` | 409 | `PUT /campaigns/{id}`: changing the work type or removing a participant while the campaign already has customers assigned or Works generated. Reassign first. |
| `CAMPAIGN_WORK_LOCKED` | 409 | A generic Work endpoint (status change, hand over, result submission, transaction codes) was called on a **lead Work** (a Work referenced by `campaign_customer.work_id`) whose customer is not `CLOSED`, or a status change / hand over on any lead Work; or the last transaction code of a lead Work was removed. |
| `WORK_TRANSACTION_LIMIT` / `WORK_TRANSACTION_DUPLICATE` | 400 / 409 | On `CLOSED`: more than 10 codes, or a code already used by another Work (rejected immediately). |
| `CAMPAIGN_NOT_COMPLETABLE` | 409 | `complete`/`cancel` on a campaign whose status does not allow it (§5.7). |
| `EMPLOYEE_NOT_PARTICIPANT` | 400 | The target employee of allocate/reassign is not an active participant of the campaign; or, on a contact update, the caller is no longer an active participant (removed from the campaign or `INACTIVE`, BR-41). |
| `NOT_FOUND` | 404 | Campaign or customer row not found, or the customer row does not belong to the caller (own-scope PATCH). |
| `VALIDATION_ERROR` | 400 | Missing required field for the target status (e.g. `callAnswered` for `CALLED`, `appointmentAt` for `APPOINTMENT_SET`, `metAt` for `MET`, `transactionCodes` for `CLOSED`), more than 2000 unassigned customers on allocate, or an invalid/non-`.xlsx` import file. |

### 5.5 Validation

- `customerCif` required, ≤50 chars, unique within the campaign (case per import rules); existing CIF on re-import updates name/phone/note only — assignment and status are kept.
- `customerName` required, ≤200 chars; `phone` optional, digits/`+`/space, 9–15 digits; `note` optional, ≤1000 chars.
- Import only when the campaign is `PLANNED` or `RUNNING`; max rows match the employee import cap.
- Allocate and reassign only when the campaign is `PLANNED` or `RUNNING`. Allocate only assigns customers with no current assignee; reassign is refused for a `CLOSED` row and requires the target employee to be an active participant.
- A contact update row must belong to the caller (own scope), the campaign must be `RUNNING`, and the caller must still be an active participant (a removed or `INACTIVE` participant keeps the rows but cannot update them).
- `CLOSED` is a terminal state: no further contact updates once a row is `CLOSED`.
- A campaign has exactly one work type: `POST /campaigns` and `PUT /campaigns/{id}` require `workTypeIds` of length 1. `batch-generate-work` requires its `workTypeId` to be the campaign's work type (`WORK_TYPE_NOT_IN_CAMPAIGN` otherwise) and no longer adds a work type to the campaign; its Works have no lead and are not locked. Campaigns that already hold several work types are cleaned by a data migration (§5.6).
- Allocate and reassign maintain the Work (§5.6). Reassigning a row that already has a contact status keeps the status and all recorded values.
- `CLOSED` requires `transactionCodes`; in one database transaction it records the codes on the Work, submits the Work (`WorkAction.Submit`, allowed from `NEW`/`IN_PROGRESS`/`REJECTED`/`OVERDUE`) and sets the status. A code not yet present in the Core Banking import leaves the Work in `SUBMITTED` until reconciliation (accepted). The Work description is `Nguồn: chiến dịch {code} - {name}. Mã KH: {cif}, Tên KH: {customerName}. Sản phẩm: {workTypeName}.`, followed by ` Ghi chú: {note}` from the customer note or the close note.
- Import of an existing CIF updates name/phone/note of a row that is not `CLOSED` and updates the Work's `customerName`, title and description with it; a `CLOSED` row is never updated.
- File endpoints accept `.xlsx` only, signature-checked, within the configured size/row limits.

### 5.6 Work mirror

Every assigned customer has one `Work` (`campaign_customer.work_id`, unique where not null). Allocate creates it, reassign
moves `assigneeEmployeeId`, the Work carries the campaign's work type, CIF, name, `plannedStartDate = today` and
`plannedEndDate = null` (the deadline is the campaign's `endDate`). A Work with this link is a **lead Work**.

| `contactStatus` | `Work.status` |
| :--- | :--- |
| `NOT_CONTACTED` | `NEW` |
| `CALLED`, `APPOINTMENT_SET`, `MET` | `IN_PROGRESS` |
| `CLOSED` | `SUBMITTED` (then follows the normal verification / reconciliation outcome) |

- A lead Work changes status only through `/my-campaigns/...`; `PATCH /works/{id}/status` and hand over always return `CAMPAIGN_WORK_LOCKED`. Result submission and `POST/DELETE /works/{id}/transactions` are allowed from My Work only once the customer is `CLOSED` (re-submit after `REJECTED`, or adding a code to a legacy Work). Removing the **last** code of a lead Work is refused (add the new code first). The customer stays `CLOSED`; `CampaignCustomerDto.workStatus` lets the list show `Kết quả bị từ chối` with a link to the Work.
- Works created by `batch-generate-work` or manually with a `businessCampaignId` have no lead and keep the normal Work rules.
- Lead Works have no `plannedEndDate`, so the overdue job and every overdue / on-time count skip them. They still count toward KPI once verified.
- Notifications: allocating customers sends one aggregate notification per employee (`Bạn được giao {n} khách hàng trong chiến dịch {code}`), never one per Work.
- My Work gets a `campaignId` filter; campaign Works are not shown in the default tab.
- The existing rule that `campaign.endDate` cannot be earlier than a generated Work's deadline stays (it only concerns Works with a date, i.e. not lead Works).
- Allocation limit per request: at most **2000** customers per allocate request (above that: `VALIDATION_ERROR`); Work codes are issued sequentially for the whole batch in one go.
- Legacy data: a migration (a) keeps exactly one `CampaignWorkType` per campaign (the work type used by most of its Works, ties by the oldest link); (b) keeps Works created at close under the old flow (their customer is `CLOSED`, so the assignee adds the transaction code from My Work); (c) creates a lead Work for every customer that is assigned, not `CLOSED` and has no Work; (d) clears `plannedEndDate` of existing lead Works. Verified Works and their KPI are untouched.

### 5.7 Campaign lifecycle

| Action | From | To | Effect on Works |
| :--- | :--- | :--- | :--- |
| `POST /campaigns/{id}/complete` | `RUNNING` | `COMPLETED` | Works not yet `SUBMITTED` become `CANCELLED` (`WorkAction.Cancel`); `SUBMITTED` and later are kept. Allowed with unclosed customers. |
| `POST /campaigns/{id}/cancel` | `PLANNED`, `RUNNING` | `CANCELLED` | Same as complete. Body `{ "reason": "..." }`, required, ≤1000 chars, stored in the audit log. |

- Both run in one transaction and are refused with `CAMPAIGN_NOT_COMPLETABLE` (409) from any other status.
- The campaign is **never completed automatically**. When `endDate` has passed and the campaign is still `RUNNING`, the system sends the campaign owner a notification asking them to complete it.
- Pause/Resume is out of scope for now. While a campaign is not `RUNNING`, employees still read their customers but cannot update them (`CAMPAIGN_NOT_RUNNING`).
