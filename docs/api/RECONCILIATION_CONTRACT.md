# API Contract & Error Mapping — Reconciliation (Đối soát giao dịch)

> Nguồn chuẩn cho BE và FE. Mọi thay đổi route / mã lỗi phải cập nhật file này.
> Trạng thái: **BE đã implement** (unit test với InMemory, chưa chạy qua HTTP thật); FE `/reconciliations` và `/reconciliations/:id` đã nối vào các endpoint này.

## 1. Phạm vi và nguyên tắc

- Màn hình **Kết quả đối soát** (SCR-REV-03) và **Chi tiết đối soát** (SCR-REV-03-D1) cho QA, Quản trị, Trưởng phòng và Giám đốc chi nhánh.
- **Không có API Core Banking.** Hệ thống chạy độc lập: "dữ liệu Core Banking" chỉ là các dòng của **file Excel giao dịch đã nhập** (`POST /transaction-imports`, xem `TRANSACTION_IMPORT_CONTRACT.md`). Mọi so sánh trong màn hình này là giữa mã giao dịch nhân viên khai trên công việc và các dòng của file đã nhập; không có lời gọi ra ngoài, không có số liệu "thời gian thực".
- Đơn vị đối soát là **một mã giao dịch của một công việc** (`work_transaction`), không phải một phiên bản bài nộp. Một công việc có tối đa 10 mã, mỗi mã có trạng thái riêng (`WORK_TRANSACTION_CONTRACT.md`).
- Màn hình chỉ **xem**. Hành động duy nhất đi kèm là mở hộp thoại ghi sự kiện rủi ro hoặc gắn cờ chất lượng (các API đã có), nếu người dùng có quyền.

Envelope, quy ước route, enum, thời gian: xem `API_CONVENTIONS.md`.

## 2. Quyền và phạm vi

Quyền `reconciliation:read` (migration `GrantReconciliationRead` và `GrantQaPermissionsToVerifier`, và `seed_dev_data.sql`):

| Vai trò | Scope | Thấy |
|---|---|---|
| VERIFIER (người kiểm soát chất lượng), SYSTEM_ADMIN | `All` | Mọi phòng ban |
| BRANCH_MANAGER | `Branch` | Chi nhánh của mình (kể cả phòng con) |
| DEPT_MANAGER | `Dept` | Phòng của mình (kể cả phòng con) |
| EMPLOYEE, HR_OFFICER | không có | 403 |

Mã nằm ngoài phạm vi của người gọi trả **404** (giống mã không tồn tại), không lộ sự tồn tại. Hộp thoại **gắn cờ chất lượng** cần `quality_flag:create`, **ghi sự kiện rủi ro** cần `risk_event:create` (chỉ QA/Verifier và Quản trị có); Trưởng phòng và Giám đốc chi nhánh chỉ xem.

## 3. Route

| Method | Route | Mô tả |
|---|---|---|
| GET | `/reconciliations` | Danh sách theo bộ lọc, 200 `ReconciliationListDto` |
| GET | `/reconciliations/{id}` | Chi tiết một mã giao dịch, 200 `ReconciliationDetailDto` |
| GET | `/reconciliations/export` | Cùng bộ lọc với danh sách, trả file `.xlsx` (tối đa 10.000 dòng) |

### Query của danh sách và export

| Tham số | Quy tắc |
|---|---|
| `periodId` | Kỳ đánh giá; đặt khoảng ngày mặc định theo kỳ. Không tồn tại: 404 |
| `from`, `to` | `YYYY-MM-DD`, tính theo ngày giờ Việt Nam (UTC+7), theo thời điểm nhân viên **gửi mã** (`submittedAt`). Ghi đè khoảng của kỳ. `from` sau `to`: 400 |
| `departmentId` | Phòng và các phòng con (phải nằm trong phạm vi) |
| `jobPositionId` | Vị trí công việc của nhân viên |
| `productCode` | Sản phẩm. Lấy theo file đã nhập; mã chưa có trong file lấy theo sản phẩm của loại công việc |
| `status` | `NOT_MATCHED`, `IN_REVIEW`, `MATCHED` (không phân biệt hoa thường). Khác: 400 |
| `reason` | Một mã lý do ở mục 5. Khác: 400 |
| `search` | Không phân biệt hoa thường, khớp một phần: mã giao dịch, mã/tên công việc, mã khách hàng (CIF), mã người giới thiệu, mã/tên nhân viên |
| `page`, `pageSize` | Mặc định 1 và 20, `pageSize` tối đa 100 (export bỏ phân trang) |

Sắp xếp cố định: `submittedAt` mới nhất trước, rồi `id`.

## 4. `ReconciliationListDto`

```json
{ "items": [ {
    "id": "guid", "transactionCode": "GD-20261021-0174",
    "workId": "guid", "workCode": "WK-1042", "workTitle": "Mở thẻ ghi nợ",
    "employeeId": "guid", "employeeCode": "NV0231", "employeeName": "Nguyễn Minh Anh", "jobTitle": "Teller",
    "departmentId": "guid", "departmentName": "Phòng Dịch vụ Khách hàng",
    "customerCif": "CIF0012345", "referEmployeeCode": null,
    "productCode": "TD_CLASSIC", "amount": 50000000, "transactionDate": "2026-10-21",
    "status": "NOT_MATCHED", "reason": "EMPLOYEE_MISMATCH",
    "submittedAt": "2026-10-21T09:02:00+00:00", "updatedAt": "2026-10-21T09:05:00+00:00" } ],
  "totalCount": 7, "page": 1, "pageSize": 20,
  "counts": { "all": 1284, "notMatched": 7, "inReview": 12, "matched": 1265 },
  "options": { "departments": [{ "id": "guid", "name": "…" }], "jobPositions": [{ "id": "guid", "title": "Teller" }],
               "products": [{ "code": "TD_CLASSIC", "name": "Tiền gửi" }], "reasons": ["EMPLOYEE_MISMATCH", "…"] } }
```

- `counts` theo **mọi bộ lọc trừ `status`**, để bốn thẻ trạng thái luôn cho thấy toàn cảnh khi đang chọn một thẻ.
- `productCode`, `amount`, `transactionDate`, `customerCif`, `referEmployeeCode` lấy từ **dòng file đã nhập có cùng mã**, kể cả mã `NOT_MATCHED` (không được liên kết với dòng đó). `customerCif` rơi về CIF khai trên công việc khi file không có. Mã chưa có trong file: `amount`, `transactionDate`, `referEmployeeCode` là `null`, `productCode` là sản phẩm của loại công việc.
- `options` giới hạn trong phạm vi của người gọi.

## 5. Lý do (`reason`)

`reason` là mã lý do của mã giao dịch, `null` với mã `MATCHED` không có ghi chú. Mã `IN_REVIEW` chưa có lý do riêng được báo là `NO_IMPORTED_TRANSACTION`.

| Mã | Ý nghĩa |
|---|---|
| `EMPLOYEE_MISMATCH` | Mã giao dịch trong file thuộc nhân viên khác |
| `PRODUCT_MISMATCH` | Sản phẩm trong file khác sản phẩm của loại công việc |
| `CUSTOMER_MISMATCH` | Công việc có CIF nhưng khác mã khách hàng trong file |
| `REFERRER_MISMATCH` | Mã người giới thiệu trong file khác người bàn giao |
| `TRANSACTION_BEFORE_HANDOVER` | Giao dịch diễn ra trước ngày bàn giao |
| `METRIC_TYPE_MISMATCH` | Chỉ số KPI của công việc không tính được từ giao dịch |
| `NO_IMPORTED_TRANSACTION` | Mã chưa có trong file nào đã nhập (đang chờ đối chiếu) |
| `KPI_TARGET_NOT_ASSIGNED` | Nhân viên chưa được giao chỉ tiêu KPI (`IN_REVIEW`, thử lại ở lần nhập sau) |
| `KPI_AGGREGATION_NOT_SUPPORTED` | KPI chưa hỗ trợ tính từ giao dịch |
| `PERIOD_LOCKED` | Kỳ đánh giá của ngày giao dịch đã khóa |
| `REFERRER_NOT_CREDITED` | Đã khớp, người giới thiệu chưa có chỉ tiêu nên không được cộng phần của mình |

Không có lệch số tiền: nhân viên không khai số tiền, hệ thống lấy số tiền từ file (BR-09).

## 6. `ReconciliationDetailDto`

`item` (như một dòng danh sách), `work` (`id, code, title, status, workTypeName, productCode, customerName, customerCif`), `submission` (bài nộp mới nhất của công việc: `id, submissionNo, status, submittedAt, submitterName`, hoặc `null`), `import` (dòng file đã nhập: `batchId, fileName, importedAt, importedByName, transactionDate, amount`, hoặc `null` khi chưa có), `fields`, `comparedFields`, `matchedFields`, `siblings` (các mã khác của cùng công việc: `id, transactionCode, status, reason`), `history`, `appeal`, `actions`.

### `fields[]`

`{ field, declared, imported, matched }` — `matched` là `null` khi không có gì để so sánh. Các `field`:

| `field` | declared / imported | `matched` |
|---|---|---|
| `TRANSACTION_CODE` | mã nhân viên khai / mã trong file | `true` khi đã có dòng file |
| `CUSTOMER` | CIF của công việc / mã khách hàng trong file | so sánh khi cả hai có giá trị, `null` nếu công việc không khai CIF |
| `EMPLOYEE` | mã nhân viên được giao / mã nhân viên trong file | theo id nhân viên |
| `PRODUCT` | sản phẩm của loại công việc / sản phẩm trong file | không phân biệt hoa thường |
| `REFERRAL` | người bàn giao (đã chấp nhận) / mã người giới thiệu trong file | chỉ so sánh khi có bàn giao; mã giới thiệu trùng chính người xử lý bị bỏ qua |
| `TRANSACTION_DATE` | ngày bàn giao / ngày giao dịch (chỉ có khi có bàn giao) | ngày giao dịch không trước ngày bàn giao |
| `AMOUNT` | luôn `null` / số tiền trong file | luôn `null`: số tiền do file quyết định, không so sánh |

`comparedFields` là số dòng có `matched` khác `null`, `matchedFields` là số dòng `true`.

### `history[]`

Mới nhất trước, từ dữ liệu thật: `{ kind, at, status, reason, actor, reference }`. `kind`: `CODE_ADDED` (nhân viên gửi mã), `IMPORTED` (file nhập, `reference` là tên file), `RECONCILED` (kết quả đối soát hiện tại), `QUALITY_FLAG` (`reference` = `QA-0001`), `RISK_EVENT` (`reference` = `RSK-0001`), `APPEAL` (`reference` = id khiếu nại).

### `appeal`

Khiếu nại chỉ có với bài nộp bị **từ chối** (`APPEAL_CONTRACT.md`), nên một mã giao dịch lệch không có khiếu nại riêng: nhân viên sửa lại mã. `state`: `NOT_APPLICABLE`, `AVAILABLE` (bài nộp mới nhất bị từ chối, còn trong `AppealRejectedHours`, kèm `deadline`), `EXPIRED`, `IN_PROGRESS` (`appealId`, `status`), `DECIDED` (`appealId`, `status`). `submissionId` là bài nộp được xét.

### `actions`

`canFlag`, `flagSubmissionId`, `flagBlockedReason`, `canRecordRisk`. QA chỉ gắn cờ được **bài nộp đã duyệt** (`QUALITY_ASSURANCE_CONTRACT.md`): `canFlag` là `true` khi người gọi có `quality_flag:create`, công việc có bài nộp `APPROVED`, không phải việc của chính họ và bài nộp chưa có cờ mở; `flagSubmissionId` là bài nộp đó. Khi `false`, `flagBlockedReason` là `NO_PERMISSION`, `NO_APPROVED_SUBMISSION`, `OWN_WORK` hoặc `OPEN_FLAG`. `canRecordRisk` cần `risk_event:create` và nhân viên khác người gọi. Gắn cờ gọi `POST /submissions/{id}/quality-flags`, ghi sự kiện rủi ro gọi `POST /risk-events`.

## 7. Export

`GET /reconciliations/export` trả `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, tên `doi-soat-yyyyMMdd-HHmm.xlsx`, một sheet "Đối soát": tiêu đề, dòng thời điểm xuất và nguồn dữ liệu, bảng 15 cột (mã việc, tên công việc, nhân viên, vị trí, phòng ban, mã giao dịch, CIF, mã người giới thiệu, sản phẩm, số tiền, ngày giao dịch, nộp lúc, trạng thái, lý do bằng tiếng Việt), có bộ lọc cột và cố định hàng tiêu đề.

## 8. Mã lỗi

| Tình huống | HTTP | `errorCode` |
|---|---|---|
| `status`, `reason`, khoảng ngày không hợp lệ | 400 | `VALIDATION_ERROR` |
| Chưa đăng nhập | 401 | `UNAUTHORIZED` |
| Không có quyền `reconciliation:read` | 403 | `FORBIDDEN` |
| `periodId` không tồn tại; mã giao dịch không có hoặc ngoài phạm vi | 404 | `NOT_FOUND` |
