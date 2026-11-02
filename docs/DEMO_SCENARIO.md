# Kịch bản test: KPI năm → chia quý/nhân viên → làm việc tích điểm → báo cáo điểm

Ký hiệu: **UI** = làm được trên giao diện. **API** = chỉ làm được qua API (Postman). **THIẾU** = code chưa có, cần note lại.
Mật khẩu tài khoản demo lấy từ biến `SeedData__DemoPassword`; tên đăng nhập là email của nhân viên.

| Vai trò | Nhân viên | Tài khoản |
| --- | --- | --- |
| Giám đốc chi nhánh | NV001 An | `an.nguyen@progest.local` |
| Quản lý phòng | NV002 Bình | `binh.tran@progest.local` |
| Nhân viên | NV004 Châu | `chau.le@progest.local` |
| Người thẩm định | NV014 | `verifier@bpms.com` |
| Nhân viên demo | NV012 | `employee@bpms.com` (quản lý trực tiếp: `manager@bpms.com`) |

Chuẩn bị: reset database, chạy lại DbMigrator (seed dùng `ON CONFLICT DO NOTHING`, DB cũ không nhận dữ liệu mới).

## Phần 1. Nhập Excel KPI cả năm, chia quý, chia nhân viên

| # | Bước | Cách làm | Kết quả mong đợi | Trạng thái |
| --- | --- | --- | --- | --- |
| 1.1 | Chuẩn bị file `.xlsx` (≤5 MB): cột A = tên KPI (khớp `kpi_definition.name`), cột B = giá trị cả năm, hàng 1 là tiêu đề | Tự tạo file | File hợp lệ | — |
| 1.2 | Nhập giá trị năm cho chi nhánh | **API** `POST /target-assignments/import?branchId=&year=` (multipart `file`) bằng NV001 | 201, các dòng chi nhánh–năm ở trạng thái `DRAFT` | **THIẾU UI** |
| 1.3 | Nhập file sai (thiếu KPI, số âm) | Như 1.2 | 400 `TARGET_IMPORT_INVALID`, mỗi dòng lỗi một thông báo, không ghi gì | **THIẾU UI** |
| 1.4 | Chia cho phòng | **API** `PUT /target-assignments/{branchYearId}/departments` | Tổng các phòng ≥ chỉ tiêu chi nhánh, nếu không 422 `TARGET_DEPARTMENT_SUM_BELOW` | **THIẾU UI** |
| 1.5 | Chia theo quý | **API** `PUT /target-assignments/{deptYearId}/quarters` | Tổng 4 quý ≥ chỉ tiêu năm của phòng, nếu không 422 `TARGET_QUARTER_SUM_BELOW` | **THIẾU UI** |
| 1.6 | Xác nhận | **API** `POST /target-assignments/{deptYearId}/confirm` | Các dòng thành `ACTIVE`, sinh dòng tháng và ngày. Tổng phải bằng đúng chỉ tiêu cha, nếu không 422 `TARGET_INVALID_STATE` | **THIẾU UI** |
| 1.7 | Xem cây chỉ tiêu | **API** `GET /target-assignments/tree?branchId=&year=&compareYear=` | Cây có target, đã chia, thực tế, so với năm trước | **THIẾU UI** |
| 1.8 | Chia xuống nhân viên | **API** `POST /target-assignments/{id}/allocations` (body có `employeeId`) bằng NV002 | Dòng target `ACTIVE` cho từng nhân viên | **THIẾU UI**; xem ghi chú bên dưới |

Ghi chú và chỗ thiếu của phần 1:
- **THIẾU UI**: không có màn hình nào cho nhập Excel, chia phòng, chia quý, chia nhân viên (`docs/api/TARGET_ASSIGNMENT_CONTRACT.md` ghi frontend "planned, not implemented"). Đây là điểm lớn nhất của kịch bản.
- **THIẾU liên kết**: luồng Excel (`TargetAssignmentController`) dừng ở phòng/quý/tháng/ngày. Chia xuống nhân viên đi qua API khác (`TargetAssignmentsController.allocations`). Tôi chưa chạy thử hai luồng nối tiếp nhau, nên chưa chắc dòng quý sau `confirm` dùng được làm cha cho chia nhân viên.
- **THIẾU dữ liệu**: seed chỉ có kỳ 2025 và 2026. Seed đã có target Q4/2026, nên nhập năm 2026 có thể gặp 409 `TARGET_INVALID_STATE` (hợp đồng API ghi rõ). Muốn thử nhập mới cần năm khác (cần tạo kỳ năm đó qua `POST /evaluation-periods`, cũng không có UI) hoặc DB không có target Q4.
- **THIẾU**: chưa có file Excel mẫu và API tải mẫu.
- Chia nhân viên chỉ cần kiểm tra bằng dữ liệu seed: NV004, NV005, NV006, NV007 đã có target Q4 (HDV 225) và tháng 10/11/12 (75/75/75).

## Phần 2. Nhân viên làm việc và tích điểm

| # | Bước | Cách làm | Kết quả mong đợi | Trạng thái |
| --- | --- | --- | --- | --- |
| 2.1 | Đăng nhập NV004, mở `/my-work` | **UI** | Thấy `W-2026-0001`, `0002`, `0003`… theo cột | OK |
| 2.2 | Kéo `W-2026-0002` (NEW) sang In progress | **UI** | Toast thành công, thẻ chuyển cột | OK |
| 2.3 | Kéo thẻ khác vào cột không hợp lệ | **UI** | Bị chặn, không gọi API | OK |
| 2.4 | Nộp kết quả cho work | **API** `POST /works/{workId}/submissions` (form: `comment`, `metricItems`, `files`) | 201, work sang `SUBMITTED` | **THIẾU UI** (nút "Tạo công việc" cũng đang `disabled`) |
| 2.5 | Người thẩm định lấy hồ sơ | **UI** `/review-verify/{submissionId}` bằng NV014 | Xem hồ sơ, bằng chứng | **THIẾU**: không có danh sách hồ sơ chờ duyệt, phải biết `submissionId` |
| 2.6 | Duyệt và nhập sản lượng công nhận | **UI**, quyết định APPROVED, nhập `verifiedValue` | Work sang `VERIFIED`, tiến độ cộng vào target của NV004 | OK |
| 2.7 | Duyệt khi `verifiedValue` trống | **UI** | FE chặn, bắt buộc nhập | OK |
| 2.8 | Duyệt chính hồ sơ của mình (maker–checker) | API | 422 `MakerCannotBeChecker` | OK |
| 2.9 | NV002 kéo work `VERIFIED` sang Done trên bảng team | **UI** | Work thành `DONE` | OK, cần có work `VERIFIED` (bước 2.6) |

Ghi chú và chỗ thiếu của phần 2:
- Tiến độ gắn vào target có kỳ ngắn nhất chứa hôm nay, tức **Tháng 11/2026**, không phải Q4. Khi xem điểm hãy chọn kỳ Tháng 11.
- Nhân viên chỉ có điểm nếu có target đang hoạt động cho đúng KPI của Work Type. `W-2026-0006` (NV009) không có target KH_MOI nên duyệt xong **không** cộng điểm và FE không báo.
- Bảng `Hoàn tất` (Done) chỉ nhận work đã `VERIFIED`.
- Mã KPI chính của Work Type lấy `FirstOrDefault()` không sắp xếp (`VerifiedResultRecorder.cs:55`), Work Type có nhiều KPI có thể lấy nhầm.
- Hồ sơ thẩm định hiển thị "sản lượng nộp" cố định 15.0 (`VerificationService.cs:65`), không lấy từ dữ liệu nộp.
- Chưa có màn hình hay API "điểm tích lũy" riêng cho nhân viên sau mỗi lần duyệt.

## Phần 3. Xem báo cáo điểm và nộp report

| # | Bước | Cách làm | Kết quả mong đợi | Trạng thái |
| --- | --- | --- | --- | --- |
| 3.1 | NV002 mở `/evaluations` | **UI** | Danh sách nhân viên phòng, mặc định mở Q4/2026 | OK |
| 3.2 | Mở form đánh giá của NV004 | **UI** `/evaluations/form?employeeId=&periodId=` | Thấy chỉ tiêu, thực tế, điểm định lượng, các nhóm định tính | OK |
| 3.3 | Chọn kỳ Tháng 11 | **UI** | Có target tháng (seed), actual lấy từ tiến độ tháng 11 | OK sau khi sửa seed |
| 3.4 | Nhập điểm định tính, nhận xét, **Lưu nháp** | **UI** | Trạng thái `DRAFT` | OK |
| 3.5 | **Hoàn tất đánh giá** (nộp báo cáo của nhân viên) | **UI** | Trạng thái `CALCULATED`, có `totalScore` | OK |
| 3.6 | Phòng nộp hiệu chuẩn lên chi nhánh | **API** `POST /evaluations/calibration/submit` | Đánh giá sang `IN_REVIEW` | **THIẾU UI** (chỉ có form và danh sách) |
| 3.7 | Chi nhánh phê duyệt, khóa kỳ | **API** `GET /evaluations/approvals`, `POST /evaluations/approvals/finalize` | Kỳ `LOCKED` | **THIẾU UI** |
| 3.8 | Xem bảng hiệu suất cá nhân, phòng, chi nhánh | **API** `GET /performance-dashboard/me`, `/departments/{id}`, `/branches/{id}` | Tổng điểm, nhóm KPI, so với kỳ trước | **THIẾU UI** (không có màn nào gọi) |
| 3.9 | Nhân viên tự xem điểm của mình | — | — | **THIẾU**: `employee-detail` không hiển thị điểm; chưa có màn "Điểm của tôi" |
| 3.10 | Xem điểm theo phòng | **UI** `/department-performance` | Điểm trung bình phòng | OK |

Ghi chú và chỗ thiếu của phần 3:
- **Điểm có hai đường tính**: form dùng `EvaluationService`; công thức chính thức `PerformanceEvaluationService.CalculateAsync` (ghi `PerformanceContributions`) chưa có chỗ nào ở FE gọi. Điểm tổng vẫn có, nhưng bảng chi tiết KPI trên dashboard (3.8) sẽ **trống** với nhân viên chấm bằng form.
- **Điểm 80 giả**: hiệu chuẩn nhân viên chưa có đánh giá sẽ lấy 80 (`EvaluationService.cs:1152`, `:1567`). Đừng dùng bước này làm bằng chứng cho điểm thật.
- Điểm mẫu trong seed (từ `seed_dev_data.sql:859`) là dữ liệu cố định; nhân viên đã có điểm mẫu có thể không phản ánh việc vừa duyệt.
- Báo cáo xuất file (PDF/Excel) cho HR: **THIẾU** hoàn toàn (`docs/BE_STATUS.md` mục 10).

## Tóm tắt những gì thiếu (để note lại)

1. Không có UI cho nhập Excel, chia phòng/quý/nhân viên, xem cây chỉ tiêu.
2. Không có UI cho tạo công việc và nộp kết quả công việc.
3. Không có danh sách hồ sơ chờ thẩm định.
4. Không có UI cho nộp hiệu chuẩn lên chi nhánh, phê duyệt và khóa kỳ, bảng hiệu suất cá nhân/phòng/chi nhánh.
5. Chưa chạy thử luồng Excel nối với chia nhân viên; chưa có file Excel mẫu; chưa có kỳ cho năm mới.
6. Điểm có hai đường tính và bảng chi tiết KPI chỉ có khi gọi `calculate`; số liệu giả 15.0 và 80.
7. Duyệt xong mà người nộp không có target thì không cộng điểm và không báo.
8. Xuất báo cáo PDF/Excel cho HR chưa có.
