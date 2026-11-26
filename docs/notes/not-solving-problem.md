# Not solving problem

Ghi chú tổng hợp ngày 2026-11-26. Chỉ ghi lại các khoảng trống giữa cách ngân hàng làm thực tế và hệ thống hiện tại. Chưa quyết định làm gì, chưa sửa code.

Nguồn thực tế: Gemini trả lời, **không dẫn nguồn cụ thể**, các con số chỉ là ví dụ minh họa, cần kiểm chứng trước khi đưa vào tài liệu. Nguồn hệ thống: đọc code và seed (`seed_dev_data.sql`, `PerformanceScoreFormula.cs`, `PerformanceScoreCalculator.cs`).

## 1. KPI trong hệ thống

- 7 KPI demo (ACTIVE): HDV, KH_MOI, DU_NO, THE_TD, CSAT, TY_LE_LOI, TUAN_THU.
- 4 KPI demo luồng duyệt: HDV_GT, THE_GT (chờ duyệt), DU_NO_GT (nháp), GN_CU (ngưng dùng).
- 33 mã `KPI_*` của KPI năm chi nhánh (import từ Excel).
- Quản lý: nhóm KPI, vòng đời DRAFT → PENDING_APPROVAL → ACTIVE/REJECTED, maker-checker, chỉ tiêu theo mục tiêu → phòng ban → nhân viên có phiên bản, hồ sơ đánh giá có trọng số.
- Đo: SUM / AVG / RATIO, % đạt = thực tế / chỉ tiêu × 100, chặn ở trần, KPI ngược chiều, hệ số theo loại công việc (thẻ 1.0/1.2/1.5), tổng điểm = trung bình có trọng số.

## 2. Chưa có / khác thực tế

| # | Thực tế ngân hàng | Hệ thống hiện tại |
|---|---|---|
| 1 | Huy động, CASA, dư nợ đo theo **số dư bình quân ngày**, tăng ròng = bình quân kỳ này trừ kỳ trước | Chỉ cộng tổng hoặc trung bình theo lượt |
| 2 | **Sàn**: đạt dưới 50-70% chỉ tiêu thì 0 điểm | Chỉ có trần |
| 3 | Chỉ tính kết quả **đạt chuẩn**: khách hàng có giao dịch, thẻ có chi tiêu trong 30-45 ngày, bảo hiểm qua 21 ngày cân nhắc | Chưa thấy |
| 4 | **Điều kiện chặn** (nợ xấu, tuân thủ) hạ bậc xếp loại | Có nạp sự kiện rủi ro, chưa thấy khống chế bậc |
| 5 | Khống chế tỷ lệ xếp loại (forced ranking), thu hồi thưởng (clawback) | Chưa thấy |
| 6 | Chia công giữa người giới thiệu và người chốt (30-50% / 50-70%) | Tính công cho một bên: người xử lý hoặc người giới thiệu |
| 7 | KPI ngược chiều: trừ tuyến tính theo mức vượt (vd mỗi 0.1% vượt trừ 10% điểm) | Trần trừ phần vượt, hoặc chỉ tiêu / thực tế × 100 |

## 3. Đổi chỉ tiêu giữa kỳ

Thực tế:
- Đổi tổng chi nhánh: Hội sở đề xuất, TGĐ hoặc PTGĐ khối duyệt. Chia lại giữa nhân viên: Giám đốc chi nhánh duyệt.
- Hạn chót khoảng ngày 15 của tháng cuối quý, sau đó hệ thống khóa.
- Điểm tính theo số ngày hiệu lực (phổ biến nhất), hoặc áp chỉ tiêu mới cho cả kỳ (chỉ khi sửa lỗi hay hạ chỉ tiêu).
- Ví dụ: chỉ tiêu 18 tỷ, sau 60/90 ngày nâng lên 27 tỷ; thực hiện 21 tỷ. Gộp chỉ tiêu: 100%. Chấm riêng từng đoạn rồi bình quân: 97,8%. Áp 27 tỷ cả kỳ: 77,8%.
- Chuyển phòng hoặc nghỉ việc: chốt số tại ngày bàn giao. Người nhận lấy số dư lúc nhận làm mốc. Hồ sơ đang dở chia 50/50 hoặc 70/30 (chuyển phòng), người nhận hưởng 100% (nghỉ việc). Người cũ vẫn chịu trách nhiệm nợ xấu 3-6 tháng.
- Chỉ tiêu chi nhánh đổi: chia phần chênh theo tỷ lệ chỉ tiêu gốc, hoặc theo năng lực (miễn tăng cho nhân viên mới). Nhiều nơi giao nhân viên cao hơn chi nhánh 5-10%.

Hệ thống:
- Đã khớp: tính điểm theo đoạn hiệu lực, chỉ tiêu mỗi phiên bản nhân tỷ lệ số ngày, kết quả ghép đoạn theo ngày phát sinh (`PerformanceScoreCalculator.cs:183`). Có lịch sử phiên bản và lý do đổi (`change_reason`, `target_change_log`). Có khóa kỳ (`PeriodLockGuard`) và luồng bàn giao.
- Cần chốt: dùng "gộp chỉ tiêu" hay "chấm riêng từng đoạn" (kết quả khác nhau). Chưa đọc bước gộp các đoạn thành một điểm KPI.
- Chưa thấy: hạn chót đổi chỉ tiêu trong kỳ, chia công hồ sơ đang dở khi bàn giao, mốc số dư của người nhận, trách nhiệm nợ xấu còn theo người cũ.

## 4. Lưu ý khi demo

- Bộ 7 KPI demo ghi đơn vị "tỷ VND", bộ `KPI_*` lưu bằng VND (có script `convert_ty_vnd_kpis_to_vnd.sql`).
- Cột `formula` chỉ là mô tả bằng chữ. Phép tính thật do `aggregation_type` và `direction`.

## 5. Màn chia target các cấp: so với workflow thực tế (2026-11-26)

Nguồn thực tế: Gemini, không dẫn nguồn, số liệu là ví dụ. Nguồn hệ thống: `docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md`, `docs/api/TARGET_ALLOCATION_CONTRACT.md` (mục Deferred), `TargetSetupService`, `MyTargetsService`, tìm trong code (không thấy từ khóa deadline, auto-confirm, buffer, probation, escalation).

Workflow thực tế: Hội sở giao tạm (T12-T1) → chi nhánh bảo vệ và chốt (T1-T2) → chia xuống phòng, nhân viên trong 5-10 ngày → nhân viên xác nhận trong 3-5 ngày làm việc (quá hạn coi như đồng ý) → phản hồi cấp 1 với trưởng phòng → khiếu nại cấp 2 lên giám đốc chi nhánh → sơ kết tháng 6-7 điều chỉnh.

Hệ thống đã có: chia 3 tầng (chi nhánh → quý → phòng → nhân viên), gợi ý theo lịch sử, xác nhận và phản hồi của nhân viên, điều chỉnh giữa kỳ có phiên bản, đổi chỉ tiêu cấp trên thì cấp dưới mở lại ở trạng thái "Lệch" (D13) và nhân viên phải xác nhận lại (RECONFIRM), chỉ giao KPI cho vị trí phụ trách (D7), KPI tỷ lệ/điểm không cộng dồn (chi nhánh dùng chung).

| # | Thực tế | Hệ thống | Mức |
|---|---|---|---|
| 1 | **Giao dư (buffer)**: tổng cấp dưới lớn hơn cấp trên, chi nhánh→phòng +5-15%, phòng→nhân viên +0-10%, có trần buffer | Tổng cấp dưới phải **bằng đúng** cấp trên (lỗi 422 `*_SUM_MISMATCH`, trigger DB). Gợi ý có "buffer unit" nhưng đó là đơn vị làm tròn, không phải giao dư | Lớn, ảnh hưởng thiết kế |
| 2 | **Hạn xác nhận** 3-5 ngày, quá hạn tự coi là đồng ý | Có PENDING/CONFIRMED/RECONFIRM, không thấy hạn chót hay tự đồng ý | Trung bình |
| 3 | **Khiếu nại 2 cấp**: trưởng phòng, rồi giám đốc chi nhánh (5-7 ngày), chốt trong 7-10 ngày | Phản hồi chỉ trả lời bởi trưởng phòng (`reply`), không thấy cấp 2 hay hạn xử lý | Trung bình |
| 4 | **Giao tạm** trước khi chốt (T12-T1), có bước đàm phán, bảo vệ kế hoạch | Có DRAFT/ACTIVE (publish), chưa có khái niệm giao tạm hay vòng đàm phán chi nhánh với Hội sở | Cần hỏi mentor |
| 5 | **Nhân viên mới**: thử việc 2 tháng miễn KPI, sau đó giảm 30-50% định mức, tính theo ngày | Mục tiêu năm chỉ có gợi ý "nhân viên mới" lấy theo năm trước; chưa thấy giảm hay tính theo ngày vào làm | Trung bình |
| 6 | **Nghỉ thai sản**: chỉ tiêu = 0 trong kỳ nghỉ, phần hụt chuyển cho người nhận hoặc lấy từ quỹ dự phòng | Có xét nhân viên đang làm (`EmployeeStatus.Active`); chưa thấy chia theo kỳ nghỉ. BE_STATUS ghi "approved leave does not change a target (BR-30)" | Cần hỏi mentor |
| 7 | **KPI số dư cuối kỳ**: quý = mức số dư cuối quý, năm = đầu năm + tăng ròng | `BALANCE` mới có tên enum, chưa xử lý (D2/D3/D4 hoãn), còn lỗi migration thiếu nhãn `BALANCE` | Đã biết, chưa làm |
| 8 | **KPI tỷ lệ phân hóa theo rủi ro** (NPL phòng KHDN 1,8%, KHCN 1,08%, bình quân gia quyền bằng chỉ tiêu chi nhánh) | Chi nhánh dùng chung một mức, RATIO không chia xuống phòng | Thấp |
| 9 | **KPI dùng chung** (lợi nhuận) gán vào phiếu điểm mọi người, trọng số khác nhau theo cấp (40/20/10%) | Chưa thấy loại này; trọng số theo hồ sơ đánh giá, chưa phân theo cấp quản lý | Cần hỏi mentor |
| 10 | **Năm đầu không lịch sử**: dùng benchmark, định mức chuẩn theo chức danh, chạy thí điểm | Đã chọn khác: nút gợi ý dùng tỷ lệ 1:2:3:4 và chia theo định biên khi không có lịch sử (hệ thống đã bỏ D5 "vô hiệu hóa") | Đã xử lý bằng cách khác |
| 11 | **Quy tắc sửa**: chỉ sửa phần còn lại của năm, không hồi tố quý đã chốt; chia phần chênh (delta) xuống cấp dưới, không chia lại toàn bộ | Có versioning và `effectiveDate`; chưa kiểm tra ràng buộc "không sửa quý đã qua" và chia theo delta khi điều chỉnh tầng 1 | Cần kiểm tra |
| 12 | **Xác nhận lại**: giảm chỉ tiêu (có lợi nhân viên) hoặc sửa lỗi kỹ thuật thì chỉ thông báo, không bắt xác nhận lại | Mọi điều chỉnh đều chuyển ack sang RECONFIRM (D10) | Thấp |
| 13 | **Căn cứ chia xuống nhân viên**: cấp bậc, danh mục khách hàng đang giữ, thâm niên | Gợi ý theo lịch sử và chia đều/theo định biên; chưa có hệ số cấp bậc hay danh mục khách hàng | Trung bình |
| 14 | **Chia quý theo mùa vụ** (15/25/25/35%) | Gợi ý theo hình dạng quý năm trước hoặc 1:2:3:4. Khớp ý tưởng, nhưng chưa có cấu hình hình dạng mùa vụ do quản trị đặt | Thấp |
| 15 | **Chia công khi bàn giao**, hồ sơ đang trình | Đã có ở mục 3 của file này | Đã ghi |

Hai điểm đáng chú ý nhất để nêu với mentor: **#1 giao dư** (mâu thuẫn trực tiếp với quy tắc tổng bằng đúng hiện tại) và **#2/#3 hạn xác nhận và khiếu nại hai cấp**.

## 6. Để trao đổi với mentor

Các mục ở phần 2 và 3 chưa được yêu cầu làm. Cần mentor hoặc nhóm quyết định mục nào nằm trong phạm vi đồ án trước khi thiết kế (theo quy tắc nhóm: cần mockup và đặc tả trước khi làm FE).
