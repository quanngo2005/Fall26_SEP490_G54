# Work API Contract

Tài liệu đặc tả hợp đồng API cho phân hệ Quản lý công việc (**Work**).

Trạng thái: Backend đã triển khai (`WorksController`).
Nguồn tham chiếu quy ước chung: `docs/api/API_CONVENTIONS.md`.

---

## 1. Tổng quan và Quy ước chung

- **Giao thức và định dạng**: RESTful API qua HTTP/HTTPS. Mọi request body và response body đều sử dụng JSON chuẩn với tên trường ở định dạng `camelCase`.
- **Envelope chuẩn**: Mọi response (thành công lẫn lỗi) đều được đóng gói trong envelope `ApiResponse` (hoặc `ApiResponse<T>`) gồm các trường:
  - `success` (`boolean`): Trạng thái thành công của yêu cầu (`true` / `false`).
  - `status` (`number`): Mã trạng thái HTTP (200, 201, 400, 401, 404, 500...).
  - `message` (`string | null`): Thông điệp mô tả kết quả xử lý hoặc lỗi.
  - `errorCode` (`string | null`): Mã lỗi nghiệp vụ ổn định (xem Bảng mã lỗi).
  - `errors` (`string[] | null`): Danh sách lỗi chi tiết (nếu có, ví dụ khi lỗi model state DataAnnotations).
  - `traceId` (`string | null`): Định danh vết yêu cầu tương ứng với header `X-Trace-Id`.
  - `data` (`T | null`): Dữ liệu trả về đối với response thành công (`ApiResponse<T>`).
- **Header**: Mọi response luôn kèm theo header `X-Trace-Id` để theo dõi và hỗ trợ kỹ thuật.
- **Xác thực (Authentication)**: Tất cả endpoint trong tài liệu này đều yêu cầu JWT Access Token hợp lệ thông qua header:
  ```http
  Authorization: Bearer <access_token>
  ```
  Nếu thiếu hoặc token không hợp lệ, hệ thống trả về mã lỗi HTTP 401 (`UNAUTHORIZED`).

---

## 2. Danh sách Endpoint

| Phương thức | Route | Xác thực / Quyền | Mô tả |
|---|---|---|---|
| `POST` | `/works` | `[Authorize]` (Mọi tài khoản đã xác thực) | Khởi tạo và phân công công việc mới |
| `GET` | `/works` | `work:read` (scope `Own` trở lên) | Danh sách công việc: lọc theo trạng thái / người thực hiện / phòng ban, phân trang |
| `GET` | `/works/{id}` | `[Authorize]` (Mọi tài khoản đã xác thực) | Lấy thông tin chi tiết công việc theo Id |
| `PATCH` | `/works/{id}/status` | `[Authorize]` + kiểm tra assignee / quản lý trực tiếp tại service | Chuyển trạng thái công việc theo máy trạng thái (mục 7) |

---

## 3. Chi tiết Endpoint

### 3.1. `POST /works` - Tạo và phân công công việc mới

Khởi tạo một công việc mới trong hệ thống và giao cho nhân viên thực hiện.

#### Bảng trường Request Body (`CreateWorkRequestDto`)

| Tên trường | Kiểu dữ liệu | Bắt buộc | Ràng buộc / Giới hạn | Giá trị mặc định | Ghi chú |
|---|---|---|---|---|---|
| `title` | `string` | **Có** | Độ dài từ 5 đến 255 ký tự | Không | Tiêu đề công việc, không được chỉ chứa khoảng trắng. |
| `workTypeId` | `string (uuid)` | **Có** | Phải tồn tại và đang `Active` | Không | Id loại công việc. Nếu không tồn tại hoặc inactive sẽ trả về lỗi 400 (`VALIDATION_ERROR`). |
| `assigneeEmployeeId` | `string (uuid)` | **Có** | Phải tồn tại và đang `Active` | Không | Id nhân viên được giao việc. Nếu có `businessCampaignId` thì nhân viên phải là Participant của campaign đó. Nếu không thỏa mãn trả 400 (`VALIDATION_ERROR`). |
| `priority` | `string` | Không | `Low`, `Medium`, `High`, `Critical`, hoặc alias `P1`, `P2` | `"Medium"` | Mức độ ưu tiên (không phân biệt hoa/thường). `P1`/`Critical` -> Critical; `P2`/`High` -> High; `Low` -> Low; mọi giá trị khác được âm thầm đưa về `Medium`. |
| `plannedStartDate` | `string (date)` | Không | Định dạng `YYYY-MM-DD` | Ngày hiện tại theo giờ Việt Nam (UTC+7) | Ngày dự kiến bắt đầu công việc. |
| `plannedEndDate` | `string (date)` | Không | Định dạng `YYYY-MM-DD` | `null` | Hạn hoàn thành dự kiến. Không được nhỏ hơn `plannedStartDate`. Nếu có chiến dịch thì không được vượt quá `endDate` của chiến dịch. |
| `estimatedHours` | `number (decimal)` | Không | Từ `0` đến `9999.99` | `null` | Số giờ ước tính hoàn thành công việc. Không được âm. |
| `businessCampaignId` | `string (uuid)` | Không | Phải tồn tại và ở trạng thái `Running` hoặc `Planned` | `null` | Id chiến dịch kinh doanh liên kết. Nếu truyền Id không tồn tại sẽ trả về lỗi 404 (`NOT_FOUND`). |
| `description` | `string` | Không | Tối đa 2000 ký tự | `null` | Mô tả chi tiết nội dung công việc. |
| `checklistItems` | `string[]` | Không | Danh sách chuỗi | `null` | Danh sách các đầu việc con. **Hiện tại server chưa xử lý và bỏ qua trường này.** |

#### Ví dụ Request Body

```json
{
  "title": "Liên hệ khách hàng tiềm năng đợt 1",
  "workTypeId": "b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
  "assigneeEmployeeId": "c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f",
  "priority": "High",
  "plannedStartDate": "2026-10-20",
  "plannedEndDate": "2026-10-31",
  "estimatedHours": 16.5,
  "businessCampaignId": "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70",
  "description": "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
  "checklistItems": [
    "Lấy danh sách khách hàng",
    "Gọi điện tư vấn"
  ]
}
```

#### Ví dụ Response Thành công (`201 Created`)

HTTP Headers:
```http
HTTP/1.1 201 Created
Location: /works/3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10
Content-Type: application/json; charset=utf-8
X-Trace-Id: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00
```

Response Body:
*(Lưu ý: Trường `status` trong body trả về 200 do `ApiResponse.Ok` khởi tạo, trong khi HTTP Status là 201).*

```json
{
  "success": true,
  "status": 200,
  "message": "Khởi tạo và phân công công việc thành công.",
  "data": {
    "id": "3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10",
    "code": "WRK-20261019-0001",
    "title": "Liên hệ khách hàng tiềm năng đợt 1",
    "description": "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
    "workTypeId": "b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
    "workTypeName": "Chăm sóc khách hàng",
    "businessCampaignId": "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70",
    "businessCampaignName": "Chiến dịch Q4 2026",
    "assigneeEmployeeId": "c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f",
    "assigneeEmployeeName": "Nguyễn Văn An",
    "assigneeEmployeeCode": "EMP001",
    "priority": "High",
    "status": "New",
    "plannedStartDate": "2026-10-20",
    "plannedEndDate": "2026-10-31",
    "estimatedHours": 16.5,
    "updatedAt": "2026-10-19T08:30:00+00:00"
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

#### Các mã lỗi có thể trả về

- **400 Bad Request (`VALIDATION_ERROR`)**:
  ```json
  {
    "success": false,
    "status": 400,
    "title": "VALIDATION_ERROR",
    "errorCode": "VALIDATION_ERROR",
    "message": "Title: The field Title must be a string with a minimum length of 5 and a maximum length of 255.",
    "errors": [
      "Title: The field Title must be a string with a minimum length of 5 and a maximum length of 255."
    ],
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
  }
  ```
- **409 Conflict (`CONFLICT`)**:
  ```json
  {
    "success": false,
    "status": 409,
    "title": "CONFLICT",
    "errorCode": "CONFLICT",
    "message": "Chiến dịch được gắn không trong trạng thái hoạt động.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
  }
  ```
- **401 Unauthorized (`UNAUTHORIZED`)**:
  ```json
  {
    "success": false,
    "status": 401,
    "title": "UNAUTHORIZED",
    "errorCode": "UNAUTHORIZED",
    "message": "Phiên làm việc không hợp lệ hoặc đã hết hạn.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
  }
  ```
- **404 Not Found (`NOT_FOUND`)**:
  *Chỉ xảy ra khi `businessCampaignId` không tồn tại trong CSDL.*
  ```json
  {
    "success": false,
    "status": 404,
    "title": "NOT_FOUND",
    "errorCode": "NOT_FOUND",
    "message": "Chiến dịch với khóa 'd4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70' không tồn tại.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
  }
  ```
- **500 Internal Server Error (`INTERNAL_ERROR`)**:
  ```json
  {
    "success": false,
    "status": 500,
    "title": "Internal Server Error",
    "errorCode": "INTERNAL_ERROR",
    "message": "An unexpected error occurred. Contact support with the request trace identifier.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
  }
  ```

---

### 3.1a. `GET /works` - Danh sách công việc (lọc, phân trang)

Yêu cầu quyền `work:read` (`[HasPermission("work", "read", "Own")]`). Phạm vi dữ liệu theo scope được cấp (người gọi phải là tài khoản đang hoạt động, không bị khóa):

| Scope được cấp | Công việc nhìn thấy |
|---|---|
| `All` / `System` / full access | Mọi công việc |
| `Branch` | Công việc của nhân viên thuộc cây phòng ban của chi nhánh (`BRANCH` gần nhất, hoặc gốc cao nhất nếu không có) chứa phòng ban của người gọi |
| `Department` / `Dept` | Công việc của nhân viên thuộc phòng ban của người gọi và các phòng ban con |
| `Own` | Chỉ công việc được giao cho chính người gọi |

Các bộ lọc luôn được **giao** với phạm vi trên (ví dụ scope `Own` lọc theo người khác sẽ ra danh sách rỗng).
Phòng ban nhân viên được tính theo phòng ban **hiện tại của người thực hiện** (`assigneeEmployee.departmentId`).

#### Query

| Tham số | Kiểu | Mô tả |
|---|---|---|
| `status` | `string` (lặp lại hoặc phân tách bằng dấu phẩy) | Một hoặc nhiều trạng thái: `New`, `InProgress`, `HandedOver`, `Submitted`, `Verified`, `Rejected`, `Overdue`, `Done`, `Cancelled` (PascalCase hoặc `UPPER_SNAKE`, không phân biệt hoa/thường). Ví dụ `?status=New&status=Overdue` hoặc `?status=New,Overdue`. Bỏ trống = mọi trạng thái. |
| `assigneeEmployeeId` | `uuid` | Người thực hiện. |
| `departmentId` | `uuid` | Phòng ban của người thực hiện. Không tồn tại -> 404; ngoài phạm vi quyền -> 403. |
| `includeChildDepartments` | `bool` (mặc định `true`) | Có gồm các phòng ban con của `departmentId` hay không. |
| `workTypeId` | `uuid` | Loại công việc. |
| `priority` | `string` | `Low`, `Medium`, `High`, `Critical` (không phân biệt hoa/thường). |
| `keyword` | `string` | Chứa trong mã hoặc tiêu đề (không phân biệt hoa/thường). |
| `page` | `int` (mặc định 1) | Trang, tối thiểu 1. |
| `pageSize` | `int` (mặc định 20) | Kích thước trang, giới hạn 1..100. |

Giá trị `status`/`priority` không hợp lệ -> 400 `VALIDATION_ERROR`. Sắp xếp: `updatedAt` giảm dần, rồi `code`.

#### Response `200 OK` (`PagedResult<WorkListItemDto>`)

```json
{
  "success": true,
  "status": 200,
  "data": {
    "items": [
      {
        "id": "…", "code": "WRK-20261022-1234", "title": "Mở tài khoản khách hàng A",
        "workTypeId": "…", "workTypeName": "Mở tài khoản khách hàng", "businessCampaignId": null,
        "assigneeEmployeeId": "…", "assigneeEmployeeCode": "NV004", "assigneeEmployeeName": "Lê Minh Châu",
        "departmentId": "…", "departmentCode": "CN-HN-KD-N1", "departmentName": "Nhóm Kinh doanh 1",
        "priority": "Medium", "status": "InProgress",
        "plannedStartDate": "2026-10-20", "plannedEndDate": "2026-10-30",
        "updatedAt": "2026-10-22T08:00:00+00:00"
      }
    ],
    "totalCount": 1, "page": 1, "pageSize": 20
  }
}
```

`priority` và `status` là PascalCase, giống `WorkDetailDto`. Danh sách không trả `description`/`estimatedHours`; dùng `GET /works/{id}` để xem chi tiết.
Lỗi: 400 `VALIDATION_ERROR`, 401, 403 (không có quyền `work:read`, tài khoản khóa, hoặc `departmentId` ngoài phạm vi), 404 (`departmentId` không tồn tại).

---

### 3.2. `GET /works/{id}` - Lấy thông tin chi tiết công việc

Truy vấn thông tin chi tiết của một công việc theo định danh duy nhất (UUID).

#### Tham số Đường dẫn (Path Parameter)

| Tham số | Kiểu dữ liệu | Bắt buộc | Mô tả |
|---|---|---|---|
| `id` | `string (uuid)` | **Có** | Id định danh của công việc (Ví dụ: `3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10`) |

#### Ví dụ Response Thành công (`200 OK`)

```json
{
  "success": true,
  "status": 200,
  "data": {
    "id": "3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10",
    "code": "WRK-20261019-0001",
    "title": "Liên hệ khách hàng tiềm năng đợt 1",
    "description": "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
    "workTypeId": "b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
    "workTypeName": "Chăm sóc khách hàng",
    "businessCampaignId": "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70",
    "businessCampaignName": "Chiến dịch Q4 2026",
    "assigneeEmployeeId": "c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f",
    "assigneeEmployeeName": "Nguyễn Văn An",
    "assigneeEmployeeCode": "EMP001",
    "priority": "High",
    "status": "New",
    "plannedStartDate": "2026-10-20",
    "plannedEndDate": "2026-10-31",
    "estimatedHours": 16.5,
    "updatedAt": "2026-10-19T08:30:00+00:00"
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

#### Ví dụ Response 404 Không tìm thấy (`404 Not Found`)

```json
{
  "success": false,
  "status": 404,
  "title": "NOT_FOUND",
  "errorCode": "NOT_FOUND",
  "message": "Không tìm thấy công việc yêu cầu.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

### 3.3. `PATCH /works/{id}/status` - Chuyển trạng thái công việc

Chuyển trạng thái thủ công của công việc theo máy trạng thái (xem mục 7). Chỉ các trạng thái đích **`InProgress`**, **`Done`**, **`Cancelled`** được phép đặt thủ công. `Submitted`, `Verified`, `Rejected` do luồng nộp/thẩm định kết quả cập nhật; `Overdue` do tiến trình nền cập nhật; `HandedOver` do luồng bàn giao cập nhật.

#### Quyền thực hiện (kiểm tra ở service)

| Trạng thái đích | Người được phép |
|---|---|
| `InProgress` | Nhân viên được giao việc (`assigneeEmployeeId`) |
| `Done`, `Cancelled` | Quản lý trực tiếp của nhân viên được giao việc (`manager` của assignee) |

Người dùng khác nhận 403 (`FORBIDDEN`).

#### Request Body (`WorkStatusDto`)

| Tên trường | Kiểu dữ liệu | Bắt buộc | Ràng buộc | Ghi chú |
|---|---|---|---|---|
| `status` | `string` | **Có** | `InProgress`, `Done`, `Cancelled` (không phân biệt hoa/thường; chấp nhận cả `IN_PROGRESS`) | Trạng thái đích. |
| `reason` | `string` | Không | Tối đa 500 ký tự | Bắt buộc khi `status` là `Cancelled`. |

```json
{
  "status": "InProgress"
}
```

#### Response Thành công (`200 OK`)

`data` là `WorkDetailDto` (xem mục 4) với `status` và `updatedAt` mới. Định dạng `status` trong response giữ nguyên **PascalCase** như `GET /works/{id}`.

#### Các mã lỗi

| HTTP Status | errorCode | Trường hợp |
|---|---|---|
| 400 | `VALIDATION_ERROR` | `status` thiếu / không hợp lệ / là trạng thái chỉ hệ thống được đặt; `reason` thiếu khi hủy hoặc quá 500 ký tự |
| 401 | `UNAUTHORIZED` | Thiếu hoặc sai token |
| 403 | `FORBIDDEN` | Người gọi không phải assignee (đích `InProgress`) hoặc không phải quản lý trực tiếp của assignee (đích `Done`/`Cancelled`) |
| 404 | `NOT_FOUND` | Không tìm thấy công việc |
| 409 | `WORK_INVALID_STATE` | Trạng thái hiện tại không cho phép chuyển sang trạng thái đích (kể cả khi đích trùng trạng thái hiện tại) |

---

## 4. Mô hình Dữ liệu Chi tiết (`WorkDetailDto`)

Đối tượng `data` trong response của `POST /works` và `GET /works/{id}` gồm 17 trường (cũng là `data` của `PATCH /works/{id}/status`):

| Tên trường | Kiểu dữ liệu | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string (uuid)` | Không | Id định danh duy nhất của công việc. |
| `code` | `string` | Không | Mã công việc do hệ thống sinh tự động (`WRK-yyyyMMdd-####`, `yyyyMMdd` là ngày hiện tại theo giờ Việt Nam, UTC+7). |
| `title` | `string` | Không | Tiêu đề công việc. |
| `description` | `string` | Có | Nội dung mô tả chi tiết công việc. |
| `workTypeId` | `string (uuid)` | Có | Id loại công việc liên kết. |
| `workTypeName` | `string` | Có | Tên loại công việc liên kết (null khi không gắn loại công việc). |
| `businessCampaignId` | `string (uuid)` | Có | Id chiến dịch kinh doanh liên kết. |
| `businessCampaignName` | `string` | Có | Tên chiến dịch kinh doanh liên kết (null khi không gắn chiến dịch). |
| `assigneeEmployeeId` | `string (uuid)` | Không | Id nhân viên được phân công công việc. |
| `assigneeEmployeeName` | `string` | Có | Họ và tên nhân viên được phân công. |
| `assigneeEmployeeCode` | `string` | Có | Mã nhân viên được phân công (ví dụ: `EMP001`). |
| `priority` | `string` | Không | Mức độ ưu tiên, định dạng **PascalCase**: `Low`, `Medium`, `High`, `Critical`. |
| `status` | `string` | Không | Trạng thái công việc, định dạng **PascalCase**: `New`, `InProgress`, `HandedOver`, `Submitted`, `Verified`, `Rejected`, `Overdue`, `Done`, `Cancelled`. Khi khởi tạo luôn là `New`. Xem máy trạng thái ở mục 7. |
| `plannedStartDate` | `string (date)` | Có | Ngày bắt đầu dự kiến (`YYYY-MM-DD`). |
| `plannedEndDate` | `string (date)` | Có | Hạn hoàn thành dự kiến (`YYYY-MM-DD`). |
| `estimatedHours` | `number (decimal)` | Có | Số giờ ước tính hoàn thành công việc. |
| `updatedAt` | `string (date-time)`| Không | Thời điểm cập nhật cuối cùng (ISO 8601 kèm múi giờ offset). |

---

## 5. Bảng Mã Lỗi Thực tế

| HTTP Status | errorCode | Nguồn phát sinh | Trường hợp kích hoạt | Message thực tế |
|---|---|---|---|---|
| 400 | `VALIDATION_ERROR` | `InvalidModelState` (ASP.NET Core Model Binding / DataAnnotations) | `Title` thiếu hoặc độ dài ngoài 5-255; `EstimatedHours` ngoài 0-9999.99; `Description` vượt 2000; lỗi parse kiểu dữ liệu JSON | `field: message` (Ví dụ: `"Title: The field Title must be a string with a minimum length of 5 and a maximum length of 255."`) |
| 400 | `VALIDATION_ERROR` | `ValidationException` (`WorkService`) | `Title` rỗng sau khi trim hoặc ngoài 5-255; `PlannedEndDate` &lt; `PlannedStartDate`; `EstimatedHours` &lt; 0; `WorkTypeId` không tồn tại hoặc không Active; `AssigneeEmployeeId` không tồn tại hoặc không Active | Message chi tiết tương ứng từ service (ví dụ: `"Loại công việc không tồn tại hoặc đã bị vô hiệu hóa."`, `"Nhân viên được phân công không tồn tại hoặc không ở trạng thái hoạt động."`) |
| 400 | `VALIDATION_ERROR` | `ValidationException` (`WorkService`) | **POST /works**: `workTypeId` không nằm trong danh sách loại công việc đã gán cho chức danh của nhân viên được giao (bảng `job_position_work_type`, quản lý qua `JOB_POSITION_CONTRACT.md`) | `"Loại công việc không hợp lệ với chức danh của nhân viên được phân công."` |
| 400 | `VALIDATION_ERROR` | `BusinessRuleException` (`WorkService`) | Gắn chiến dịch nhưng nhân viên được giao việc chưa là Participant trong chiến dịch | `"Nhân viên được giao việc chưa được đăng ký tham gia (Participant) trong chiến dịch này."` |
| 409 | `CONFLICT` | `ConflictException` (`WorkService`) | Chiến dịch không ở trạng thái `Running` hoặc `Planned` | `"Chiến dịch được gắn không trong trạng thái hoạt động."` |
| 400 | `VALIDATION_ERROR` | `ValidationException` (`WorkService`) | `PlannedEndDate` vượt quá `EndDate` của chiến dịch | `"Hạn hoàn thành công việc (...) vượt quá ngày kết thúc chiến dịch (...)."` |
| 401 | `UNAUTHORIZED` | JwtBearer `OnChallenge` (`Program.cs`) | Thiếu header `Authorization`, token sai định dạng, token hết hạn hoặc token đã bị thu hồi | `"Phiên làm việc không hợp lệ hoặc đã hết hạn."` |
| 403 | `FORBIDDEN` | JwtBearer `OnForbidden` (`Program.cs`) | Người dùng không có quyền truy cập endpoint | `"Bạn không có quyền thực hiện thao tác này."` *(Hiện tại controller chỉ có `[Authorize]`, chưa có policy/permission nên thực tế không xuất hiện trong luồng chuẩn).* |
| 404 | `NOT_FOUND` | `NotFoundException` (`WorkService`) | **POST /works**: `businessCampaignId` truyền lên không tồn tại trong hệ thống | `"Chiến dịch với khóa '{id}' không tồn tại."` |
| 404 | `NOT_FOUND` | `WorksController.GetById` | **GET /works/{id}**: Không tìm thấy công việc với Id yêu cầu | `"Không tìm thấy công việc yêu cầu."` |
| 409 | `WORK_INVALID_STATE` | `ConflictException` (`WorkService`) | **PATCH /works/{id}/status** (và các luồng nộp/thẩm định/quá hạn): chuyển trạng thái không nằm trong bảng ở mục 7.2 | `Không thể chuyển công việc từ trạng thái {from} sang {to}.` |
| 500 | `INTERNAL_ERROR` | `GlobalExceptionHandler` | Lỗi ngoại lệ hệ thống không được xử lý | `"An unexpected error occurred. Contact support with the request trace identifier."` |

---

## 6. Lưu ý và Khác biệt Hiện tại

1. **Lệch HTTP Status và Body Status ở POST /works**: `POST /works` trả về mã HTTP là **201 Created** kèm header `Location`, nhưng trường `status` trong body JSON envelope là **200** do hàm `ApiResponse.Ok` khởi tạo mặc định. Client cần căn cứ vào HTTP Status Code làm nguồn chân lý.
2. **Enum trả về PascalCase**: Mặc dù một số enum trong hệ thống dùng `UPPER_SNAKE` trên wire format, hai trường `priority` và `status` của Work API được chuyển thành chuỗi bằng `.ToString()` từ C# enum nên có định dạng **PascalCase** (`New`, `InProgress`, `High`...). Client cần xử lý chính xác định dạng này.
3. **Mức ưu tiên không hợp lệ bị fallback về Medium**: Hàm `ParsePriority` xử lý case-insensitive (không phân biệt chữ hoa/thường): `"P1"` / `"Critical"` thành `Critical`, `"P2"` / `"High"` thành `High`, `"Low"` thành `Low`. Mọi giá trị khác (kể cả `"Medium"` hay chuỗi sai chính tả như `"Normal"`, `"abc"`) đều được âm thầm đưa về `Medium` mà **không báo lỗi**.
4. **`checklistItems` chưa được lưu trữ**: Trường `checklistItems` có mặt trong request DTO nhưng service hiện tại chưa xử lý lưu vào cơ sở dữ liệu (bị bỏ qua).
5. **Ràng buộc `[Required]` với `Guid.Empty`**: Thuộc tính `[Required]` của DataAnnotations coi `Guid.Empty` (`00000000-0000-0000-0000-000000000000`) là giá trị hợp lệ. Tuy nhiên khi vào service, câu truy vấn tìm kiếm theo `Guid.Empty` sẽ không tìm thấy bản ghi và ném ra lỗi 400 (`VALIDATION_ERROR`).
6. **Không tìm thấy WorkType hoặc Assignee trả 400, không phải 404**: Khác với chiến dịch (`businessCampaignId` không tồn tại trả 404), khi `workTypeId` hoặc `assigneeEmployeeId` không tồn tại (hoặc không ở trạng thái Active), service ném `ValidationException` dẫn tới mã HTTP trả về là **400** (`VALIDATION_ERROR`).
7. **Lỗi gắn chiến dịch dùng mã chung**: chiến dịch không ở trạng thái `Running`/`Planned` trả 409 `CONFLICT`; hạn hoàn thành công việc vượt quá ngày kết thúc chiến dịch trả 400 `VALIDATION_ERROR`. Client phân biệt qua `message`, không qua `errorCode` (trước đây cả hai dùng chung `MSG18`).
8. **WorkType phải hợp lệ theo chức danh của người được giao**: `POST /works` chỉ chấp nhận `workTypeId` đã được gán cho chức danh (`jobPositionId`) của `assigneeEmployeeId` (xem `GET /job-positions/{id}/work-types`). Chức danh chưa gán loại công việc nào thì mọi `workTypeId` đều bị từ chối. Kiểm tra này chạy sau khi xác nhận WorkType và Assignee đều Active, nên lỗi không tồn tại/không Active vẫn có message riêng. Lỗi trả 400 `VALIDATION_ERROR`; client nên lọc dropdown loại công việc theo chức danh người được giao để tránh lỗi này.

---

## 7. Máy Trạng thái Công việc (Work State Machine)

Nguồn chân lý duy nhất là `WorkStateMachine` (`G54.BLL/Common/Workflow`), cùng kiểu với `SubmissionStateMachine`: máy chỉ trả lời "hành động `WorkAction` này từ trạng thái hiện tại dẫn tới trạng thái nào" qua `TryGetNextStatus`; service gọi máy rồi tự quyết định lỗi. Mọi service (`WorkService`, `ResultSubmissionService`, `VerificationService`, tiến trình quá hạn) đều phải đi qua máy này. `PATCH /works/{id}/status` chuyển trạng thái không hợp lệ trả 409 `WORK_INVALID_STATE`; nộp/thu hồi báo cáo trả 409 `SUBMISSION_INVALID_STATE`; thẩm định trả 409 `VERIFICATION_INVALID_STATE`.

### 7.1. Trạng thái

| Giá trị | Ý nghĩa | Loại |
|---|---|---|
| `New` | Mới giao, chưa bắt đầu | Mở |
| `InProgress` | Đang thực hiện | Mở |
| `HandedOver` | Đang bàn giao cho người khác | Mở |
| `Submitted` | Đã nộp kết quả, chờ thẩm định | Mở |
| `Rejected` | Kết quả bị từ chối, cần làm lại | Mở |
| `Overdue` | Quá hạn (`plannedEndDate` &lt; ngày hiện tại theo giờ Việt Nam, UTC+7) mà chưa nộp | Mở |
| `Verified` | Kết quả đã được thẩm định đạt | Mở |
| `Done` | Hoàn tất | Kết thúc |
| `Cancelled` | Đã hủy | Kết thúc |

### 7.2. Bảng chuyển trạng thái

| Từ | Sang (được phép) | Tác nhân |
|---|---|---|
| `New` | `InProgress`, `Submitted`, `HandedOver`, `Overdue`, `Cancelled` | Assignee / nộp kết quả / hệ thống / quản lý |
| `InProgress` | `Submitted`, `HandedOver`, `Overdue`, `Cancelled`; `Verified`, `Rejected`, `InProgress` khi thẩm định lại sau yêu cầu bổ sung | Nộp kết quả / hệ thống / quản lý / thẩm định |
| `HandedOver` | `InProgress`, `Overdue`, `Cancelled` | Assignee mới / hệ thống / quản lý |
| `Submitted` | `Verified`, `Rejected`, `InProgress` | Thẩm định; `InProgress` do thu hồi báo cáo hoặc yêu cầu bổ sung |
| `Rejected` | `InProgress`, `Submitted`, `Cancelled` | Assignee làm lại / nộp lại / quản lý |
| `Overdue` | `Submitted`, `Cancelled` | Nộp trễ / quản lý (không thể quay lại `InProgress`, xem 7.3) |
| `Verified` | `Done` | Quản lý |
| `Done`, `Cancelled` | *(không có)* | Trạng thái kết thúc |

`New -> Submitted` và `Rejected -> Submitted` được giữ lại để tương thích hành vi nộp kết quả hiện có (nộp trực tiếp, không cần bấm "bắt đầu").


Cùng bảng trên, nhìn theo hành động (`WorkAction`, đúng như `WorkStateMachine` định nghĩa):

| Hành động | Từ | Sang | Ai thực hiện |
|---|---|---|---|
| `Start` | `New`, `HandedOver`, `Rejected` | `InProgress` | Assignee (`PATCH`) |
| `Submit` | `New`, `InProgress`, `Rejected`, `Overdue` | `Submitted` | Assignee (nộp kết quả) |
| `Reopen` | `Submitted`, `InProgress` | `InProgress` | Assignee thu hồi báo cáo, hoặc người thẩm định yêu cầu bổ sung |
| `Verify` | `Submitted`, `InProgress` | `Verified` | Người thẩm định |
| `Reject` | `Submitted`, `InProgress` | `Rejected` | Người thẩm định |
| `Complete` | `Verified` | `Done` | Quản lý trực tiếp của assignee (`PATCH`) |
| `Cancel` | `New`, `InProgress`, `HandedOver`, `Rejected`, `Overdue` | `Cancelled` | Quản lý trực tiếp của assignee (`PATCH`, bắt buộc `reason`) |
| `Handover` | `New`, `InProgress` | `HandedOver` | Người nhận chấp nhận yêu cầu bàn giao (`POST /handovers/{id}/accept`, xem `HANDOVER_CONTRACT.md`) |
| `MarkOverdue` | `New`, `InProgress`, `HandedOver` | `Overdue` | Tiến trình nền |

Sau khi người thẩm định yêu cầu bổ sung (`REVISION_REQUIRED`), công việc về `InProgress` còn báo cáo vẫn ở `UNDER_REVIEW`. `SubmissionStateMachine` cho phép thẩm định lại báo cáo đó, nên `Verify`, `Reject` và `Reopen` cũng được phép từ `InProgress`.

### 7.3. Quá hạn (Overdue)

- `Overdue` là giá trị lưu trong cột `work.status` (enum PostgreSQL `work_status`, giá trị `OVERDUE`).
- Tiến trình nền `WorkOverdueBackgroundService` chạy định kỳ (`WorkOverdue:IntervalMinutes`, mặc định 15 phút; `WorkOverdue:Enabled`, mặc định `true`) và chuyển các công việc `New`, `InProgress`, `HandedOver` có `plannedEndDate` &lt; ngày hiện tại theo giờ Việt Nam (UTC+7, `TimeProvider.GetBusinessToday()`) sang `Overdue`. `plannedEndDate` là ngày lịch do người dùng nhập, nên việc có hạn 21/10 chỉ bị chuyển `Overdue` từ 00:00 ngày 22/10 giờ Việt Nam.
- `Submitted`, `Verified`, `Rejected` không bị chuyển quá hạn (đang chờ thẩm định hoặc đã có kết quả).
- Công việc `Overdue` **không thể** quay lại `InProgress` bằng `PATCH /works/{id}/status`: tiến trình nền sẽ chuyển lại về `Overdue` ở lần chạy kế tiếp (hạn vẫn nằm trong quá khứ), nên trạng thái chỉ nhảy qua lại. Yêu cầu này trả 409 `WORK_INVALID_STATE`. Công việc rời `Overdue` bằng cách nộp kết quả (`Submitted`) hoặc bị hủy (`Cancelled`).
- Công việc `Overdue` vẫn được nộp kết quả (`Overdue -> Submitted`), hiện chưa có quy tắc chặn nộp trễ nào được thực thi (`CAMPAIGN_DEADLINE_EXPIRED` chỉ được dự phòng trong `SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md`).
- Thống kê phòng ban: công việc `Overdue` được tính vào số lượng quá hạn và là công việc đang mở.
