-- ============================================================================
-- Progest - development seed data
-- Scope: one branch (Chi nhánh Hà Nội) with several departments / teams and
-- two job positions: SALES and TELLER.
--
-- Idempotent: fixed UUIDs + ON CONFLICT DO NOTHING. Safe to run repeatedly.
-- Executed by DatabaseSeeder inside a single transaction (deferred triggers
-- such as profile weight = 100 and SUM target splits are checked at commit).
-- UUID scheme: 5eed0000-0000-0000-<table>-<row>
-- ============================================================================

-- ---------------------------------------------------------------------------
-- Organization
--   Chi nhánh Hà Nội
--   ├─ Phòng Kinh doanh  -> Nhóm KD 1, Nhóm KD 2      (SALES)
--   ├─ Phòng Giao dịch   -> Quầy 1, Quầy 2            (TELLER)
--   ├─ Phòng KHCN, Phòng KHDN                         (SALES, no teams)
--   └─ Phòng Dịch vụ KH                               (TELLER, no teams)
-- ---------------------------------------------------------------------------
INSERT INTO department (id, code, name, description, parent_department_id, level_type) VALUES
 ('5eed0000-0000-0000-0001-000000000001', 'CN-HN',       'Chi nhánh Hà Nội',      'Chi nhánh trọng tâm của hệ thống', NULL,                                   'BRANCH'),
 ('5eed0000-0000-0000-0001-000000000002', 'CN-HN-KD',    'Phòng Kinh doanh',      'Khối bán hàng (Sales)',            '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-000000000003', 'CN-HN-GD',    'Phòng Giao dịch',       'Khối giao dịch viên (Teller)',     '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-000000000004', 'CN-HN-KD-N1', 'Nhóm Kinh doanh 1',     'Sales - khách hàng cá nhân',       '5eed0000-0000-0000-0001-000000000002', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000005', 'CN-HN-KD-N2', 'Nhóm Kinh doanh 2',     'Sales - hộ kinh doanh',            '5eed0000-0000-0000-0001-000000000002', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000006', 'CN-HN-GD-Q1', 'Nhóm Giao dịch Quầy 1', 'Teller - quầy tiền gửi',           '5eed0000-0000-0000-0001-000000000003', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000007', 'CN-HN-GD-Q2', 'Nhóm Giao dịch Quầy 2', 'Teller - quầy dịch vụ',            '5eed0000-0000-0000-0001-000000000003', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000008', 'CN-HN-KHCN',  'Phòng Khách hàng cá nhân',     'Khối bán hàng (Sales) - khách hàng cá nhân',  '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-000000000009', 'CN-HN-KHDN',  'Phòng Khách hàng doanh nghiệp', 'Khối bán hàng (Sales) - khách hàng doanh nghiệp', '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-00000000000a', 'CN-HN-DVKH',  'Phòng Dịch vụ khách hàng',    'Khối giao dịch viên (Teller) - dịch vụ khách hàng', '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT')
ON CONFLICT DO NOTHING;

-- Ranking slot quotas (absolute headcount per tier). A quota is a ceiling, not
-- a target: a tier may stay below its headcount, which blocks nothing.
INSERT INTO ranking_slot_quota (id, department_id, tier_code, headcount, created_at, is_deleted) VALUES
 (md5('evaluation-demo-slot-KD-A1')::uuid, '5eed0000-0000-0000-0001-000000000002', 'A1', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-A2')::uuid, '5eed0000-0000-0000-0001-000000000002', 'A2', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-A3')::uuid, '5eed0000-0000-0000-0001-000000000002', 'A3', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-B')::uuid,  '5eed0000-0000-0000-0001-000000000002', 'B',  1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-C')::uuid,  '5eed0000-0000-0000-0001-000000000002', 'C',  2, now(), FALSE),
 (md5('evaluation-demo-slot-KD-N1-A1')::uuid, '5eed0000-0000-0000-0001-000000000004', 'A1', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-N1-A2')::uuid, '5eed0000-0000-0000-0001-000000000004', 'A2', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-N1-A3')::uuid, '5eed0000-0000-0000-0001-000000000004', 'A3', 1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-N1-B')::uuid,  '5eed0000-0000-0000-0001-000000000004', 'B',  1, now(), FALSE),
 (md5('evaluation-demo-slot-KD-N1-C')::uuid,  '5eed0000-0000-0000-0001-000000000004', 'C',  1, now(), FALSE)
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- KPI catalog
-- ---------------------------------------------------------------------------
INSERT INTO kpi_group (id, code, name, description) VALUES
 ('5eed0000-0000-0000-0003-000000000001', 'KD',   'Kinh doanh',         'Chỉ tiêu tài chính, kinh doanh'),
 ('5eed0000-0000-0000-0003-000000000002', 'CLDV', 'Chất lượng dịch vụ', 'Trải nghiệm và hài lòng khách hàng'),
 ('5eed0000-0000-0000-0003-000000000003', 'TT',   'Tuân thủ',           'Tuân thủ quy trình, quy định')
ON CONFLICT DO NOTHING;

INSERT INTO kpi_definition (id, kpi_group_id, code, name, description, formula, unit, aggregation_type, direction, max_achievement_percent) VALUES
 ('5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0003-000000000001', 'HDV',       'Huy động vốn',               'Số dư tiền gửi huy động mới',    'SUM(số dư mới)',                 'tỷ VND',     'SUM',   'HIGHER_BETTER', 150),
 ('5eed0000-0000-0000-0005-000000000002', '5eed0000-0000-0000-0003-000000000001', 'KH_MOI',    'Khách hàng mới',             'Số khách hàng mở mới',           'COUNT(khách hàng mới)',          'khách hàng', 'SUM',   'HIGHER_BETTER', 150),
 ('5eed0000-0000-0000-0005-000000000003', '5eed0000-0000-0000-0003-000000000001', 'DU_NO',     'Giải ngân tín dụng',         'Dư nợ giải ngân mới',            'SUM(giải ngân)',                 'tỷ VND',     'SUM',   'HIGHER_BETTER', 150),
 ('5eed0000-0000-0000-0005-000000000004', '5eed0000-0000-0000-0003-000000000002', 'CSAT',      'Điểm hài lòng khách hàng',   'Điểm khảo sát CSAT (1-5)',       'AVG(điểm khảo sát)',             'điểm',       'AVG',   'HIGHER_BETTER', 100),
 ('5eed0000-0000-0000-0005-000000000005', '5eed0000-0000-0000-0003-000000000002', 'TY_LE_LOI', 'Tỷ lệ lỗi giao dịch',        'Giao dịch lỗi / tổng giao dịch', 'giao dịch lỗi / tổng giao dịch', '%',          'RATIO', 'LOWER_BETTER',  100),
 ('5eed0000-0000-0000-0005-000000000006', '5eed0000-0000-0000-0003-000000000003', 'TUAN_THU',  'Tỷ lệ hồ sơ đúng quy trình', 'Hồ sơ đạt / tổng hồ sơ',         'hồ sơ đạt / tổng hồ sơ',         '%',          'RATIO', 'HIGHER_BETTER', 100),
 -- BR-48 Task Multiplier: one KPI shared by every card tier; the tier's weight comes from
 -- work_type_kpi.multiplier below, not from a separate KPI per tier.
 ('5eed0000-0000-0000-0005-000000000007', '5eed0000-0000-0000-0003-000000000001', 'THE_TD',    'Thẻ tín dụng',               'Số thẻ tín dụng mở mới (quy đổi theo hạng thẻ)', 'SUM(thẻ mở mới x hệ số hạng thẻ)', 'thẻ', 'SUM', 'HIGHER_BETTER', 150)
ON CONFLICT DO NOTHING;

-- Year Target Import (docs/design/BRANCH_YEAR_KPI_IMPORT_DESIGN.md, D2/D3): the 28 KPI_* codes used by
-- docs/excels/KPI_Chi_nhanh_2025_chuan_hoa.xlsx, grouped as in its "Bảng đối chiếu mã" sheet.
-- Monetary KPIs are stored in VND (the file's "tỷ VND" column is converted x10^9 on import, G9).
-- Tổng hợp COUNT -> SUM and LAST -> NO_AGGREGATION (D3; KpiAggregationType has no COUNT/LAST).
INSERT INTO kpi_group (id, code, name, description) VALUES
 ('5eed0000-0000-0000-0003-00000000000a', 'SALES',            'Kinh doanh & huy động',  'Tài chính, huy động, khách hàng, sản phẩm (nhóm A-D của file KPI năm)'),
 ('5eed0000-0000-0000-0003-00000000000b', 'SERVICE_QUALITY',  'Chất lượng dịch vụ',     'Hài lòng khách hàng, thời gian phục vụ, khiếu nại (nhóm E)'),
 ('5eed0000-0000-0000-0003-00000000000c', 'OPERATIONS',       'Vận hành & tuân thủ',    'Giao dịch, bàn giao, rủi ro, đào tạo (nhóm F)')
ON CONFLICT DO NOTHING;

INSERT INTO kpi_definition (id, kpi_group_id, code, name, description, unit, aggregation_type, direction) VALUES
 ('5eed0000-0000-0000-0005-000000000101', '5eed0000-0000-0000-0003-00000000000a', 'KPI_NFI',           'Thu nhập thuần từ dịch vụ (NFI)',        'Tổng phí dịch vụ thu được trong kỳ (từ file giao dịch)', 'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000102', '5eed0000-0000-0000-0003-00000000000a', 'KPI_FEE_CARD',      'Thu phí dịch vụ thẻ',                    'Phí phát hành, thường niên, giao dịch thẻ',              'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000103', '5eed0000-0000-0000-0003-00000000000a', 'KPI_FEE_EBANK',     'Thu phí eBanking & chuyển tiền',         'Phí chuyển tiền, SMS, gói tài khoản số',                 'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000104', '5eed0000-0000-0000-0003-00000000000a', 'KPI_INSURANCE',     'Thu phí bảo hiểm (bancassurance)',       'Hoa hồng hợp đồng bảo hiểm đã xác minh',                 'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000105', '5eed0000-0000-0000-0003-00000000000a', 'KPI_DEP_NET',       'Huy động tăng ròng',                     'Số dư huy động cuối kỳ - đầu kỳ',                        'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000106', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CASA',          'CASA tăng ròng',                         'Số dư tiền gửi không kỳ hạn cuối kỳ - đầu kỳ',           'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000107', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CASA_RATIO',    'Tỷ lệ CASA / tổng huy động',             'Số dư CASA / tổng huy động, cuối kỳ',                    '%',      'AVG',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000108', '5eed0000-0000-0000-0003-00000000000a', 'KPI_TERM_DEP',      'Tiền gửi có kỳ hạn mở mới',              'Tổng giá trị sổ tiết kiệm/HĐTG mở mới',                  'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000109', '5eed0000-0000-0000-0003-00000000000a', 'KPI_ACC_OPEN',      'Số tài khoản thanh toán mở mới',         'Tài khoản mở mới có giao dịch phát sinh trong 30 ngày',  'TK',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010a', '5eed0000-0000-0000-0003-00000000000a', 'KPI_NEW_CUST',      'Khách hàng mới',                         'KH cá nhân mới có mã CIF mở trong kỳ',                   'KH',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010b', '5eed0000-0000-0000-0003-00000000000a', 'KPI_REFERRAL',      'Khách hàng mới qua giới thiệu/bàn giao',  'Bàn giao Teller -> Sales thành công (BR-07)',           'KH',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010c', '5eed0000-0000-0000-0003-00000000000a', 'KPI_ACTIVE_CUST',   'Khách hàng hoạt động (active)',          'KH có >= 1 giao dịch trong 90 ngày, cuối kỳ',           'KH',     'NO_AGGREGATION', 'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010d', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CROSS_SELL',    'Số sản phẩm bình quân / KH',             'Tổng sản phẩm đang dùng / số KH active',                 'SP/KH',  'AVG',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010e', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CHURN',         'Tỷ lệ KH rời bỏ',                        'KH đóng toàn bộ tài khoản / KH đầu kỳ',                  '%',      'AVG',            'LOWER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000010f', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CARD_DEBIT',    'Thẻ ghi nợ phát hành',                   'Thẻ debit đã kích hoạt',                                 'thẻ',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000110', '5eed0000-0000-0000-0003-00000000000a', 'KPI_CARD_CREDIT',   'Thẻ tín dụng phát hành',                 'Thẻ credit đã kích hoạt (hệ số BR-33)',                  'thẻ',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000111', '5eed0000-0000-0000-0003-00000000000a', 'KPI_EBANK_ACTIVE',  'Kích hoạt eBanking',                     'KH đăng nhập eBanking lần đầu',                          'KH',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000112', '5eed0000-0000-0000-0003-00000000000a', 'KPI_INSURANCE_CNT', 'Hợp đồng bảo hiểm bán mới',              'HĐ bảo hiểm đã phát hành',                               'HĐ',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000113', '5eed0000-0000-0000-0003-00000000000a', 'KPI_AUTO_SAVING',   'Đăng ký dịch vụ sinh lời tự động',       'KH đăng ký tính năng sinh lời tự động',                  'KH',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000114', '5eed0000-0000-0000-0003-00000000000b', 'KPI_CSAT',          'Điểm hài lòng bình quân',                 'Bình quân điểm khảo sát sau giao dịch',                 'điểm/5', 'AVG',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000115', '5eed0000-0000-0000-0003-00000000000b', 'KPI_1STAR_RATE',    'Tỷ lệ đánh giá 1 sao',                   'Số đánh giá 1 sao / tổng đánh giá (BR-17)',              '%',      'AVG',            'LOWER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000116', '5eed0000-0000-0000-0003-00000000000b', 'KPI_SERVE_TIME',    'Thời gian phục vụ bình quân',            'Thời gian từ gọi số đến hoàn tất giao dịch',             'phút',   'AVG',            'LOWER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000117', '5eed0000-0000-0000-0003-00000000000b', 'KPI_COMPLAINT_SLA', 'Khiếu nại xử lý đúng hạn',               'Khiếu nại đóng trong SLA / tổng khiếu nại',              '%',      'AVG',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000118', '5eed0000-0000-0000-0003-00000000000c', 'KPI_TXN_VOL',       'Số giao dịch tại quầy',                  'Giao dịch tất toán thành công tại quầy',                 'GD',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000119', '5eed0000-0000-0000-0003-00000000000c', 'KPI_TXN_ERR_RATE',  'Tỷ lệ lỗi giao dịch',                    'GD phải điều chỉnh / tổng GD',                           '%',      'AVG',            'LOWER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000011a', '5eed0000-0000-0000-0003-00000000000c', 'KPI_HANDOVER_SLA',  'Bàn giao được chấp nhận đúng SLA',        'Bàn giao nhận trong 4h / tổng bàn giao (BR-21)',        '%',      'AVG',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000011b', '5eed0000-0000-0000-0003-00000000000c', 'KPI_RISK_SEVERE',   'Sự kiện rủi ro nghiêm trọng',             'RiskEvent mức nghiêm trọng do QA ghi nhận',              'vụ',     'SUM',            'LOWER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000011c', '5eed0000-0000-0000-0003-00000000000c', 'KPI_TRAINING',      'Hoàn thành đào tạo bắt buộc',             'Nhân viên hoàn thành khoá AML/tuân thủ (BR-44)',         '%',      'AVG',            'HIGHER_BETTER'),
 -- 2026 additions: three Sales KPIs and two Teller KPIs, each with 2025/2026 history and a split to every eligible employee.
 ('5eed0000-0000-0000-0005-00000000011d', '5eed0000-0000-0000-0003-00000000000a', 'KPI_LOAN_DISB',     'Giải ngân tín dụng cá nhân',             'Dư nợ giải ngân mới cho khách hàng cá nhân',             'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000011e', '5eed0000-0000-0000-0003-00000000000a', 'KPI_FX_FEE',        'Thu phí kinh doanh ngoại hối',           'Phí mua bán và chuyển đổi ngoại tệ',                     'VND',    'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-00000000011f', '5eed0000-0000-0000-0003-00000000000a', 'KPI_PAYROLL_ACC',    'Tài khoản chi lương mở mới',             'Tài khoản nhận lương của doanh nghiệp mới ký',           'TK',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000120', '5eed0000-0000-0000-0003-00000000000c', 'KPI_DOC_PROCESSED', 'Hồ sơ xử lý tại quầy',                  'Hồ sơ giao dịch đã xử lý và hoàn tất',                   'HS',     'SUM',            'HIGHER_BETTER'),
 ('5eed0000-0000-0000-0005-000000000121', '5eed0000-0000-0000-0003-00000000000c', 'KPI_CASH_TXN',      'Giao dịch tiền mặt',                     'Giao dịch nộp/rút tiền mặt thành công',                  'GD',     'SUM',            'HIGHER_BETTER')
ON CONFLICT DO NOTHING;

-- Only two job positions; managers hold one of them and get the DEPT_MANAGER role.
INSERT INTO job_position (id, code, title, description) VALUES
 ('5eed0000-0000-0000-0006-000000000001', 'SALES',  'Chuyên viên Kinh doanh', 'Bán hàng, phát triển khách hàng'),
 ('5eed0000-0000-0000-0006-000000000002', 'TELLER', 'Giao dịch viên',         'Giao dịch tại quầy')
ON CONFLICT DO NOTHING;

-- Data Import (02_Nhan_su_To_chuc.xlsx, docs/api/DATA_IMPORT_CONTRACT.md): the other 5 positions the template offers.
INSERT INTO job_position (id, code, title, description) VALUES
 ('5eed0000-0000-0000-0006-000000000003', 'DEPT_MANAGER',   'Trưởng phòng',          'Quản lý phòng ban'),
 ('5eed0000-0000-0000-0006-000000000004', 'BRANCH_MANAGER', 'Giám đốc chi nhánh',    'Quản lý chi nhánh'),
 ('5eed0000-0000-0000-0006-000000000005', 'QA',              'Kiểm soát chất lượng', 'Kiểm soát chất lượng dịch vụ'),
 ('5eed0000-0000-0000-0006-000000000006', 'HR_OFFICER',      'Chuyên viên nhân sự',  'Quản lý nhân sự'),
 ('5eed0000-0000-0000-0006-000000000007', 'SYS_ADMIN',       'Quản trị hệ thống',    'Quản trị hệ thống')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Evaluation profiles, one per position (ACTIVE profiles must total 100% - checked at commit)
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_profile (id, job_position_id, code, name, description, version, effective_from, status) VALUES
 ('5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0006-000000000001', 'EP-SALES',  'Hồ sơ đánh giá Sales',          'Chuyên viên kinh doanh',  1, '2026-01-01', 'ACTIVE'),
 ('5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0006-000000000002', 'EP-TELLER', 'Hồ sơ đánh giá Giao dịch viên', 'Giao dịch viên tại quầy', 1, '2026-01-01', 'ACTIVE')
ON CONFLICT DO NOTHING;

-- Weight tree: group nodes (name, no KPI) at the root, KPI leaves below; siblings total 100.
-- SALES: KD 70 / CLDV 20 / TT 10 - TELLER: KD 20 / CLDV 50 / TT 30
-- Leaves: KD = HDV 40, KH_MOI 30, DU_NO 30; CLDV = CSAT 50, TY_LE_LOI 50; TT = TUAN_THU 100
INSERT INTO evaluation_profile_item (id, evaluation_profile_id, parent_item_id, kpi_definition_id, name, weight_percent, display_order) VALUES
 ('5eed0000-0000-0000-0004-000000000001', '5eed0000-0000-0000-0002-000000000001', NULL, NULL, 'Kinh doanh',            70, 1),
 ('5eed0000-0000-0000-0004-000000000002', '5eed0000-0000-0000-0002-000000000001', NULL, NULL, 'Chất lượng dịch vụ',    20, 2),
 ('5eed0000-0000-0000-0004-000000000003', '5eed0000-0000-0000-0002-000000000001', NULL, NULL, 'Tuân thủ',              10, 3),
 ('5eed0000-0000-0000-0004-000000000004', '5eed0000-0000-0000-0002-000000000002', NULL, NULL, 'Kinh doanh',            20, 1),
 ('5eed0000-0000-0000-0004-000000000005', '5eed0000-0000-0000-0002-000000000002', NULL, NULL, 'Chất lượng dịch vụ',    50, 2),
 ('5eed0000-0000-0000-0004-000000000006', '5eed0000-0000-0000-0002-000000000002', NULL, NULL, 'Tuân thủ',              30, 3),
 ('5eed0000-0000-0000-0004-000000000101', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000001', '5eed0000-0000-0000-0005-000000000001', NULL, 40, 1),
 ('5eed0000-0000-0000-0004-000000000102', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000001', '5eed0000-0000-0000-0005-000000000002', NULL, 30, 2),
 ('5eed0000-0000-0000-0004-000000000103', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000001', '5eed0000-0000-0000-0005-000000000003', NULL, 30, 3),
 ('5eed0000-0000-0000-0004-000000000104', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000002', '5eed0000-0000-0000-0005-000000000004', NULL, 50, 1),
 ('5eed0000-0000-0000-0004-000000000105', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000002', '5eed0000-0000-0000-0005-000000000005', NULL, 50, 2),
 ('5eed0000-0000-0000-0004-000000000106', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0004-000000000003', '5eed0000-0000-0000-0005-000000000006', NULL, 100, 1),
 ('5eed0000-0000-0000-0004-000000000201', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000004', '5eed0000-0000-0000-0005-000000000001', NULL, 40, 1),
 ('5eed0000-0000-0000-0004-000000000202', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000004', '5eed0000-0000-0000-0005-000000000002', NULL, 30, 2),
 ('5eed0000-0000-0000-0004-000000000203', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000004', '5eed0000-0000-0000-0005-000000000003', NULL, 30, 3),
 ('5eed0000-0000-0000-0004-000000000204', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000005', '5eed0000-0000-0000-0005-000000000004', NULL, 50, 1),
 ('5eed0000-0000-0000-0004-000000000205', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000005', '5eed0000-0000-0000-0005-000000000005', NULL, 50, 2),
 ('5eed0000-0000-0000-0004-000000000206', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0004-000000000006', '5eed0000-0000-0000-0005-000000000006', NULL, 100, 1)
ON CONFLICT DO NOTHING;

-- RestructureEvaluationProfileItemTree resets ACTIVE profiles to DRAFT; reactivate the seeded ones once reseeded.
UPDATE evaluation_profile SET status = 'ACTIVE'
WHERE id IN ('5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0002-000000000002')
  AND status = 'DRAFT';

-- Year Target Import (BRANCH_YEAR_KPI_IMPORT_DESIGN.md, D11): a position's applicable KPIs come from its
-- ACTIVE EvaluationProfile, used in phase 2 to find each department's headcount per KPI. These are separate,
-- flat profiles (not the performance-evaluation profiles above) so they don't disturb their weight-100 tree;
-- effective_from is offset by a day because (job_position_id, effective_from) is unique.
-- They apply from 2025: 2026 evaluations keep using EP-SALES / EP-TELLER, whose KPIs the
-- 2026 targets and progress are seeded for.
-- Sales (file groups A-D) -> SALES; Service/Operations (file groups E-F) -> TELLER.
INSERT INTO evaluation_profile (id, job_position_id, code, name, description, version, effective_from, status) VALUES
 ('5eed0000-0000-0000-0002-000000000003', '5eed0000-0000-0000-0006-000000000001', 'EP-SALES-YEARKPI',  'Hồ sơ KPI năm - Kinh doanh', 'Liên kết KPI_* nhóm A-D cho Phân bổ chỉ tiêu năm (phase 2).',             1, '2025-01-01', 'ACTIVE'),
 ('5eed0000-0000-0000-0002-000000000004', '5eed0000-0000-0000-0006-000000000002', 'EP-TELLER-YEARKPI', 'Hồ sơ KPI năm - Dịch vụ & Vận hành', 'Liên kết KPI_* nhóm E-F cho Phân bổ chỉ tiêu năm (phase 2).', 1, '2025-01-01', 'ACTIVE')
ON CONFLICT (id) DO UPDATE SET effective_from = EXCLUDED.effective_from, status = 'ACTIVE';
-- Databases seeded earlier carry the profiles with the old date.
UPDATE evaluation_profile SET effective_from = '2025-01-01'
WHERE id IN ('5eed0000-0000-0000-0002-000000000003', '5eed0000-0000-0000-0002-000000000004');


INSERT INTO evaluation_profile_item (id, evaluation_profile_id, parent_item_id, kpi_definition_id, name, weight_percent, display_order) VALUES
 ('5eed0000-0000-0000-0004-000000000301', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000101', NULL, 4.5455, 1),
 ('5eed0000-0000-0000-0004-000000000302', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000102', NULL, 4.5455, 2),
 ('5eed0000-0000-0000-0004-000000000303', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000103', NULL, 4.5455, 3),
 ('5eed0000-0000-0000-0004-000000000304', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000104', NULL, 4.5455, 4),
 ('5eed0000-0000-0000-0004-000000000305', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000105', NULL, 4.5455, 5),
 ('5eed0000-0000-0000-0004-000000000306', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000106', NULL, 4.5455, 6),
 ('5eed0000-0000-0000-0004-000000000307', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000107', NULL, 4.5455, 7),
 ('5eed0000-0000-0000-0004-000000000308', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000108', NULL, 4.5455, 8),
 ('5eed0000-0000-0000-0004-000000000309', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000109', NULL, 4.5455, 9),
 ('5eed0000-0000-0000-0004-00000000030a', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010a', NULL, 4.5455, 10),
 ('5eed0000-0000-0000-0004-00000000030b', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010b', NULL, 4.5455, 11),
 ('5eed0000-0000-0000-0004-00000000030c', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010c', NULL, 4.5455, 12),
 ('5eed0000-0000-0000-0004-00000000030d', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010d', NULL, 4.5455, 13),
 ('5eed0000-0000-0000-0004-00000000030e', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010e', NULL, 4.5455, 14),
 ('5eed0000-0000-0000-0004-00000000030f', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000010f', NULL, 4.5455, 15),
 ('5eed0000-0000-0000-0004-000000000310', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000110', NULL, 4.5455, 16),
 ('5eed0000-0000-0000-0004-000000000311', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000111', NULL, 4.5455, 17),
 ('5eed0000-0000-0000-0004-000000000312', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000112', NULL, 4.5455, 18),
 ('5eed0000-0000-0000-0004-000000000313', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-000000000113', NULL, 4.5455, 19),
 ('5eed0000-0000-0000-0004-000000000314', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000011d', NULL, 4.5455, 20),
 ('5eed0000-0000-0000-0004-000000000315', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000011e', NULL, 4.5455, 21),
 ('5eed0000-0000-0000-0004-000000000316', '5eed0000-0000-0000-0002-000000000003', NULL, '5eed0000-0000-0000-0005-00000000011f', NULL, 4.5445, 22),
 ('5eed0000-0000-0000-0004-000000000401', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000114', NULL, 9.0909, 1),
 ('5eed0000-0000-0000-0004-000000000402', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000115', NULL, 9.0909, 2),
 ('5eed0000-0000-0000-0004-000000000403', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000116', NULL, 9.0909, 3),
 ('5eed0000-0000-0000-0004-000000000404', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000117', NULL, 9.0909, 4),
 ('5eed0000-0000-0000-0004-000000000405', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000118', NULL, 9.0909, 5),
 ('5eed0000-0000-0000-0004-000000000406', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000119', NULL, 9.0909, 6),
 ('5eed0000-0000-0000-0004-000000000407', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-00000000011a', NULL, 9.0909, 7),
 ('5eed0000-0000-0000-0004-000000000408', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-00000000011b', NULL, 9.0909, 8),
 ('5eed0000-0000-0000-0004-000000000409', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-00000000011c', NULL, 9.0909, 9),
 ('5eed0000-0000-0000-0004-00000000040a', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000120', NULL, 9.0909, 10),
 ('5eed0000-0000-0000-0004-00000000040b', '5eed0000-0000-0000-0002-000000000004', NULL, '5eed0000-0000-0000-0005-000000000121', NULL, 9.0910, 11)
ON CONFLICT DO NOTHING;
-- Databases seeded earlier carry the 19/9-item weights: rebalance so each flat profile still totals 100.
UPDATE evaluation_profile_item SET weight_percent = 4.5455 WHERE evaluation_profile_id = '5eed0000-0000-0000-0002-000000000003' AND id <> '5eed0000-0000-0000-0004-000000000316';
UPDATE evaluation_profile_item SET weight_percent = 9.0909 WHERE evaluation_profile_id = '5eed0000-0000-0000-0002-000000000004' AND id <> '5eed0000-0000-0000-0004-00000000040b';


-- NV001 giám đốc CN, NV002 trưởng nhóm KD 1, NV003 trưởng phòng GD, NV011 trưởng phòng KD; managers first.
INSERT INTO employee (id, employee_code, full_name, email, phone, department_id, job_position_id, manager_id) VALUES
 ('5eed0000-0000-0000-0007-000000000001', 'NV001', 'Nguyễn Văn An',  'an.nguyen@progest.local', '0901000001', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-0006-000000000001', NULL),
 ('5eed0000-0000-0000-0007-000000000002', 'NV002', 'Trần Thị Bình',  'binh.tran@progest.local', '0901000002', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000003', 'NV003', 'Đỗ Thị Lan',     'lan.do@progest.local',    '0901000003', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000004', 'NV004', 'Lê Minh Châu',   'chau.le@progest.local',   '0901000004', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000005', 'NV005', 'Phạm Quốc Dũng', 'dung.pham@progest.local', '0901000005', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000006', 'NV006', 'Vũ Đức Khang',   'khang.vu@progest.local',  '0901000006', '5eed0000-0000-0000-0001-000000000005', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-00000000000b'),
 ('5eed0000-0000-0000-0007-000000000007', 'NV007', 'Ngô Thanh Mai',  'mai.ngo@progest.local',   '0901000007', '5eed0000-0000-0000-0001-000000000005', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-00000000000b'),
 ('5eed0000-0000-0000-0007-000000000008', 'NV008', 'Hoàng Thu Hà',   'ha.hoang@progest.local',  '0901000008', '5eed0000-0000-0000-0001-000000000006', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-000000000009', 'NV009', 'Bùi Văn Nam',    'nam.bui@progest.local',   '0901000009', '5eed0000-0000-0000-0001-000000000006', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-00000000000a', 'NV010', 'Đặng Ngọc Oanh', 'oanh.dang@progest.local', '0901000010', '5eed0000-0000-0000-0001-000000000007', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-00000000000b', 'NV011', 'Demo Manager',    'manager@bpms.com',        '0901000011', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-00000000000c', 'NV012', 'Demo Employee',   'employee@bpms.com',       '0901000012', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-00000000000b'),
 ('5eed0000-0000-0000-0007-00000000000d', 'NV013', 'Demo System Admin', 'sysadmin@bpms.com',     '0901000013', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-0006-000000000001', NULL),
 ('5eed0000-0000-0000-0007-00000000000e', 'NV014', 'Demo Verifier',     'verifier@bpms.com',     '0901000014', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 -- NV022: demo QA account (role QA; default position like NV013/NV014). Employee import still maps position QA to VERIFIER; role QA is assigned by System Admin.
 ('5eed0000-0000-0000-0007-000000000016', 'NV022', 'Demo QA',           'qa@bpms.com',           '0901000022', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001')
ON CONFLICT DO NOTHING;

-- NV015-NV021: staff of the extra departments (KHCN, KHDN: SALES; DVKH: TELLER), so each has headcount in the
-- Allocation grid. The first of each department is its head.
INSERT INTO employee (id, employee_code, full_name, email, phone, department_id, job_position_id, manager_id) VALUES
 ('5eed0000-0000-0000-0007-00000000000f', 'NV015', 'Trịnh Hoài Nam',   'nam.trinh@progest.local',   '0901000015', '5eed0000-0000-0000-0001-000000000008', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000010', 'NV016', 'Lương Thị Hạnh',   'hanh.luong@progest.local',  '0901000016', '5eed0000-0000-0000-0001-000000000008', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-00000000000f'),
 ('5eed0000-0000-0000-0007-000000000011', 'NV017', 'Cao Văn Long',     'long.cao@progest.local',    '0901000017', '5eed0000-0000-0000-0001-000000000008', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-00000000000f'),
 ('5eed0000-0000-0000-0007-000000000012', 'NV018', 'Mai Quang Huy',    'huy.mai@progest.local',     '0901000018', '5eed0000-0000-0000-0001-000000000009', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000013', 'NV019', 'Đinh Thị Phương',  'phuong.dinh@progest.local', '0901000019', '5eed0000-0000-0000-0001-000000000009', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000012'),
 ('5eed0000-0000-0000-0007-000000000014', 'NV020', 'Tạ Minh Tuấn',     'tuan.ta@progest.local',     '0901000020', '5eed0000-0000-0000-0001-00000000000a', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000015', 'NV021', 'Phan Thùy Linh',   'linh.phan@progest.local',   '0901000021', '5eed0000-0000-0000-0001-00000000000a', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000014')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Access control: six roles only (EMPLOYEE, DEPT_MANAGER, BRANCH_MANAGER, HR_OFFICER, VERIFIER, SYSTEM_ADMIN)
-- ---------------------------------------------------------------------------
INSERT INTO role (id, code, name, description) VALUES
 ('5eed0000-0000-0000-0008-000000000003', 'VERIFIER', 'Người duyệt',   'Thẩm định kết quả công việc'),
 ('5eed0000-0000-0000-0008-000000000004', 'EMPLOYEE', 'Nhân viên',     'Thực hiện và nộp kết quả công việc'),
 ('5eed0000-0000-0000-0008-000000000005', 'SYSTEM_ADMIN',   'System Administrator', 'Quản trị hệ thống'),
 ('5eed0000-0000-0000-0008-000000000006', 'BRANCH_MANAGER', 'Giám đốc chi nhánh',   'Quản lý chi nhánh và các phòng ban trực thuộc'),
 ('5eed0000-0000-0000-0008-000000000007', 'DEPT_MANAGER',   'Trưởng phòng',         'Quản lý phòng ban của mình'),
 ('5eed0000-0000-0000-0008-000000000009', 'HR_OFFICER',     'Chuyên viên nhân sự',  'Quản lý hồ sơ nhân sự và tổ chức')
ON CONFLICT DO NOTHING;

INSERT INTO permission (id, code, name, resource, action) VALUES
 ('5eed0000-0000-0000-0009-000000000001', 'WORK_READ',           'Xem công việc',        'work',         'read'),
 ('5eed0000-0000-0000-0009-000000000002', 'WORK_CREATE',         'Tạo công việc',        'work',         'create'),
 ('5eed0000-0000-0000-0009-000000000003', 'WORK_UPDATE',         'Cập nhật công việc',   'work',         'update'),
 ('5eed0000-0000-0000-0009-000000000004', 'SUBMISSION_READ',     'Xem kết quả nộp',      'submission',   'read'),
 ('5eed0000-0000-0000-0009-000000000005', 'SUBMISSION_CREATE',   'Nộp kết quả',          'submission',   'create'),
 ('5eed0000-0000-0000-0009-000000000006', 'VERIFICATION_DECIDE', 'Duyệt kết quả',        'verification', 'decide'),
 ('5eed0000-0000-0000-0009-000000000007', 'KPI_READ',            'Xem KPI',              'kpi',          'read'),
 ('5eed0000-0000-0000-0009-000000000008', 'KPI_MANAGE',          'Quản lý KPI',          'kpi',          'manage'),
 ('5eed0000-0000-0000-0009-000000000009', 'TARGET_MANAGE',       'Giao chỉ tiêu',        'target',       'manage'),
 ('5eed0000-0000-0000-0009-00000000000a', 'EVALUATION_READ',     'Xem kết quả đánh giá', 'evaluation',   'read'),
 ('5eed0000-0000-0000-0009-00000000000b', 'EMPLOYEE_READ',       'Xem nhân viên',        'employee',     'read'),
 ('5eed0000-0000-0000-0009-00000000000c', 'EVALUATION_SUBMIT',   'Nộp đánh giá',         'evaluation',   'submit'),
 ('5eed0000-0000-0000-0009-00000000000d', 'EVALUATION_CALIBRATE','Hiệu chuẩn đánh giá',  'evaluation',   'calibrate'),
 ('5eed0000-0000-0000-0009-00000000000e', 'EVALUATION_FINALIZE', 'Phê duyệt và khóa kỳ đánh giá', 'evaluation', 'finalize'),
 ('5eed0000-0000-0000-0009-00000000002f', 'EVALUATION_CALCULATE', 'Tính điểm KPI', 'evaluation', 'calculate'),
 ('5eed0000-0000-0000-0009-00000000000f', 'CAMPAIGN_READ',       'Xem chiến dịch',       'campaign',     'read'),
 ('5eed0000-0000-0000-0009-000000000010', 'CAMPAIGN_MANAGE',     'Quản lý chiến dịch',   'campaign',     'manage'),
 ('5eed0000-0000-0000-0009-000000000011', 'HANDOVER_CREATE',     'Yêu cầu bàn giao',     'handover',     'create'),
 ('5eed0000-0000-0000-0009-000000000012', 'HANDOVER_SET_SHARE',  'Đặt tỷ lệ chia điểm khi bàn giao', 'handover', 'set_share'),
 ('5eed0000-0000-0000-0009-000000000013', 'HANDOVER_RESPOND',    'Chấp nhận hoặc từ chối bàn giao', 'handover', 'respond'),
 ('5eed0000-0000-0000-0009-000000000014', 'HANDOVER_MANAGE',     'Quản lý duyệt, đổi người nhận hoặc từ chối bàn giao', 'handover', 'manage'),
 ('5eed0000-0000-0000-0009-000000000015', 'ORGANIZATION_READ',    'Xem tổ chức (phòng ban, vị trí công việc)', 'organization', 'read'),
 ('5eed0000-0000-0000-0009-000000000016', 'JOB_POSITION_MANAGE', 'Quản lý loại công việc của vị trí công việc', 'job-position', 'manage'),
 ('5eed0000-0000-0000-0009-000000000017', 'EVALUATION_PROFILE_APPROVE', 'Duyệt hoặc từ chối hồ sơ đánh giá', 'evaluation-profile', 'approve'),
 ('5eed0000-0000-0000-0009-000000000018', 'AUDIT_LOG_VIEW',      'Xem nhật ký audit',    'audit_log',    'view'),
 ('5eed0000-0000-0000-0009-000000000019', 'OBJECTIVE_READ',      'Xem mục tiêu kinh doanh',     'objective', 'read'),
 ('5eed0000-0000-0000-0009-00000000001a', 'OBJECTIVE_MANAGE',    'Quản lý mục tiêu kinh doanh', 'objective', 'manage'),
 ('5eed0000-0000-0000-0009-00000000001b', 'KPI_REQUEST',         'Yêu cầu bổ sung KPI',         'kpi',       'request'),
 ('5eed0000-0000-0000-0009-00000000001c', 'KPI_APPROVE',         'Duyệt hoặc từ chối KPI',      'kpi',       'approve'),
 ('5eed0000-0000-0000-0009-00000000001d', 'NOTIFICATION_READ',   'Xem thông báo của mình',      'notification', 'read'),
 ('5eed0000-0000-0000-0009-00000000001e', 'NOTIFICATION_SEND',   'Gửi thông báo cho nhân viên', 'notification', 'send'),
 ('5eed0000-0000-0000-0009-00000000001f', 'TRANSACTION_IMPORT_MANAGE', 'Import giao dịch Core Banking', 'transaction-import', 'manage'),
 ('5eed0000-0000-0000-0009-000000000020', 'QUALITY_FLAG_CREATE', 'Gắn cờ chất lượng kết quả đã duyệt', 'quality_flag', 'create'),
 ('5eed0000-0000-0000-0009-000000000021', 'QUALITY_FLAG_READ', 'Xem cờ chất lượng', 'quality_flag', 'read'),
 ('5eed0000-0000-0000-0009-000000000022', 'QUALITY_FLAG_REWORK', 'Yêu cầu nhân viên làm lại', 'quality_flag', 'rework'),
 ('5eed0000-0000-0000-0009-000000000023', 'QUALITY_FLAG_DISMISS', 'Bỏ qua cờ chất lượng', 'quality_flag', 'dismiss'),
 ('5eed0000-0000-0000-0009-000000000024', 'QA_SAMPLE_READ', 'Xem mẫu kiểm tra QA 5%', 'qa_sample', 'read'),
 ('5eed0000-0000-0000-0009-000000000025', 'QA_SAMPLE_DECIDE', 'Quyết định kết quả kiểm tra mẫu QA', 'qa_sample', 'decide'),
 ('5eed0000-0000-0000-0009-000000000026', 'DATA_IMPORT',         'Import dữ liệu',       'data_import',  'import'),
 ('5eed0000-0000-0000-0009-000000000027', 'RISK_EVENT_CREATE', 'Ghi sự kiện rủi ro', 'risk_event', 'create'),
 ('5eed0000-0000-0000-0009-000000000028', 'RISK_EVENT_READ', 'Xem sự kiện rủi ro', 'risk_event', 'read'),
 ('5eed0000-0000-0000-0009-000000000029', 'RISK_EVENT_CONFIRM', 'Xác nhận sự kiện rủi ro', 'risk_event', 'confirm'),
 ('5eed0000-0000-0000-0009-00000000002a', 'RISK_EVENT_DISMISS', 'Bác bỏ sự kiện rủi ro', 'risk_event', 'dismiss'),
 ('5eed0000-0000-0000-0009-00000000002b', 'RISK_EVENT_RESOLVE', 'Giải quyết sự kiện rủi ro', 'risk_event', 'resolve'),
 ('5eed0000-0000-0000-0009-00000000002c', 'RISK_EVENT_REVERSE', 'Gỡ điểm phạt sự kiện rủi ro', 'risk_event', 'reverse'),
 ('5eed0000-0000-0000-0009-00000000002d', 'SYSTEM_PARAMETER_VIEW',   'Xem tham số hệ thống',      'system_parameter', 'view'),
 ('5eed0000-0000-0000-0009-00000000002e', 'SYSTEM_PARAMETER_UPDATE', 'Cập nhật tham số hệ thống', 'system_parameter', 'update'),
 ('5eed0000-0000-0000-0009-000000000030', 'RECONCILIATION_READ', 'Xem đối soát giao dịch', 'reconciliation', 'read'),
 ('5eed0000-0000-0000-0009-000000000031', 'ROLE_UPDATE', 'Quản lý tài khoản và vai trò', 'role', 'update'),
 ('5eed0000-0000-0000-0009-000000000032', 'WORK_TYPE_MANAGE', 'Quản lý loại công việc', 'work-type', 'manage'),
 ('5eed0000-0000-0000-0009-000000000033', 'AI_REVIEW_FLAG_READ', 'Xem đoạn ghi âm cần nghe lại', 'ai_review_flag', 'read'),
 ('5eed0000-0000-0000-0009-000000000034', 'AI_REVIEW_FLAG_REVIEW', 'Xác nhận hoặc bỏ qua đoạn ghi âm cần nghe lại', 'ai_review_flag', 'review'),
 ('5eed0000-0000-0000-0009-000000000035', 'CALL_RECORDING_PLAY', 'Nghe ghi âm cuộc gọi', 'call_recording', 'play'),
 ('5eed0000-0000-0000-0009-000000000036', 'SERVICE_RATING_IMPORT_MANAGE', 'Import đánh giá dịch vụ', 'service_rating_import', 'manage'),
 ('5eed0000-0000-0000-0009-000000000037', 'CAMPAIGN_CONTACT',    'Cập nhật liên hệ khách hàng chiến dịch', 'campaign_customer', 'update')
ON CONFLICT DO NOTHING;

INSERT INTO role_permission (role_id, permission_id, scope)
SELECT r.id, p.id, m.scope
FROM (VALUES
    -- Campaign customer contact tracking (docs/api/CAMPAIGN_CONTRACT.md §5); own-scope for every role that can be a campaign participant or owner.
    ('EMPLOYEE', 'CAMPAIGN_CONTACT', 'Own'), ('DEPT_MANAGER', 'CAMPAIGN_CONTACT', 'Own'), ('BRANCH_MANAGER', 'CAMPAIGN_CONTACT', 'Own'),
    ('SYSTEM_ADMIN', 'WORK_READ', 'All'), ('SYSTEM_ADMIN', 'WORK_CREATE', 'All'), ('SYSTEM_ADMIN', 'WORK_UPDATE', 'All'),
    ('SYSTEM_ADMIN', 'SUBMISSION_READ', 'All'), ('SYSTEM_ADMIN', 'SUBMISSION_CREATE', 'All'), ('SYSTEM_ADMIN', 'VERIFICATION_DECIDE', 'All'),
    ('SYSTEM_ADMIN', 'KPI_READ', 'All'), ('SYSTEM_ADMIN', 'KPI_MANAGE', 'All'), ('SYSTEM_ADMIN', 'TARGET_MANAGE', 'All'), ('SYSTEM_ADMIN', 'EVALUATION_READ', 'All'),
    ('SYSTEM_ADMIN', 'EMPLOYEE_READ', 'All'), ('SYSTEM_ADMIN', 'EVALUATION_SUBMIT', 'All'), ('SYSTEM_ADMIN', 'EVALUATION_CALIBRATE', 'All'),
    ('SYSTEM_ADMIN', 'EVALUATION_CALCULATE', 'All'), ('SYSTEM_ADMIN', 'EVALUATION_FINALIZE', 'All'), ('SYSTEM_ADMIN', 'CAMPAIGN_READ', 'All'), ('SYSTEM_ADMIN', 'CAMPAIGN_MANAGE', 'All'), 
    ('SYSTEM_ADMIN', 'HANDOVER_CREATE', 'All'), ('SYSTEM_ADMIN', 'HANDOVER_SET_SHARE', 'All'),
    ('SYSTEM_ADMIN', 'HANDOVER_RESPOND', 'All'), ('SYSTEM_ADMIN', 'HANDOVER_MANAGE', 'All'),
    ('DEPT_MANAGER', 'WORK_READ', 'Department'), ('DEPT_MANAGER', 'WORK_CREATE', 'Department'), ('DEPT_MANAGER', 'WORK_UPDATE', 'Department'),
    ('DEPT_MANAGER', 'SUBMISSION_READ', 'Department'), ('DEPT_MANAGER', 'VERIFICATION_DECIDE', 'Department'),
    ('DEPT_MANAGER', 'KPI_READ', 'All'), ('DEPT_MANAGER', 'TARGET_MANAGE', 'Department'), ('DEPT_MANAGER', 'EVALUATION_READ', 'Department'),
    ('DEPT_MANAGER', 'EMPLOYEE_READ', 'Department'), ('DEPT_MANAGER', 'EVALUATION_SUBMIT', 'Department'), ('DEPT_MANAGER', 'EVALUATION_CALIBRATE', 'Department'), ('DEPT_MANAGER', 'EVALUATION_CALCULATE', 'Department'),
    ('DEPT_MANAGER', 'CAMPAIGN_READ', 'Department'), ('DEPT_MANAGER', 'CAMPAIGN_MANAGE', 'Department'), ('DEPT_MANAGER', 'HANDOVER_CREATE', 'Own'), ('DEPT_MANAGER', 'HANDOVER_SET_SHARE', 'Own'), ('DEPT_MANAGER', 'HANDOVER_RESPOND', 'Own'), ('DEPT_MANAGER', 'HANDOVER_MANAGE', 'Own'),
    ('VERIFIER', 'SUBMISSION_READ', 'Department'), ('VERIFIER', 'VERIFICATION_DECIDE', 'Department'), ('VERIFIER', 'KPI_READ', 'All'),
    ('EMPLOYEE', 'WORK_READ', 'Own'), ('EMPLOYEE', 'WORK_UPDATE', 'Own'), ('EMPLOYEE', 'SUBMISSION_READ', 'Own'),
    ('EMPLOYEE', 'SUBMISSION_CREATE', 'Own'), ('EMPLOYEE', 'KPI_READ', 'All'), ('EMPLOYEE', 'EVALUATION_READ', 'Own'),
    ('EMPLOYEE', 'EMPLOYEE_READ', 'Own'), ('EMPLOYEE', 'HANDOVER_CREATE', 'Own'), ('EMPLOYEE', 'HANDOVER_RESPOND', 'Own'),
    ('SYSTEM_ADMIN', 'ORGANIZATION_READ', 'All'), ('SYSTEM_ADMIN', 'ORGANIZATION_READ', 'All'), ('BRANCH_MANAGER', 'ORGANIZATION_READ', 'Branch'),
    ('DEPT_MANAGER', 'ORGANIZATION_READ', 'Dept'), ('DEPT_MANAGER', 'ORGANIZATION_READ', 'Dept'),
    -- Managers only: the system administrator administers the system, not the organization's job positions.
    ('BRANCH_MANAGER', 'JOB_POSITION_MANAGE', 'Branch'), ('DEPT_MANAGER', 'JOB_POSITION_MANAGE', 'Dept'), ('DEPT_MANAGER', 'JOB_POSITION_MANAGE', 'Dept'),
    -- Admin writes evaluation profiles; the Branch Manager approves them (docs/BPMS_AI_Agent_Context.md).
    ('BRANCH_MANAGER', 'EVALUATION_PROFILE_APPROVE', 'All'),
    -- The Branch Manager owns business objectives and approves KPIs; the Department Manager reads objectives of the department.
    ('DEPT_MANAGER', 'OBJECTIVE_READ', 'Dept'), ('DEPT_MANAGER', 'OBJECTIVE_READ', 'Dept'), ('BRANCH_MANAGER', 'OBJECTIVE_READ', 'Branch'),
    ('BRANCH_MANAGER', 'OBJECTIVE_MANAGE', 'All'), ('BRANCH_MANAGER', 'KPI_REQUEST', 'All'), ('BRANCH_MANAGER', 'KPI_APPROVE', 'All'),
    ('SYSTEM_ADMIN', 'AUDIT_LOG_VIEW', 'All'),
    ('SYSTEM_ADMIN', 'NOTIFICATION_READ', 'All'), ('DEPT_MANAGER', 'NOTIFICATION_READ', 'Own'), ('EMPLOYEE', 'NOTIFICATION_READ', 'Own'),
    ('VERIFIER', 'NOTIFICATION_READ', 'Own'), ('VERIFIER', 'NOTIFICATION_READ', 'Own'), ('HR_OFFICER', 'NOTIFICATION_READ', 'Own'),
    ('HR_OFFICER', 'EMPLOYEE_READ', 'All'), ('HR_OFFICER', 'ORGANIZATION_READ', 'All'), ('HR_OFFICER', 'EVALUATION_READ', 'All'),
    ('BRANCH_MANAGER', 'EVALUATION_FINALIZE', 'Branch'), ('VERIFIER', 'EVALUATION_READ', 'Own'), ('VERIFIER', 'EVALUATION_READ', 'Own'), ('SYSTEM_ADMIN', 'ROLE_UPDATE', 'All'), ('SYSTEM_ADMIN', 'WORK_TYPE_MANAGE', 'All'),
    ('SYSTEM_ADMIN', 'NOTIFICATION_SEND', 'All'), ('SYSTEM_ADMIN', 'TRANSACTION_IMPORT_MANAGE', 'All'),
    ('SYSTEM_ADMIN', 'TRANSACTION_IMPORT_MANAGE', 'All'), ('DEPT_MANAGER', 'TRANSACTION_IMPORT_MANAGE', 'All'),
    ('BRANCH_MANAGER', 'TRANSACTION_IMPORT_MANAGE', 'All'), ('VERIFIER', 'TRANSACTION_IMPORT_MANAGE', 'All'),
    -- Service rating import (docs/api/SERVICE_RATING_IMPORT_CONTRACT.md); the migration AddServiceRatingImport grants the same on an existing database.
    ('SYSTEM_ADMIN', 'SERVICE_RATING_IMPORT_MANAGE', 'All'), ('DEPT_MANAGER', 'SERVICE_RATING_IMPORT_MANAGE', 'All'),
    ('BRANCH_MANAGER', 'SERVICE_RATING_IMPORT_MANAGE', 'All'), ('VERIFIER', 'SERVICE_RATING_IMPORT_MANAGE', 'All'),
    ('DEPT_MANAGER', 'NOTIFICATION_SEND', 'Dept'),
    ('DEPT_MANAGER', 'QUALITY_FLAG_CREATE', 'Dept'), ('DEPT_MANAGER', 'QUALITY_FLAG_REWORK', 'Dept'),
    ('BRANCH_MANAGER', 'QUALITY_FLAG_CREATE', 'Branch'), ('BRANCH_MANAGER', 'QUALITY_FLAG_REWORK', 'Branch'),
    -- QA quality control (docs/api/QUALITY_ASSURANCE_CONTRACT.md); the migration AddQualityAssurance grants the same on an existing database.
    ('SYSTEM_ADMIN', 'QUALITY_FLAG_CREATE', 'All'), ('SYSTEM_ADMIN', 'QUALITY_FLAG_READ', 'All'), ('SYSTEM_ADMIN', 'QUALITY_FLAG_REWORK', 'All'), ('SYSTEM_ADMIN', 'QUALITY_FLAG_DISMISS', 'All'), ('SYSTEM_ADMIN', 'QA_SAMPLE_READ', 'All'), ('SYSTEM_ADMIN', 'QA_SAMPLE_DECIDE', 'All'), ('VERIFIER', 'QUALITY_FLAG_CREATE', 'All'), ('VERIFIER', 'QUALITY_FLAG_READ', 'All'), ('VERIFIER', 'QUALITY_FLAG_REWORK', 'All'), ('VERIFIER', 'QUALITY_FLAG_DISMISS', 'All'), ('VERIFIER', 'QA_SAMPLE_READ', 'All'), ('VERIFIER', 'QA_SAMPLE_DECIDE', 'All'),
    -- Risk events (docs/api/RISK_EVENT_CONTRACT.md §9): QA and administrators record and reverse; managers read within their scope.
    ('SYSTEM_ADMIN', 'RISK_EVENT_CREATE', 'All'), ('SYSTEM_ADMIN', 'RISK_EVENT_READ', 'All'), ('SYSTEM_ADMIN', 'RISK_EVENT_CONFIRM', 'All'), ('SYSTEM_ADMIN', 'RISK_EVENT_DISMISS', 'All'), ('SYSTEM_ADMIN', 'RISK_EVENT_RESOLVE', 'All'), ('SYSTEM_ADMIN', 'RISK_EVENT_REVERSE', 'All'), ('VERIFIER', 'RISK_EVENT_CREATE', 'All'), ('VERIFIER', 'RISK_EVENT_READ', 'All'), ('VERIFIER', 'RISK_EVENT_CONFIRM', 'All'), ('VERIFIER', 'RISK_EVENT_DISMISS', 'All'), ('VERIFIER', 'RISK_EVENT_RESOLVE', 'All'), ('VERIFIER', 'RISK_EVENT_REVERSE', 'All'),
    ('DEPT_MANAGER', 'RISK_EVENT_READ', 'Dept'),
    -- AI call triage (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §2); the migration AddCallRecordingAiTriage grants the same on an existing database.
    ('SYSTEM_ADMIN', 'AI_REVIEW_FLAG_READ', 'All'), ('SYSTEM_ADMIN', 'AI_REVIEW_FLAG_REVIEW', 'All'), ('SYSTEM_ADMIN', 'CALL_RECORDING_PLAY', 'All'), ('VERIFIER', 'AI_REVIEW_FLAG_READ', 'All'), ('VERIFIER', 'AI_REVIEW_FLAG_REVIEW', 'All'), ('VERIFIER', 'CALL_RECORDING_PLAY', 'All'),
    -- Data Import (docs/api/DATA_IMPORT_CONTRACT.md): Admin-only, per BPMS_AI_Agent_Context.md.
    ('SYSTEM_ADMIN', 'DATA_IMPORT', 'All'), ('SYSTEM_ADMIN', 'DATA_IMPORT', 'All'),
    -- System parameters (SCR-ADM-04, docs/api/SYSTEM_PARAMETER_CONTRACT.md).
    ('SYSTEM_ADMIN', 'SYSTEM_PARAMETER_VIEW', 'All'), ('SYSTEM_ADMIN', 'SYSTEM_PARAMETER_UPDATE', 'All'),
    ('SYSTEM_ADMIN', 'SYSTEM_PARAMETER_VIEW', 'All'), ('SYSTEM_ADMIN', 'SYSTEM_PARAMETER_UPDATE', 'All'),
    -- Reconciliation (docs/api/RECONCILIATION_CONTRACT.md); the migration GrantReconciliationRead grants the same on an existing database.
    ('VERIFIER', 'RECONCILIATION_READ', 'All'), ('SYSTEM_ADMIN', 'RECONCILIATION_READ', 'All'), ('SYSTEM_ADMIN', 'RECONCILIATION_READ', 'All'),
    ('BRANCH_MANAGER', 'RECONCILIATION_READ', 'Branch'), ('DEPT_MANAGER', 'RECONCILIATION_READ', 'Dept'),
    ('DEPT_MANAGER', 'RECONCILIATION_READ', 'Dept'),
    -- The verifier is the QA / inspector of the system (the former QA role was merged into VERIFIER).
    ('VERIFIER', 'RECONCILIATION_READ', 'All'), ('VERIFIER', 'TRANSACTION_IMPORT_MANAGE', 'All'),
    ('VERIFIER', 'QUALITY_FLAG_CREATE', 'All'), ('VERIFIER', 'QUALITY_FLAG_READ', 'All'), ('VERIFIER', 'QUALITY_FLAG_REWORK', 'All'), ('VERIFIER', 'QUALITY_FLAG_DISMISS', 'All'),
    ('VERIFIER', 'QA_SAMPLE_READ', 'All'), ('VERIFIER', 'QA_SAMPLE_DECIDE', 'All'),
    ('VERIFIER', 'RISK_EVENT_CREATE', 'All'), ('VERIFIER', 'RISK_EVENT_READ', 'All'), ('VERIFIER', 'RISK_EVENT_CONFIRM', 'All'), ('VERIFIER', 'RISK_EVENT_DISMISS', 'All'), ('VERIFIER', 'RISK_EVENT_RESOLVE', 'All'), ('VERIFIER', 'RISK_EVENT_REVERSE', 'All')
) AS m(role_code, permission_code, scope)
JOIN role r ON r.code = m.role_code
JOIN permission p ON p.code = m.permission_code
ON CONFLICT DO NOTHING;

-- BRANCH_MANAGER inherits the DEPT_MANAGER grants with department scopes widened to Branch (the same copy the migration
-- GrantWorkAccessToOrganizationRoles performs), because a fresh database has no DEPT_MANAGER grants when migrations run.
INSERT INTO role_permission (role_id, permission_id, scope)
SELECT target.id, rp.permission_id,
       CASE WHEN lower(rp.scope) IN ('dept', 'department') THEN 'Branch' ELSE rp.scope END
FROM role_permission rp
JOIN role source ON source.id = rp.role_id AND source.code = 'DEPT_MANAGER'
JOIN role target ON target.code = 'BRANCH_MANAGER'
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Work type catalog
-- ---------------------------------------------------------------------------
INSERT INTO work_type (id, code, name, description, product_code) VALUES
 ('5eed0000-0000-0000-000c-000000000001', 'MO_TAI_KHOAN',    'Mở tài khoản khách hàng', NULL, NULL),
 ('5eed0000-0000-0000-000c-000000000002', 'HUY_DONG',        'Huy động tiền gửi',       NULL, NULL),
 ('5eed0000-0000-0000-000c-000000000003', 'GIAI_NGAN',       'Giải ngân khoản vay',     NULL, NULL),
 ('5eed0000-0000-0000-000c-000000000004', 'GIAO_DICH',       'Giao dịch tại quầy',      NULL, NULL),
 -- BR-48 Task Multiplier: one work type per card tier, each carrying the product code the future
 -- transaction matcher (BR-09) will require to match, and its own multiplier on the KPI mapping below.
 ('5eed0000-0000-0000-000c-000000000005', 'THE_TD_CLASSIC',  'Mở thẻ tín dụng Classic',  'Thẻ tín dụng hạng Classic',  'TD_CLASSIC'),
 ('5eed0000-0000-0000-000c-000000000006', 'THE_TD_GOLD',     'Mở thẻ tín dụng Gold',     'Thẻ tín dụng hạng Gold',     'TD_GOLD'),
 ('5eed0000-0000-0000-000c-000000000007', 'THE_TD_PLATINUM', 'Mở thẻ tín dụng Platinum', 'Thẻ tín dụng hạng Platinum', 'TD_PLATINUM'),
 -- Core Banking product catalog (01_Danh_muc_ma.xlsx / 06_Giao_dich.xlsx):
 ('5eed0000-0000-0000-000c-000000000008', 'WT_DEPOSIT',          'Nộp tiền mặt/chuyển khoản vào TK thanh toán', NULL, 'DEPOSIT'),
 ('5eed0000-0000-0000-000c-000000000009', 'WT_TERM_DEPOSIT',     'Mở sổ tiết kiệm / tiền gửi có kỳ hạn',        NULL, 'TERM_DEPOSIT'),
 ('5eed0000-0000-0000-000c-00000000000a', 'WT_WITHDRAWAL',       'Rút tiền mặt tại quầy',                       NULL, 'WITHDRAWAL'),
 ('5eed0000-0000-0000-000c-00000000000b', 'WT_TRANSFER',         'Chuyển tiền trong/ngoài hệ thống',            NULL, 'TRANSFER'),
 ('5eed0000-0000-0000-000c-00000000000c', 'WT_ACCOUNT_OPEN',     'Mở tài khoản thanh toán mới',                 NULL, 'ACCOUNT_OPEN'),
 ('5eed0000-0000-0000-000c-00000000000d', 'WT_CARD_DEBIT_STD',   'Phát hành thẻ ghi nợ Chuẩn',                  NULL, 'CARD_DEBIT_STD'),
 ('5eed0000-0000-0000-000c-00000000000e', 'WT_CARD_DEBIT_GOLD',  'Phát hành thẻ ghi nợ Vàng',                   NULL, 'CARD_DEBIT_GOLD'),
 ('5eed0000-0000-0000-000c-00000000000f', 'WT_CARD_CREDIT_STD',  'Phát hành thẻ tín dụng Chuẩn',                NULL, 'CARD_CREDIT_STD'),
 ('5eed0000-0000-0000-000c-000000000010', 'WT_CARD_CREDIT_PLAT', 'Phát hành thẻ tín dụng Platinum',             NULL, 'CARD_CREDIT_PLAT'),
 ('5eed0000-0000-0000-000c-000000000011', 'WT_EBANKING',         'Đăng ký và kích hoạt eBanking',               NULL, 'EBANKING'),
 ('5eed0000-0000-0000-000c-000000000012', 'WT_SMS_BANKING',      'Đăng ký SMS biến động số dư',                 NULL, 'SMS_BANKING'),
 ('5eed0000-0000-0000-000c-000000000013', 'WT_QR_MERCHANT',      'Mở điểm chấp nhận thanh toán QR',             NULL, 'QR_MERCHANT'),
 ('5eed0000-0000-0000-000c-000000000014', 'WT_INSURANCE',        'Hợp đồng bảo hiểm liên kết ngân hàng',        NULL, 'INSURANCE')
ON CONFLICT DO NOTHING;

-- Fixed ids are required: the overlap trigger excludes rows with the same id.
INSERT INTO work_type_kpi (id, work_type_id, kpi_definition_id, is_required, multiplier, effective_from) VALUES
 ('5eed0000-0000-0000-000d-000000000001', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0005-000000000002', TRUE,  1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000002', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0005-000000000006', FALSE, 1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000003', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0005-000000000001', TRUE,  1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000004', '5eed0000-0000-0000-000c-000000000003', '5eed0000-0000-0000-0005-000000000003', TRUE,  1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000005', '5eed0000-0000-0000-000c-000000000003', '5eed0000-0000-0000-0005-000000000006', FALSE, 1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000006', '5eed0000-0000-0000-000c-000000000004', '5eed0000-0000-0000-0005-000000000004', TRUE,  1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000007', '5eed0000-0000-0000-000c-000000000004', '5eed0000-0000-0000-0005-000000000005', TRUE,  1.0, '2026-01-01'),
 -- Classic / Gold / Platinum: multiplier 1.0 / 1.2 / 1.5 on the same KPI "Thẻ tín dụng" (THE_TD).
 ('5eed0000-0000-0000-000d-000000000008', '5eed0000-0000-0000-000c-000000000005', '5eed0000-0000-0000-0005-000000000007', TRUE,  1.0, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000009', '5eed0000-0000-0000-000c-000000000006', '5eed0000-0000-0000-0005-000000000007', TRUE,  1.2, '2026-01-01'),
 ('5eed0000-0000-0000-000d-00000000000a', '5eed0000-0000-0000-000c-000000000007', '5eed0000-0000-0000-0005-000000000007', TRUE,  1.5, '2026-01-01')
ON CONFLICT DO NOTHING;

INSERT INTO job_position_work_type (job_position_id, work_type_id)
SELECT jp.id, wt.id
FROM (VALUES
    ('SALES', 'MO_TAI_KHOAN'), ('SALES', 'HUY_DONG'), ('SALES', 'GIAI_NGAN'),
    ('TELLER', 'MO_TAI_KHOAN'), ('TELLER', 'GIAO_DICH'),
    ('SALES', 'THE_TD_CLASSIC'), ('SALES', 'THE_TD_GOLD'), ('SALES', 'THE_TD_PLATINUM')
) AS m(position_code, work_type_code)
JOIN job_position jp ON jp.code = m.position_code
JOIN work_type wt ON wt.code = m.work_type_code
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Safety purge: clear any lingering >= 2027 test data
-- ---------------------------------------------------------------------------
DELETE FROM employee_target_ack WHERE year >= 2027;
DELETE FROM target_change_log WHERE year >= 2027;
DELETE FROM target_assignment_feedback WHERE target_assignment_id IN (
    SELECT id FROM target_assignment WHERE evaluation_period_id IN (
        SELECT id FROM evaluation_period WHERE code LIKE '2027%'
    )
);
DELETE FROM target_assignment WHERE evaluation_period_id IN (
    SELECT id FROM evaluation_period WHERE code LIKE '2027%'
);
DELETE FROM evaluation_period WHERE code LIKE '2027%';

-- ---------------------------------------------------------------------------
-- Evaluation periods: 2026 -> H1/H2 -> Q1..Q4 -> Oct/Nov/Dec -> every day of Q4
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-000f-000000000001', '2026',    'Năm 2026',          'YEAR',      NULL,                                   '2026-01-01', '2026-12-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000007', '2026-H1', '6 tháng đầu 2026',  'HALF_YEAR', '5eed0000-0000-0000-000f-000000000001', '2026-01-01', '2026-06-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000002', '2026-H2', '6 tháng cuối 2026', 'HALF_YEAR', '5eed0000-0000-0000-000f-000000000001', '2026-07-01', '2026-12-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000008', '2026-Q1', 'Quý 1/2026',        'QUARTER',   '5eed0000-0000-0000-000f-000000000007', '2026-01-01', '2026-03-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000009', '2026-Q2', 'Quý 2/2026',        'QUARTER',   '5eed0000-0000-0000-000f-000000000007', '2026-04-01', '2026-06-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000a', '2026-Q3', 'Quý 3/2026',        'QUARTER',   '5eed0000-0000-0000-000f-000000000002', '2026-07-01', '2026-09-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000003', '2026-Q4', 'Quý 4/2026',        'QUARTER',   '5eed0000-0000-0000-000f-000000000002', '2026-10-01', '2026-12-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000004', '2026-10', 'Tháng 10/2026',     'MONTH',     '5eed0000-0000-0000-000f-000000000003', '2026-10-01', '2026-10-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000005', '2026-11', 'Tháng 11/2026',     'MONTH',     '5eed0000-0000-0000-000f-000000000003', '2026-11-01', '2026-11-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000006', '2026-12', 'Tháng 12/2026',     'MONTH',     '5eed0000-0000-0000-000f-000000000003', '2026-12-01', '2026-12-31', 'OPEN')
ON CONFLICT DO NOTHING;

INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status)
SELECT md5('progest-seed-day-' || d::date)::uuid,
       'D' || to_char(d, 'YYYY-MM-DD'),
       'Ngày ' || to_char(d, 'DD/MM/YYYY'),
       'DAY',
       CASE EXTRACT(MONTH FROM d)
           WHEN 10 THEN '5eed0000-0000-0000-000f-000000000004'::uuid
           WHEN 11 THEN '5eed0000-0000-0000-000f-000000000005'::uuid
           ELSE '5eed0000-0000-0000-000f-000000000006'::uuid
       END,
       d::date, d::date, 'OPEN'
FROM generate_series('2026-10-01'::date, '2026-12-31'::date, interval '1 day') AS d
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2026: Add H1 and Q1-Q3 periods (months 01-09, all DAY rows)
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-000f-000000000007', '2026-H1', '6 tháng đầu 2026', 'HALF_YEAR', '5eed0000-0000-0000-000f-000000000001', '2026-01-01', '2026-06-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000008', '2026-Q1', 'Quý 1/2026',       'QUARTER',   '5eed0000-0000-0000-000f-000000000007', '2026-01-01', '2026-03-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000009', '2026-Q2', 'Quý 2/2026',       'QUARTER',   '5eed0000-0000-0000-000f-000000000007', '2026-04-01', '2026-06-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000a', '2026-Q3', 'Quý 3/2026',       'QUARTER',   '5eed0000-0000-0000-000f-000000000002', '2026-07-01', '2026-09-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000b', '2026-01', 'Tháng 01/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000008', '2026-01-01', '2026-01-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000c', '2026-02', 'Tháng 02/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000008', '2026-02-01', '2026-02-28', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000d', '2026-03', 'Tháng 03/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000008', '2026-03-01', '2026-03-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000e', '2026-04', 'Tháng 04/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000009', '2026-04-01', '2026-04-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-00000000000f', '2026-05', 'Tháng 05/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000009', '2026-05-01', '2026-05-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000010', '2026-06', 'Tháng 06/2026',    'MONTH',     '5eed0000-0000-0000-000f-000000000009', '2026-06-01', '2026-06-30', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000011', '2026-07', 'Tháng 07/2026',    'MONTH',     '5eed0000-0000-0000-000f-00000000000a', '2026-07-01', '2026-07-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000012', '2026-08', 'Tháng 08/2026',    'MONTH',     '5eed0000-0000-0000-000f-00000000000a', '2026-08-01', '2026-08-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000013', '2026-09', 'Tháng 09/2026',    'MONTH',     '5eed0000-0000-0000-000f-00000000000a', '2026-09-01', '2026-09-30', 'OPEN')
ON CONFLICT DO NOTHING;

-- Add all DAY rows for Jan-Sep 2026
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status)
SELECT md5('progest-seed-day-' || d::date)::uuid,
       'D' || to_char(d, 'YYYY-MM-DD'),
       'Ngày ' || to_char(d, 'DD/MM/YYYY'),
       'DAY',
       CASE EXTRACT(MONTH FROM d)
           WHEN 1 THEN '5eed0000-0000-0000-000f-00000000000b'::uuid
           WHEN 2 THEN '5eed0000-0000-0000-000f-00000000000c'::uuid
           WHEN 3 THEN '5eed0000-0000-0000-000f-00000000000d'::uuid
           WHEN 4 THEN '5eed0000-0000-0000-000f-00000000000e'::uuid
           WHEN 5 THEN '5eed0000-0000-0000-000f-00000000000f'::uuid
           WHEN 6 THEN '5eed0000-0000-0000-000f-000000000010'::uuid
           WHEN 7 THEN '5eed0000-0000-0000-000f-000000000011'::uuid
           WHEN 8 THEN '5eed0000-0000-0000-000f-000000000012'::uuid
           ELSE '5eed0000-0000-0000-000f-000000000013'::uuid
       END,
       d::date, d::date, 'OPEN'
FROM generate_series('2026-01-01'::date, '2026-09-30'::date, interval '1 day') AS d
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Strategy: objective, campaign, participants
-- ---------------------------------------------------------------------------
INSERT INTO business_objective (id, code, name, description, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-0010-000000000001', 'OBJ-2026-TT', 'Tăng trưởng chi nhánh Hà Nội 2026', 'Mở rộng nền khách hàng và huy động vốn', '2026-01-01', '2026-12-31', 'ACTIVE')
ON CONFLICT DO NOTHING;

INSERT INTO objective_kpi (id, business_objective_id, kpi_definition_id) VALUES
 ('5eed0000-0000-0000-0011-000000000001', '5eed0000-0000-0000-0010-000000000001', '5eed0000-0000-0000-0005-000000000001'),
 ('5eed0000-0000-0000-0011-000000000002', '5eed0000-0000-0000-0010-000000000001', '5eed0000-0000-0000-0005-000000000002'),
 ('5eed0000-0000-0000-0011-000000000003', '5eed0000-0000-0000-0010-000000000001', '5eed0000-0000-0000-0005-000000000003')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- KPI catalog (SCR-STRAT-02): product, credit role, data source, applicable positions and validity of the
-- KPIs above, plus a few more KPIs in other statuses (two waiting for the Branch Manager, a draft, an inactive one).
-- The UPDATE only touches KPIs that have not been given catalog data yet, so re-running the seed keeps later edits.
-- ---------------------------------------------------------------------------
UPDATE kpi_definition k
SET product_name = v.product, credit_role = v.role, data_source = v.source,
    effective_from_period_id = p.id
FROM (VALUES
    ('HDV',       'Tiền gửi có kỳ hạn'::text, 'PROCESSOR', 'TRANSACTION'),
    ('KH_MOI',    NULL::text,                 'PROCESSOR', 'TRANSACTION'),
    ('DU_NO',     NULL::text,                 'PROCESSOR', 'TRANSACTION'),
    ('THE_TD',    'Thẻ / E-banking'::text,    'PROCESSOR', 'TRANSACTION'),
    ('CSAT',      NULL::text,                 'PROCESSOR', 'SERVICE_RATING'),
    ('TY_LE_LOI', NULL::text,                 'PROCESSOR', 'TRANSACTION'),
    ('TUAN_THU',  NULL::text,                 'PROCESSOR', 'TRANSACTION')
) AS v(code, product, role, source)
CROSS JOIN evaluation_period p
WHERE p.code = '2026-Q1' AND k.code = v.code
  AND k.product_name IS NULL AND k.effective_from_period_id IS NULL;

UPDATE kpi_definition k
SET baseline_value = v.baseline,
    max_achievement_percent = v.max_percent
FROM (VALUES
    ('CSAT',      5::numeric,   100::numeric),
    ('TY_LE_LOI', 0::numeric,   100::numeric),
    ('TUAN_THU',  100::numeric, 100::numeric)
) AS v(code, baseline, max_percent)
WHERE k.code = v.code
  AND (k.baseline_value IS NULL OR (k.code = 'TY_LE_LOI' AND k.max_achievement_percent = 120));

INSERT INTO kpi_definition (id, kpi_group_id, code, name, description, formula, unit, aggregation_type, direction,
                            status, credit_role, data_source, product_name, effective_from_period_id, submitted_at, request_note)
SELECT v.id::uuid, '5eed0000-0000-0000-0003-000000000001', v.code, v.name, v.description, v.formula, v.unit,
       v.aggregation::kpi_aggregation_type, 'HIGHER_BETTER', v.status::kpi_status, v.role, 'TRANSACTION', v.product,
       p.id, v.submitted_at::timestamptz, NULL
FROM (VALUES
    ('5eed0000-0000-0000-0005-0000000000a1', 'HDV_GT',    'Huy động vốn - giới thiệu', 'Số dư tiền gửi do nhân viên giới thiệu', 'SUM(số dư mới) WHERE Refer ID = Nhân viên', 'VND',
        'SUM',   'PENDING_APPROVAL', 'REFERRER', 'Tiền gửi có kỳ hạn', '2026-10-22 10:12:40+07'),
    ('5eed0000-0000-0000-0005-0000000000a2', 'THE_GT',    'Mở thẻ - giới thiệu',       'Số thẻ do nhân viên giới thiệu',         'COUNT(*) WHERE Sản phẩm = Thẻ AND Refer ID = Nhân viên', 'HĐ',
        'SUM',   'PENDING_APPROVAL', 'REFERRER', 'Thẻ / E-banking',    '2026-10-27 15:40:00+07'),
    ('5eed0000-0000-0000-0005-0000000000a3', 'DU_NO_GT',  'Giải ngân - giới thiệu',    'Dư nợ giải ngân do nhân viên giới thiệu', 'SUM(giải ngân) WHERE Refer ID = Nhân viên',    'VND',
        'SUM',   'DRAFT',            'REFERRER', NULL,                 NULL),
    ('5eed0000-0000-0000-0005-0000000000a4', 'GN_CU',     'Giải ngân (cũ)',            'KPI giải ngân của năm trước',            'SUM(giải ngân)',                              'VND',
        'SUM',   'INACTIVE',         'PROCESSOR', NULL,                NULL)
) AS v(id, code, name, description, formula, unit, aggregation, status, role, product, submitted_at)
CROSS JOIN evaluation_period p
WHERE p.code = '2026-Q4'
-- A demo KPI left over from before the catalog columns existed gets its catalog data; one that already has some is left alone.
ON CONFLICT (id) DO UPDATE
SET credit_role = EXCLUDED.credit_role, product_name = EXCLUDED.product_name,
    effective_from_period_id = EXCLUDED.effective_from_period_id, submitted_at = EXCLUDED.submitted_at
WHERE kpi_definition.effective_from_period_id IS NULL AND kpi_definition.product_name IS NULL;

INSERT INTO kpi_definition_job_position (kpi_definition_id, job_position_id)
SELECT k.id, jp.id
FROM (VALUES
    ('HDV', 'SALES'), ('HDV', 'TELLER'), ('KH_MOI', 'SALES'), ('DU_NO', 'SALES'),
    ('THE_TD', 'SALES'), ('THE_TD', 'TELLER'), ('CSAT', 'SALES'), ('CSAT', 'TELLER'),
    ('TY_LE_LOI', 'TELLER'), ('TUAN_THU', 'TELLER'), ('TUAN_THU', 'SALES'),
    ('HDV_GT', 'TELLER'), ('THE_GT', 'TELLER'), ('DU_NO_GT', 'TELLER'), ('GN_CU', 'SALES')
) AS m(kpi_code, position_code)
JOIN kpi_definition k ON k.code = m.kpi_code
JOIN job_position jp ON jp.code = m.position_code
ON CONFLICT DO NOTHING;

INSERT INTO business_campaign (id, business_objective_id, code, name, description, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0010-000000000001', 'CMP-2026-Q4-HDV', 'Chiến dịch huy động tiền gửi Q4/2026', 'Đẩy mạnh huy động và mở mới khách hàng cuối năm', '2026-10-01', '2026-12-31', 'RUNNING')
ON CONFLICT DO NOTHING;

-- A campaign now targets exactly one product (CAMPAIGN_CONTRACT.md §5.5). Keep the row the CampaignLeadWorks
-- migration keeps on existing databases (equal work counts, oldest row wins), so a reseed does not re-add the other.
INSERT INTO campaign_work_type (id, business_campaign_id, work_type_id) VALUES
 ('5eed0000-0000-0000-0013-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-000c-000000000001')
ON CONFLICT DO NOTHING;

-- 4 Sales + 2 Teller join the campaign.
INSERT INTO participant (id, business_campaign_id, employee_id) VALUES
 ('5eed0000-0000-0000-0014-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000004'),
 ('5eed0000-0000-0000-0014-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000005'),
 ('5eed0000-0000-0000-0014-000000000003', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000006'),
 ('5eed0000-0000-0000-0014-000000000004', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000007'),
 ('5eed0000-0000-0000-0014-000000000005', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000008'),
 ('5eed0000-0000-0000-0014-000000000006', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000009')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Targets Q4/2026 - HDV (tỷ VND), every SUM split adds up to its parent:
--   Chi nhánh 1000
--   ├─ Phòng Kinh doanh 900
--   │  ├─ Nhóm KD 1 450 -> Châu 225, Dũng 225
--   │  └─ Nhóm KD 2 450 -> Khang 225, Mai 225
--   └─ Phòng Giao dịch 100 -> Quầy 1 60, Quầy 2 40
-- Plus KH_MOI per Sales (no split) and one DRAFT CSAT target for a Teller.
-- Rows are inserted parent-first (ORDER BY row_no).
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT ('5eed0000-0000-0000-0015-' || t.row_no)::uuid,
       CASE WHEN t.kpi_no IN ('000000000001', '000000000002', '000000000003')
            THEN '5eed0000-0000-0000-0010-000000000001'::uuid END,
       ('5eed0000-0000-0000-0005-' || t.kpi_no)::uuid,
       ('5eed0000-0000-0000-0001-' || t.dept_no)::uuid,
       ('5eed0000-0000-0000-0007-' || t.emp_no)::uuid,
       '5eed0000-0000-0000-000f-000000000003'::uuid,
       ('5eed0000-0000-0000-0015-' || t.parent_no)::uuid,
       t.split::target_split_dimension,
       t.target_value,
       t.status::target_assignment_status,
       now(),
       CASE WHEN t.status = 'ACTIVE' THEN now() END,
       t.reason,
       (SELECT start_date FROM evaluation_period WHERE id = '5eed0000-0000-0000-000f-000000000003'::uuid)
FROM (VALUES
    ('000000000001', '000000000001', '000000000001', NULL,           NULL,           NULL,  1000, 'ACTIVE', '000000000001', 'Chỉ tiêu chi nhánh Q4'),
    ('000000000002', '000000000001', '000000000002', NULL,           '000000000001', 'ORG',  900, 'ACTIVE', '000000000001', NULL),
    ('000000000003', '000000000001', '000000000003', NULL,           '000000000001', 'ORG',  100, 'ACTIVE', '000000000001', NULL),
    ('000000000004', '000000000001', '000000000004', NULL,           '000000000002', 'ORG',  450, 'ACTIVE', '000000000002', NULL),
    ('000000000005', '000000000001', '000000000005', NULL,           '000000000002', 'ORG',  450, 'ACTIVE', '000000000002', NULL),
    ('000000000006', '000000000001', '000000000006', NULL,           '000000000003', 'ORG',   60, 'ACTIVE', '000000000003', NULL),
    ('000000000007', '000000000001', '000000000007', NULL,           '000000000003', 'ORG',   40, 'ACTIVE', '000000000003', NULL),
    ('000000000008', '000000000001', NULL,           '000000000004', '000000000004', 'ORG',  225, 'ACTIVE', '000000000002', NULL),
    ('000000000009', '000000000001', NULL,           '000000000005', '000000000004', 'ORG',  225, 'ACTIVE', '000000000002', NULL),
    ('00000000000a', '000000000001', NULL,           '000000000006', '000000000005', 'ORG',  225, 'ACTIVE', '000000000002', NULL),
    ('00000000000b', '000000000001', NULL,           '000000000007', '000000000005', 'ORG',  225, 'ACTIVE', '000000000002', NULL),
    ('00000000000c', '000000000002', NULL,           '000000000004', NULL,           NULL,    40, 'ACTIVE', '000000000002', NULL),
    ('00000000000d', '000000000002', NULL,           '000000000006', NULL,           NULL,    40, 'ACTIVE', '000000000002', NULL),
    ('00000000000e', '000000000004', NULL,           '000000000008', NULL,           NULL,   4.5, 'DRAFT',  '000000000003', 'Chờ duyệt')
) AS t(row_no, kpi_no, dept_no, emp_no, parent_no, split, target_value, status, assigned_by_no, reason)
ORDER BY t.row_no
ON CONFLICT DO NOTHING;

-- Year-level targets (DU_NO and KH_MOI) to be split by quarters
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT ('5eed0000-0000-0000-0015-' || t.row_no)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       ('5eed0000-0000-0000-0005-' || t.kpi_no)::uuid,
       ('5eed0000-0000-0000-0001-' || t.dept_no)::uuid,
       NULL::uuid,
       '5eed0000-0000-0000-000f-000000000001'::uuid,
       NULL::uuid,
       NULL::target_split_dimension,
       t.target_value,
       'ACTIVE'::target_assignment_status,
       now(),
       now(),
       t.reason,
       (SELECT start_date FROM evaluation_period WHERE id = '5eed0000-0000-0000-000f-000000000001'::uuid)
FROM (VALUES
    ('000000000010', '000000000003', '000000000001', 1200, 'Chỉ tiêu dư nợ năm 2026'),
    ('000000000020', '000000000002', '000000000001',  480, NULL)
) AS t(row_no, kpi_no, dept_no, target_value, reason)
ON CONFLICT DO NOTHING;

-- Quarter-level allocations of DU_NO (period split of year)
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT ('5eed0000-0000-0000-0015-' || t.row_no)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       '5eed0000-0000-0000-0005-000000000003'::uuid,
       '5eed0000-0000-0000-0001-000000000001'::uuid,
       NULL::uuid,
       ('5eed0000-0000-0000-000f-' || t.period_no)::uuid,
       '5eed0000-0000-0000-0015-000000000010'::uuid,
       'PERIOD'::target_split_dimension,
       t.target_value,
       'ACTIVE'::target_assignment_status,
       now(),
       now(),
       NULL,
       (SELECT start_date FROM evaluation_period WHERE id = ('5eed0000-0000-0000-000f-' || t.period_no)::uuid)
FROM (VALUES
    ('000000000011', '000000000008', 300),
    ('000000000012', '000000000009', 300),
    ('000000000013', '00000000000a', 300),
    ('000000000014', '000000000003', 300)
) AS t(row_no, period_no, target_value)
ORDER BY t.row_no
ON CONFLICT DO NOTHING;

-- ORG-level split of Q1 (DU_NO quarter target to departments)
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT ('5eed0000-0000-0000-0015-' || t.row_no)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       '5eed0000-0000-0000-0005-000000000003'::uuid,
       ('5eed0000-0000-0000-0001-' || t.dept_no)::uuid,
       NULL::uuid,
       '5eed0000-0000-0000-000f-000000000008'::uuid,
       '5eed0000-0000-0000-0015-000000000011'::uuid,
       'ORG'::target_split_dimension,
       t.target_value,
       'ACTIVE'::target_assignment_status,
       now(),
       now(),
       NULL,
       (SELECT start_date FROM evaluation_period WHERE id = '5eed0000-0000-0000-000f-000000000008'::uuid)
FROM (VALUES
    ('000000000015', '000000000002', 250),
    ('000000000016', '000000000003',  50)
) AS t(row_no, dept_no, target_value)
ORDER BY t.row_no
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Target allocation scenario 2026 (HDV, KH_MOI, DU_NO), stored the way SCR-STRAT-03/04 read it:
--   branch year (DRAFT, still being set up) -> PERIOD branch quarters and ORG department years
--   department year -> PERIOD department quarters -> ORG teams -> ORG employees
-- Q1-Q2 are fully allocated, Q3 is half-way (department rows DRAFT, GD has no teams), Q4 is only allocated
-- for the HDV chain above, so the wizard still has steps to run. Values are uneven on purpose.
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, effective_from)
SELECT t.id,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       t.kpi_id, t.dept_id, t.emp_id, t.period_id, t.parent_id, t.split, t.target_value, t.status,
       now(),
       CASE WHEN t.status = 'ACTIVE' THEN now() END,
       (SELECT start_date FROM evaluation_period WHERE id = t.period_id)
FROM (VALUES
    ('5eed0000-0000-0000-0035-000000000001'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, NULL::uuid, NULL::target_split_dimension, 3500, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000011'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000001'::uuid, 'PERIOD'::target_split_dimension, 750, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000012'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000001'::uuid, 'PERIOD'::target_split_dimension, 800, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000013'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000001'::uuid, 'PERIOD'::target_split_dimension, 950, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000021'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0015-000000000020'::uuid, 'PERIOD'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000022'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0015-000000000020'::uuid, 'PERIOD'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000023'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0015-000000000020'::uuid, 'PERIOD'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000024'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000003'::uuid, '5eed0000-0000-0000-0015-000000000020'::uuid, 'PERIOD'::target_split_dimension, 140, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000031'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, '5eed0000-0000-0000-0035-000000000001'::uuid, 'ORG'::target_split_dimension, 2630, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000032'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, '5eed0000-0000-0000-0035-000000000001'::uuid, 'ORG'::target_split_dimension, 870, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000033'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, '5eed0000-0000-0000-0015-000000000010'::uuid, 'ORG'::target_split_dimension, 800, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000034'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, '5eed0000-0000-0000-0015-000000000010'::uuid, 'ORG'::target_split_dimension, 400, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000035'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000001'::uuid, '5eed0000-0000-0000-0015-000000000020'::uuid, 'ORG'::target_split_dimension, 480, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000041'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000031'::uuid, 'PERIOD'::target_split_dimension, 520, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000042'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000031'::uuid, 'PERIOD'::target_split_dimension, 560, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000043'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000031'::uuid, 'PERIOD'::target_split_dimension, 650, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000044'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000032'::uuid, 'PERIOD'::target_split_dimension, 230, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000045'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000032'::uuid, 'PERIOD'::target_split_dimension, 240, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000046'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000032'::uuid, 'PERIOD'::target_split_dimension, 300, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000047'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000033'::uuid, 'PERIOD'::target_split_dimension, 200, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000048'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000034'::uuid, 'PERIOD'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000049'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000033'::uuid, 'PERIOD'::target_split_dimension, 230, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000004a'::uuid, '5eed0000-0000-0000-0005-000000000003'::uuid, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000034'::uuid, 'PERIOD'::target_split_dimension, 120, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000004b'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000035'::uuid, 'PERIOD'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000004c'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000035'::uuid, 'PERIOD'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000004d'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000035'::uuid, 'PERIOD'::target_split_dimension, 130, 'DRAFT'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000051'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000041'::uuid, 'ORG'::target_split_dimension, 270, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000080'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000051'::uuid, 'ORG'::target_split_dimension, 140, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000081'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000051'::uuid, 'ORG'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000052'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000041'::uuid, 'ORG'::target_split_dimension, 250, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000082'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000052'::uuid, 'ORG'::target_split_dimension, 120, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000083'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000052'::uuid, 'ORG'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000053'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000044'::uuid, 'ORG'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000084'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000053'::uuid, 'ORG'::target_split_dimension, 70, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000085'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000053'::uuid, 'ORG'::target_split_dimension, 60, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000054'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000044'::uuid, 'ORG'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000086'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-000000000054'::uuid, 'ORG'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000055'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000042'::uuid, 'ORG'::target_split_dimension, 300, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000087'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000055'::uuid, 'ORG'::target_split_dimension, 160, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000088'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000055'::uuid, 'ORG'::target_split_dimension, 140, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000056'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000042'::uuid, 'ORG'::target_split_dimension, 260, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000089'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000056'::uuid, 'ORG'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008a'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000056'::uuid, 'ORG'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000057'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000045'::uuid, 'ORG'::target_split_dimension, 140, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008b'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000057'::uuid, 'ORG'::target_split_dimension, 75, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008c'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000057'::uuid, 'ORG'::target_split_dimension, 65, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000058'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000045'::uuid, 'ORG'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008d'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-000000000058'::uuid, 'ORG'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000059'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000043'::uuid, 'ORG'::target_split_dimension, 340, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008e'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000059'::uuid, 'ORG'::target_split_dimension, 170, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000008f'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000059'::uuid, 'ORG'::target_split_dimension, 170, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000005a'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-000000000043'::uuid, 'ORG'::target_split_dimension, 310, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000090'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-00000000005a'::uuid, 'ORG'::target_split_dimension, 150, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000091'::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '5eed0000-0000-0000-000f-00000000000a'::uuid, '5eed0000-0000-0000-0035-00000000005a'::uuid, 'ORG'::target_split_dimension, 160, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000005b'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000004b'::uuid, 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000092'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000005b'::uuid, 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000093'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000005b'::uuid, 'ORG'::target_split_dimension, 27, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000005c'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000004b'::uuid, 'ORG'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000094'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000005c'::uuid, 'ORG'::target_split_dimension, 22, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000095'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '5eed0000-0000-0000-000f-000000000008'::uuid, '5eed0000-0000-0000-0035-00000000005c'::uuid, 'ORG'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000005d'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000004c'::uuid, 'ORG'::target_split_dimension, 60, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000096'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000005d'::uuid, 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000097'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000005d'::uuid, 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-00000000005e'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000004c'::uuid, 'ORG'::target_split_dimension, 50, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000098'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000005e'::uuid, 'ORG'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status),
    ('5eed0000-0000-0000-0035-000000000099'::uuid, '5eed0000-0000-0000-0005-000000000002'::uuid, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '5eed0000-0000-0000-000f-000000000009'::uuid, '5eed0000-0000-0000-0035-00000000005e'::uuid, 'ORG'::target_split_dimension, 24, 'ACTIVE'::target_assignment_status)
) AS t(id, kpi_id, dept_id, emp_id, period_id, parent_id, split, target_value, status)
ON CONFLICT DO NOTHING;

-- The rows seeded earlier join the same tree: the HDV Q4 chain hangs under the HDV year, the DU_NO and KH_MOI
-- years are drafts, and the DU_NO quarters and departments get uneven values.
-- DU_NO is authored in tỷ VND; the scale keeps these values right when the seed re-runs after convert_ty_vnd_kpis_to_vnd.sql.
UPDATE target_assignment SET parent_assignment_id = '5eed0000-0000-0000-0035-000000000001', split_dimension = 'PERIOD' WHERE id = '5eed0000-0000-0000-0015-000000000001';
UPDATE target_assignment SET parent_assignment_id = '5eed0000-0000-0000-0035-000000000031', split_dimension = 'PERIOD' WHERE id = '5eed0000-0000-0000-0015-000000000002';
UPDATE target_assignment SET parent_assignment_id = '5eed0000-0000-0000-0035-000000000032', split_dimension = 'PERIOD' WHERE id = '5eed0000-0000-0000-0015-000000000003';
UPDATE target_assignment SET status = 'DRAFT', approved_at = NULL WHERE id IN ('5eed0000-0000-0000-0015-000000000010', '5eed0000-0000-0000-0015-000000000020');
UPDATE target_assignment SET parent_assignment_id = '5eed0000-0000-0000-0035-000000000033', split_dimension = 'PERIOD', target_value = 190 * (SELECT CASE unit WHEN 'tỷ VND' THEN 1::numeric ELSE 1000000000::numeric END FROM kpi_definition WHERE code = 'DU_NO') WHERE id = '5eed0000-0000-0000-0015-000000000015';
UPDATE target_assignment SET parent_assignment_id = '5eed0000-0000-0000-0035-000000000034', split_dimension = 'PERIOD', target_value = 60 * (SELECT CASE unit WHEN 'tỷ VND' THEN 1::numeric ELSE 1000000000::numeric END FROM kpi_definition WHERE code = 'DU_NO') WHERE id = '5eed0000-0000-0000-0015-000000000016';
UPDATE target_assignment SET target_value = v.value * (SELECT CASE unit WHEN 'tỷ VND' THEN 1::numeric ELSE 1000000000::numeric END FROM kpi_definition WHERE code = 'DU_NO')
FROM (VALUES ('5eed0000-0000-0000-0015-000000000011'::uuid, 250), ('5eed0000-0000-0000-0015-000000000012'::uuid, 300), ('5eed0000-0000-0000-0015-000000000013'::uuid, 350), ('5eed0000-0000-0000-0015-000000000014'::uuid, 300)) AS v(id, value)
WHERE target_assignment.id = v.id;

-- Employee feedback on the Q3 HDV targets: one confirmed, one commented.
INSERT INTO target_assignment_feedback (id, target_assignment_id, employee_id, type, comment, created_at)
SELECT f.id::uuid, ta.id, ta.employee_id, f.type::target_assignment_feedback_type, f.comment, now()
FROM (VALUES
    ('5eed0000-0000-0000-0036-000000000001', '5eed0000-0000-0000-0035-00000000008e', 'CONFIRM', NULL),
    ('5eed0000-0000-0000-0036-000000000002', '5eed0000-0000-0000-0035-000000000090', 'COMMENT', 'Chỉ tiêu Q3 cao hơn Q2 gần 15%, đề nghị xem lại.')
) AS f(id, target_id, type, comment)
JOIN target_assignment ta ON ta.id = f.target_id::uuid
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2026 Q1-Q3 detail: every employee quarter target split by month (HDV, KH_MOI, DU_NO), the teams/employees still
-- missing in Q3 and below the DU_NO departments, and progress for Jan-Aug. September onwards is progress of the
-- evaluation sample below, which reuses these month targets. Oanh has no DU_NO target (N/A sample).
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       now(), CASE WHEN t.status = 'ACTIVE' THEN now() END, now(), per.start_date
FROM (VALUES
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000004-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000004-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000004-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000005-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000005-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000005-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000006-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000006-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000006-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000007-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000007-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000007-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000008-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000008-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000008-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000009-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000009-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000009-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-00000000000a-2026-Q1', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-00000000000a-2026-Q1', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-00000000000a-2026-Q1', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000004-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000004-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000004-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000005-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000005-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000005-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000006-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000006-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000006-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000007-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000007-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000007-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000008-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000008-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000008-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000009-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000009-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000009-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-00000000000a-2026-Q2', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-00000000000a-2026-Q2', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-00000000000a-2026-Q2', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000004-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000004-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000004-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000005-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000005-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000005-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000006-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000006-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000006-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000007-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000007-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000007-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000008-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000008-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000008-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-000000000009-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-000000000009-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-000000000009-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-CSAT--5eed0000-0000-0000-0007-00000000000a-2026-Q3', 'CSAT', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3', NULL::target_split_dimension, 4.5, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TY_LE_LOI--5eed0000-0000-0000-0007-00000000000a-2026-Q3', 'TY_LE_LOI', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3', NULL::target_split_dimension, 2, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2026-TUAN_THU--5eed0000-0000-0000-0007-00000000000a-2026-Q3', 'TUAN_THU', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3', NULL::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text)
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       now(), CASE WHEN t.status = 'ACTIVE' THEN now() END, now(), per.start_date
FROM (VALUES
    ('2026-HDV-5eed0000-0000-0000-0001-000000000006--2026-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 180, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-HDV-5eed0000-0000-0000-0001-000000000007--2026-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 120, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH-5eed0000-0000-0000-0001-000000000004--2026-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 70, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH-5eed0000-0000-0000-0001-000000000005--2026-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 60, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000004--2026-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q1', 'ORG'::target_split_dimension, 98, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000005--2026-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q1', 'ORG'::target_split_dimension, 92, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000006--2026-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q1', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000007--2026-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2026-Q1', 'ORG'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000004--2026-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q2', 'ORG'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000005--2026-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q2', 'ORG'::target_split_dimension, 90, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000006--2026-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q2', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000007--2026-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2026-Q2', 'ORG'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000004--2026-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 115, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000005--2026-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 115, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000006--2026-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 69, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO-5eed0000-0000-0000-0001-000000000007--2026-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2026-Q3', 'ORG'::target_split_dimension, 51, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2026-Q3')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       now(), CASE WHEN t.status = 'ACTIVE' THEN now() END, now(), per.start_date
FROM (VALUES
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 85, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 120, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 36, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 50, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 48, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1', 'ORG'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 57, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 53, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 48, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2', 'ORG'::target_split_dimension, 25, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 59, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 56, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 54, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 61, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2026-Q3')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       now(), CASE WHEN t.status = 'ACTIVE' THEN now() END, now(), per.start_date
FROM (VALUES
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 46, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 51, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 40, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 48, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 44, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 40, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 48, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 36, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 6, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 56, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 46, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 24, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 25, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 22, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 22, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 35, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 56, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000004-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 62, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 56, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000005-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 62, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 46, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000006-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000007-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 59, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 29, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000008-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 35, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-000000000009-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3'),
    ('2026-HDV--5eed0000-0000-0000-0007-00000000000a-2026-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 44, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000004-2026-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000005-2026-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000006-2026-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-KH--5eed0000-0000-0000-0007-000000000007-2026-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 5, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 5, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-01', 'PERIOD'::target_split_dimension, 4, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-02', 'PERIOD'::target_split_dimension, 5, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-03', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q1'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 20, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-04', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-05', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q2'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000004-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 22, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000005-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000006-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 20, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000007-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000008-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-08', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3'),
    ('2026-DU_NO--5eed0000-0000-0000-0007-000000000009-2026-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2026-Q3')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

-- Progress for the 2026 employee months seeded above (Jan-Aug; September onwards comes from the evaluation sample below).
-- Fixed per-row factor 0.78-1.18, not random, so every run gives the same data.
INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, status)
SELECT md5('progest-actual-2026-' || ta.id::text)::uuid,
       ta.id,
       (p.start_date + 14),
       round(ta.target_value * (0.78 + (abs(hashtext(ta.id::text)) % 41) / 100.0), 0),
       'VALID'::actual_progress_status
FROM target_assignment ta
JOIN evaluation_period p ON p.id = ta.evaluation_period_id AND p.period_type = 'MONTH'
JOIN kpi_definition k ON k.id = ta.kpi_definition_id AND k.code IN ('HDV', 'KH_MOI', 'DU_NO')
WHERE ta.employee_id IS NOT NULL
  AND p.start_date >= '2026-01-01' AND p.start_date < '2026-09-01'
ON CONFLICT DO NOTHING;

-- Progress of the standalone quality targets (CSAT, error rate, compliance): one value per month, July and August
-- only in Q3 (September comes from the evaluation sample). RATIO rows use denominator 100 like the sample does.
INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, denominator, status)
SELECT md5('progest-actual-quality-' || ta.id::text || '-' || m.n::text)::uuid,
       ta.id,
       ((p.start_date + (m.n * interval '1 month'))::date + 14),
       CASE k.code
           WHEN 'CSAT' THEN LEAST(5, round(ta.target_value * (0.85 + (abs(hashtext(ta.id::text || m.n::text)) % 25) / 100.0), 2))
           WHEN 'TY_LE_LOI' THEN round(ta.target_value * (0.7 + (abs(hashtext(ta.id::text || m.n::text)) % 60) / 100.0), 2)
           ELSE LEAST(100, round(ta.target_value * (0.9 + (abs(hashtext(ta.id::text || m.n::text)) % 11) / 100.0), 2))
       END,
       CASE WHEN k.aggregation_type = 'RATIO' THEN 100::numeric END,
       'VALID'::actual_progress_status
FROM target_assignment ta
JOIN evaluation_period p ON p.id = ta.evaluation_period_id AND p.period_type = 'QUARTER'
JOIN kpi_definition k ON k.id = ta.kpi_definition_id AND k.code IN ('CSAT', 'TY_LE_LOI', 'TUAN_THU')
CROSS JOIN generate_series(0, 2) AS m(n)
WHERE ta.employee_id IS NOT NULL AND p.start_date >= '2026-01-01' AND p.start_date < '2026-10-01'
  AND NOT (p.code = '2026-Q3' AND m.n = 2)
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Month-level employee targets: each Q4 employee target (rows ...08-...0b HDV, ...0c-...0d KH_MOI) split by month
-- (Oct/Nov/Dec) so the evaluation screen has targets whichever Q4 period is picked.
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT ('5eed0000-0000-0000-0015-' || lpad(to_hex(256 + p.parent_seq * 3 + m.month_idx), 12, '0'))::uuid,
       parent.business_objective_id,
       parent.kpi_definition_id,
       NULL::uuid,
       parent.employee_id,
       ('5eed0000-0000-0000-000f-00000000000' || (3 + m.month_idx))::uuid,
       parent.id,
       'PERIOD'::target_split_dimension,
       CASE WHEN m.month_idx < 3 THEN floor(parent.target_value / 3) ELSE parent.target_value - 2 * floor(parent.target_value / 3) END,
       'ACTIVE'::target_assignment_status,
       now(),
       now(),
       NULL,
       (SELECT start_date FROM evaluation_period WHERE id = ('5eed0000-0000-0000-000f-00000000000' || (3 + m.month_idx))::uuid)
FROM (VALUES
    ('000000000008', 0), ('000000000009', 1), ('00000000000a', 2), ('00000000000b', 3),
    ('00000000000c', 4), ('00000000000d', 5)
) AS p(parent_no, parent_seq)
JOIN target_assignment parent ON parent.id = ('5eed0000-0000-0000-0015-' || p.parent_no)::uuid
CROSS JOIN (VALUES (1), (2), (3)) AS m(month_idx)
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Employee target confirm / comment (SCR-STRAT-05 "Phản hồi", SCR-STRAT-03-D1).
--   Q4 HDV: Châu confirmed, Dũng commented, Khang and Mai not viewed yet (no row).
--   Demo Employee (employee@bpms.com) gets a standalone KH_MOI Q4 target with one comment so the
--   employee login has data for GET /target-assignments/mine.
-- Feedback rows are only inserted when the target exists and belongs to that employee, so a database where the
-- target was created another way (one ACTIVE target per KPI/period/employee) still seeds cleanly.
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason, effective_from)
SELECT '5eed0000-0000-0000-0015-00000000000f', '5eed0000-0000-0000-0010-000000000001', '5eed0000-0000-0000-0005-000000000002',
        NULL, '5eed0000-0000-0000-0007-00000000000c', '5eed0000-0000-0000-000f-000000000003', NULL, NULL,
        30, 'ACTIVE', now(), now(), 'Chỉ tiêu KH mới Q4 (demo)',
        p.start_date
FROM evaluation_period p WHERE p.id = '5eed0000-0000-0000-000f-000000000003'
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment_feedback (id, target_assignment_id, employee_id, type, comment, created_at)
SELECT f.id::uuid, ta.id, ta.employee_id, f.type::target_assignment_feedback_type, f.comment, now()
FROM (VALUES
    ('5eed0000-0000-0000-0030-000000000001', '5eed0000-0000-0000-0015-000000000008', '5eed0000-0000-0000-0007-000000000004', 'CONFIRM', NULL),
    ('5eed0000-0000-0000-0030-000000000002', '5eed0000-0000-0000-0015-000000000009', '5eed0000-0000-0000-0007-000000000005', 'COMMENT', 'Tháng 11 tôi nghỉ phép 5 ngày, xin giảm chỉ tiêu tháng 11.'),
    ('5eed0000-0000-0000-0030-000000000003', '5eed0000-0000-0000-0015-00000000000f', '5eed0000-0000-0000-0007-00000000000c', 'COMMENT', 'Đề nghị chia lại chỉ tiêu theo số ngày đi làm thực tế.')
) AS f(id, target_id, employee_id, type, comment)
JOIN target_assignment ta ON ta.id = f.target_id::uuid AND ta.employee_id = f.employee_id::uuid
ON CONFLICT DO NOTHING;



-- ---------------------------------------------------------------------------
-- 2026 branch targets and quarters for KPI_* allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md quarter-split):
-- GetQuartersAsync's previousYear / previousYearActual read 2026 ACTIVE branch target_assignment rows (root +
-- PERIOD quarter children) and VALID actual_progress summed over the 2026-root's descendant tree. Only SUM
-- KPI_* are period-additive (IsPeriodAdditive), so only SUM KPI_* get 2026 history here; AVG/NO_AGGREGATION
-- KPI_* (KPI_CASA_RATIO, KPI_ACTIVE_CUST, KPI_CROSS_SELL, KPI_CHURN, KPI_CSAT, KPI_1STAR_RATE, KPI_SERVE_TIME,
-- KPI_COMPLAINT_SLA, KPI_TXN_ERR_RATE, KPI_HANDOVER_SLA, KPI_TRAINING) are untouched.
-- The 2026 root value is a standard branch-year value scaled down ~15%, split into 4
-- quarters with a seasonal shape (20/23/25/32%, Q4 takes the remainder) that sums exactly to the root, as the
-- deferred trg_validate_target_children_sum constraint requires for an ACTIVE SUM KPI.
-- Of the 16 SUM KPI_* codes (KPI_RISK_SEVERE's value is 0, so it is skipped):
--   - 12 get targets AND VALID actual_progress on the quarter children, with a different per-month shape
--     (weights 30/33/37 scaled by a per-row 0.85-1.25 factor, deterministic via hashtext) so actual-vs-target
--     is visibly different: KPI_NFI, KPI_FEE_CARD, KPI_FEE_EBANK, KPI_INSURANCE, KPI_DEP_NET, KPI_CASA,
--     KPI_TERM_DEP, KPI_ACC_OPEN, KPI_NEW_CUST, KPI_REFERRAL, KPI_CARD_DEBIT, KPI_CARD_CREDIT.
--   - 3 get targets only (no actual_progress), so quarter-split falls back to "last year's targets" with no
--     actual: KPI_EBANK_ACTIVE, KPI_INSURANCE_CNT, KPI_AUTO_SAVING.
--   - 1 gets nothing at all, so quarter-split falls back to an even split: KPI_TXN_VOL.
-- Nothing here touches the 2026 evaluation sample: KPI_* is not in EP-SALES/EP-TELLER (the 2026 profiles), only
-- in EP-SALES-YEARKPI/EP-TELLER-YEARKPI.
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE seed_2026_kpi_hist (kpi_code, year_value, with_actuals) ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('KPI_NFI',           5100000000::numeric, true),
    ('KPI_FEE_CARD',      1530000000::numeric, true),
    ('KPI_FEE_EBANK',     1275000000::numeric, true),
    ('KPI_INSURANCE',     1020000000::numeric, true),
    ('KPI_DEP_NET',     255000000000::numeric, true),
    ('KPI_CASA',        102000000000::numeric, true),
    ('KPI_TERM_DEP',    212500000000::numeric, true),
    ('KPI_ACC_OPEN',            2040::numeric, true),
    ('KPI_NEW_CUST',            1700::numeric, true),
    ('KPI_REFERRAL',             510::numeric, true),
    ('KPI_CARD_DEBIT',          1020::numeric, true),
    ('KPI_CARD_CREDIT',          255::numeric, true),
    ('KPI_EBANK_ACTIVE',        1054::numeric, true),
    ('KPI_INSURANCE_CNT',        130::numeric, true),
    ('KPI_AUTO_SAVING',          680::numeric, true),
    ('KPI_LOAN_DISB',      85000000000::numeric, true),
    ('KPI_FX_FEE',           765000000::numeric, true),
    ('KPI_PAYROLL_ACC',            340::numeric, true),
    ('KPI_DOC_PROCESSED',          4250::numeric, true),
    ('KPI_CASH_TXN',              18700::numeric, true)
) AS v(kpi_code, year_value, with_actuals);
-- KPI_TXN_VOL (SUM) and KPI_RISK_SEVERE (SUM, sample value 0) intentionally get no row here, so quarter-split
-- falls back to an even split for them.

-- Root: 2026 branch-year ACTIVE row per KPI above (DepartmentId = branch, no parent, SplitDimension = NULL).
INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2026-kpi-hist-year-' || h.kpi_code)::uuid,
       k.id, '5eed0000-0000-0000-0001-000000000001'::uuid, yp.id, NULL::uuid,
       NULL::target_split_dimension, h.year_value, 'ACTIVE'::target_assignment_status,
       make_timestamp(2026, 1, 1, 8, 0, 0), make_timestamp(2026, 1, 15, 17, 0, 0), make_timestamp(2026, 1, 1, 8, 0, 0), yp.start_date
FROM seed_2026_kpi_hist h
JOIN kpi_definition k ON k.code = h.kpi_code
JOIN evaluation_period yp ON yp.code = '2026'
ON CONFLICT DO NOTHING;

-- Quarters: seasonal shape 20/23/25/32%, split_dimension PERIOD, parent = the root above. Q4 takes the
-- remainder so the 4 quarters sum exactly to the root (required for an ACTIVE SUM KPI).
INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2026-kpi-hist-q' || q.quarter_no || '-' || h.kpi_code)::uuid,
       k.id, '5eed0000-0000-0000-0001-000000000001'::uuid, qp.id, root.id,
       'PERIOD'::target_split_dimension,
       CASE q.quarter_no
           WHEN 1 THEN round(h.year_value * 0.20)
           WHEN 2 THEN round(h.year_value * 0.23)
           WHEN 3 THEN round(h.year_value * 0.25)
           ELSE h.year_value - round(h.year_value * 0.20) - round(h.year_value * 0.23) - round(h.year_value * 0.25)
       END,
       'ACTIVE'::target_assignment_status,
       make_timestamp(2026, 1, 1, 8, 0, 0), make_timestamp(2026, 1, 15, 17, 0, 0), make_timestamp(2026, 1, 1, 8, 0, 0), qp.start_date
FROM seed_2026_kpi_hist h
JOIN kpi_definition k ON k.code = h.kpi_code
JOIN target_assignment root ON root.id = md5('seed-2026-kpi-hist-year-' || h.kpi_code)::uuid
CROSS JOIN (VALUES (1, '2026-Q1'), (2, '2026-Q2'), (3, '2026-Q3'), (4, '2026-Q4')) AS q(quarter_no, period_code)
JOIN evaluation_period qp ON qp.code = q.period_code
ON CONFLICT DO NOTHING;


-- ---------------------------------------------------------------------------
-- 2025 history for 2026 KPI_* allocation (GetQuartersAsync previousYear / previousYearActual):
-- 2025 root is 2026 root scaled down ~15%, split into 4 quarters (20/23/25/32%), with VALID actual_progress.
-- ---------------------------------------------------------------------------
-- 2025 Root: branch-year ACTIVE row per KPI
INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2025-kpi-hist-year-' || h.kpi_code)::uuid,
       k.id, '5eed0000-0000-0000-0001-000000000001'::uuid, yp.id, NULL::uuid,
       NULL::target_split_dimension, round(h.year_value * 0.85), 'ACTIVE'::target_assignment_status,
       make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0), yp.start_date
FROM seed_2026_kpi_hist h
JOIN kpi_definition k ON k.code = h.kpi_code
JOIN evaluation_period yp ON yp.code = '2025'
ON CONFLICT DO NOTHING;

-- 2025 Quarters: seasonal shape 20/23/25/32%
INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2025-kpi-hist-q' || q.quarter_no || '-' || h.kpi_code)::uuid,
       k.id, '5eed0000-0000-0000-0001-000000000001'::uuid, qp.id, root.id,
       'PERIOD'::target_split_dimension,
       CASE q.quarter_no
           WHEN 1 THEN round(root.target_value * 0.20)
           WHEN 2 THEN round(root.target_value * 0.23)
           WHEN 3 THEN round(root.target_value * 0.25)
           ELSE root.target_value - round(root.target_value * 0.20) - round(root.target_value * 0.23) - round(root.target_value * 0.25)
       END,
       'ACTIVE'::target_assignment_status,
       make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0), qp.start_date
FROM seed_2026_kpi_hist h
JOIN kpi_definition k ON k.code = h.kpi_code
JOIN target_assignment root ON root.id = md5('seed-2025-kpi-hist-year-' || h.kpi_code)::uuid
CROSS JOIN (VALUES (1, '2025-Q1'), (2, '2025-Q2'), (3, '2025-Q3'), (4, '2025-Q4')) AS q(quarter_no, period_code)
JOIN evaluation_period qp ON qp.code = q.period_code
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2025 + 2026 department / employee split and ACTUALs for every KPI_* history row above.
-- A published year is split all the way down: each root -> every department with an eligible employee (ORG) ->
-- every eligible active employee of that department (ORG) -> that employee's four quarters (PERIOD). Every level
-- sums exactly to its parent (the remainder goes to the last unit). Eligibility = the position's ACTIVE
-- EP-*-YEARKPI profile item, the same rule the allocation services use (employees directly in the department).
-- ACTUAL progress lives only on the employee year rows (one VALID row per month-end), so the branch, department and
-- employee suggestion all read the same total when they walk the tree; quarters come from the progress date.
-- ---------------------------------------------------------------------------
-- Earlier seeds put actuals on the branch quarter rows and two TERM_DEP department rows: drop them so nothing is counted twice.
DELETE FROM actual_progress
WHERE target_assignment_id IN (
    SELECT q.id FROM target_assignment q
    WHERE q.parent_assignment_id IN (SELECT md5('seed-' || y.n || '-kpi-hist-year-' || h.kpi_code)::uuid
                                     FROM seed_2026_kpi_hist h CROSS JOIN (VALUES (2025), (2026)) AS y(n)))
   OR target_assignment_id IN (SELECT md5('seed-' || y.n || '-kpi-hist-dept-' || d.c)::uuid
                               FROM (VALUES (2025), (2026)) AS y(n) CROSS JOIN (VALUES ('CN-HN-KD'), ('CN-HN-KHCN')) AS d(c));
DELETE FROM target_assignment
WHERE id IN (SELECT md5('seed-' || y.n || '-kpi-hist-dept-' || d.c)::uuid
             FROM (VALUES (2025), (2026)) AS y(n) CROSS JOIN (VALUES ('CN-HN-KD'), ('CN-HN-KHCN')) AS d(c));

CREATE TEMP TABLE seed_hist_year (year_no, period_code) ON COMMIT DROP AS
SELECT * FROM (VALUES (2025, '2025'), (2026, '2026')) AS v(year_no, period_code);

CREATE TEMP TABLE seed_hist_elig ON COMMIT DROP AS
SELECT DISTINCT h.kpi_code, k.id AS kpi_id, d.id AS dept_id, d.code AS dept_code, e.id AS emp_id, e.employee_code
FROM seed_2026_kpi_hist h
JOIN kpi_definition k ON k.code = h.kpi_code
JOIN evaluation_profile_item i ON i.kpi_definition_id = k.id
JOIN evaluation_profile p ON p.id = i.evaluation_profile_id AND p.status = 'ACTIVE' AND p.code LIKE 'EP-%-YEARKPI'
JOIN employee e ON e.job_position_id = p.job_position_id AND e.status = 'ACTIVE'
JOIN department d ON d.id = e.department_id AND d.level_type = 'DEPARTMENT'
                 AND d.parent_department_id = '5eed0000-0000-0000-0001-000000000001';

CREATE TEMP TABLE seed_hist_dept ON COMMIT DROP AS
WITH w AS (
    SELECT y.year_no, el.kpi_code, el.kpi_id, el.dept_id, el.dept_code,
           root.id AS root_id, root.target_value AS root_value, root.evaluation_period_id, root.effective_from,
           count(*) * (0.8 + (abs(hashtext(el.kpi_code || el.dept_code || y.year_no::text)) % 41) / 100.0) AS weight
    FROM seed_hist_elig el
    CROSS JOIN seed_hist_year y
    JOIN target_assignment root ON root.id = md5('seed-' || y.year_no || '-kpi-hist-year-' || el.kpi_code)::uuid
    GROUP BY y.year_no, el.kpi_code, el.kpi_id, el.dept_id, el.dept_code, root.id, root.target_value, root.evaluation_period_id, root.effective_from
), r AS (
    SELECT w.*,
           row_number() OVER (PARTITION BY year_no, kpi_code ORDER BY dept_code) AS rn,
           count(*) OVER (PARTITION BY year_no, kpi_code) AS cnt,
           round(root_value * weight / sum(weight) OVER (PARTITION BY year_no, kpi_code)) AS part
    FROM w
)
SELECT year_no, kpi_code, kpi_id, dept_id, dept_code, root_id, evaluation_period_id, effective_from,
       md5('seed-' || year_no || '-kpi-hist-dept-' || kpi_code || '-' || dept_code)::uuid AS id,
       CASE WHEN rn = cnt
            THEN root_value - coalesce(sum(part) OVER (PARTITION BY year_no, kpi_code ORDER BY rn ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
            ELSE part END AS value
FROM r;

INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT d.id, d.kpi_id, d.dept_id, d.evaluation_period_id, d.root_id, 'ORG'::target_split_dimension, d.value,
       'ACTIVE'::target_assignment_status,
       make_timestamp(d.year_no, 1, 1, 8, 0, 0), make_timestamp(d.year_no, 1, 15, 17, 0, 0), make_timestamp(d.year_no, 1, 1, 8, 0, 0), d.effective_from
FROM seed_hist_dept d
ON CONFLICT DO NOTHING;

CREATE TEMP TABLE seed_hist_emp ON COMMIT DROP AS
WITH w AS (
    SELECT d.year_no, d.kpi_code, d.kpi_id, d.dept_code, d.id AS dept_row_id, d.value AS dept_value,
           d.evaluation_period_id, d.effective_from, el.emp_id, el.employee_code,
           0.8 + (abs(hashtext(d.kpi_code || el.employee_code || d.year_no::text)) % 41) / 100.0 AS weight
    FROM seed_hist_dept d
    JOIN seed_hist_elig el ON el.kpi_code = d.kpi_code AND el.dept_code = d.dept_code
), r AS (
    SELECT w.*,
           row_number() OVER (PARTITION BY year_no, kpi_code, dept_code ORDER BY employee_code) AS rn,
           count(*) OVER (PARTITION BY year_no, kpi_code, dept_code) AS cnt,
           round(dept_value * weight / sum(weight) OVER (PARTITION BY year_no, kpi_code, dept_code)) AS part
    FROM w
)
SELECT year_no, kpi_code, kpi_id, emp_id, dept_row_id, evaluation_period_id, effective_from,
       md5('seed-' || year_no || '-kpi-hist-emp-' || kpi_code || '-' || employee_code)::uuid AS id,
       CASE WHEN rn = cnt
            THEN dept_value - coalesce(sum(part) OVER (PARTITION BY year_no, kpi_code, dept_code ORDER BY rn ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
            ELSE part END AS value
FROM r;

INSERT INTO target_assignment (id, kpi_definition_id, employee_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT e.id, e.kpi_id, e.emp_id, e.evaluation_period_id, e.dept_row_id, 'ORG'::target_split_dimension, e.value,
       'ACTIVE'::target_assignment_status,
       make_timestamp(e.year_no, 1, 1, 8, 0, 0), make_timestamp(e.year_no, 1, 15, 17, 0, 0), make_timestamp(e.year_no, 1, 1, 8, 0, 0), e.effective_from
FROM seed_hist_emp e
ON CONFLICT DO NOTHING;

-- Quarters below the year rows. The branch quarters (20/23/25/32%) are the anchor: for Q1-Q3 each department takes
-- its share of the branch quarter and the last department takes the rest, so the department quarters add up to the
-- branch quarter ("Còn lại 0"); Q4 is the department year minus Q1-Q3. Employees repeat this inside their department.
DELETE FROM target_assignment
WHERE split_dimension = 'PERIOD'
  AND parent_assignment_id IN (SELECT id FROM seed_hist_emp UNION SELECT id FROM seed_hist_dept);

CREATE TEMP TABLE seed_hist_dept_q ON COMMIT DROP AS
WITH base AS (
    SELECT d.year_no, d.kpi_code, d.kpi_id, d.dept_code, d.id AS dept_row_id, d.value AS dept_value, q.n,
           bq.target_value AS branch_q, root.target_value AS root_value, qp.id AS period_id, qp.start_date,
           row_number() OVER (PARTITION BY d.year_no, d.kpi_code, q.n ORDER BY d.dept_code) AS rn,
           count(*) OVER (PARTITION BY d.year_no, d.kpi_code, q.n) AS cnt
    FROM seed_hist_dept d
    CROSS JOIN (VALUES (1), (2), (3)) AS q(n)
    JOIN target_assignment root ON root.id = d.root_id
    JOIN evaluation_period qp ON qp.code = d.year_no || '-Q' || q.n
    JOIN target_assignment bq ON bq.parent_assignment_id = d.root_id AND bq.evaluation_period_id = qp.id AND bq.split_dimension = 'PERIOD'
), part AS (
    SELECT base.*, round(dept_value * branch_q / root_value) AS ideal FROM base
), q13 AS (
    SELECT year_no, kpi_code, kpi_id, dept_row_id, dept_value, n, period_id, start_date,
           CASE WHEN rn = cnt
                THEN branch_q - coalesce(sum(ideal) OVER (PARTITION BY year_no, kpi_code, n ORDER BY rn ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
                ELSE ideal END AS qvalue
    FROM part
)
SELECT year_no, kpi_code, kpi_id, dept_row_id, n, period_id, start_date, qvalue FROM q13
UNION ALL
SELECT s.year_no, s.kpi_code, s.kpi_id, s.dept_row_id, 4, qp.id, qp.start_date, s.dept_value - s.used
FROM (SELECT year_no, kpi_code, kpi_id, dept_row_id, max(dept_value) AS dept_value, sum(qvalue) AS used FROM q13 GROUP BY year_no, kpi_code, kpi_id, dept_row_id) s
JOIN evaluation_period qp ON qp.code = s.year_no || '-Q4';

INSERT INTO target_assignment (id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-' || q.year_no || '-kpi-hist-dept-q' || q.n || '-' || q.dept_row_id::text)::uuid,
       q.kpi_id, d.dept_id, q.period_id, q.dept_row_id, 'PERIOD'::target_split_dimension, q.qvalue,
       'ACTIVE'::target_assignment_status,
       make_timestamp(q.year_no, 1, 1, 8, 0, 0), make_timestamp(q.year_no, 1, 15, 17, 0, 0), make_timestamp(q.year_no, 1, 1, 8, 0, 0), q.start_date
FROM seed_hist_dept_q q
JOIN seed_hist_dept d ON d.id = q.dept_row_id
ON CONFLICT DO NOTHING;

CREATE TEMP TABLE seed_hist_emp_q ON COMMIT DROP AS
WITH base AS (
    SELECT e.year_no, e.kpi_code, e.kpi_id, e.emp_id, e.id AS emp_row_id, e.value AS emp_value, e.dept_row_id,
           dq.n, dq.qvalue AS dept_q, dq.period_id, dq.start_date, d.value AS dept_value,
           row_number() OVER (PARTITION BY e.dept_row_id, dq.n ORDER BY e.id) AS rn,
           count(*) OVER (PARTITION BY e.dept_row_id, dq.n) AS cnt
    FROM seed_hist_emp e
    JOIN seed_hist_dept d ON d.id = e.dept_row_id
    JOIN seed_hist_dept_q dq ON dq.dept_row_id = e.dept_row_id AND dq.n <= 3
), part AS (
    SELECT base.*, round(emp_value * dept_q / nullif(dept_value, 0)) AS ideal FROM base
), q13 AS (
    SELECT year_no, kpi_code, kpi_id, emp_id, emp_row_id, emp_value, n, period_id, start_date,
           CASE WHEN rn = cnt
                THEN dept_q - coalesce(sum(ideal) OVER (PARTITION BY dept_row_id, n ORDER BY rn ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
                ELSE ideal END AS qvalue
    FROM part
)
SELECT year_no, kpi_code, kpi_id, emp_id, emp_row_id, n, period_id, start_date, qvalue FROM q13
UNION ALL
SELECT s.year_no, s.kpi_code, s.kpi_id, s.emp_id, s.emp_row_id, 4, qp.id, qp.start_date, s.emp_value - s.used
FROM (SELECT year_no, kpi_code, kpi_id, emp_id, emp_row_id, max(emp_value) AS emp_value, sum(qvalue) AS used FROM q13 GROUP BY year_no, kpi_code, kpi_id, emp_id, emp_row_id) s
JOIN evaluation_period qp ON qp.code = s.year_no || '-Q4';

INSERT INTO target_assignment (id, kpi_definition_id, employee_id, evaluation_period_id, parent_assignment_id,
    split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-' || q.year_no || '-kpi-hist-emp-q' || q.n || '-' || q.emp_row_id::text)::uuid,
       q.kpi_id, q.emp_id, q.period_id, q.emp_row_id, 'PERIOD'::target_split_dimension, q.qvalue,
       'ACTIVE'::target_assignment_status,
       make_timestamp(q.year_no, 1, 1, 8, 0, 0), make_timestamp(q.year_no, 1, 15, 17, 0, 0), make_timestamp(q.year_no, 1, 1, 8, 0, 0), q.start_date
FROM seed_hist_emp_q q
ON CONFLICT DO NOTHING;

-- Actuals: one VALID row per month-end on each employee year row, a different shape than the targets
-- (per-row factor 0.85-1.25 via hashtext, deterministic) so actual-vs-target is visibly different.
INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, status)
SELECT md5('seed-' || e.year_no || '-kpi-hist-actual-' || e.id::text || '-' || m.n::text)::uuid,
       e.id,
       (make_date(e.year_no, m.n, 1) + interval '1 month' - interval '1 day')::date,
       round(e.value
             * (ARRAY[0.20, 0.23, 0.25, 0.32])[(m.n - 1) / 3 + 1]
             * (ARRAY[0.30, 0.33, 0.37])[(m.n - 1) % 3 + 1]
             * (0.85 + (abs(hashtext(e.id::text || m.n::text)) % 41) / 100.0), 0),
       'VALID'::actual_progress_status
FROM seed_hist_emp e
CROSS JOIN generate_series(1, 12) AS m(n)
ON CONFLICT DO NOTHING;


-- ---------------------------------------------------------------------------
-- Work items (open, ready for submission / verification flows)
-- ---------------------------------------------------------------------------
WITH seed_work (id, work_type_id, business_campaign_id, assignee_employee_id, code, title, description, priority, estimated_hours, planned_start_date, planned_end_date, status) AS (VALUES
 ('5eed0000-0000-0000-0016-000000000001', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000004', 'W-2026-0001', 'Mở tài khoản cho nhóm KH khu Cầu Giấy',  NULL, 'HIGH',     16,  '2026-10-01', '2026-12-31', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000002', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000004', 'W-2026-0002', 'Huy động tiền gửi KH hiện hữu',           NULL, 'MEDIUM',   24,  '2026-10-05', '2026-11-30', 'NEW'),
 ('5eed0000-0000-0000-0016-000000000003', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000006', 'W-2026-0003', 'Huy động tiền gửi hộ kinh doanh chợ Hôm', NULL, 'HIGH',     24,  '2026-10-01', '2026-11-15', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000004', '5eed0000-0000-0000-000c-000000000003', NULL,                                   '5eed0000-0000-0000-0007-000000000007', 'W-2026-0004', 'Giải ngân vay tiêu dùng hồ sơ A102',      NULL, 'CRITICAL', 8,   '2026-10-10', '2026-12-15', 'NEW'),
 ('5eed0000-0000-0000-0016-000000000005', '5eed0000-0000-0000-000c-000000000004', NULL,                                   '5eed0000-0000-0000-0007-000000000008', 'W-2026-0005', 'Giao dịch tại quầy tháng 10',             NULL, 'MEDIUM',   160, '2026-10-01', '2026-12-31', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000006', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000009', 'W-2026-0006', 'Mở tài khoản tại quầy cho KH vãng lai',   NULL, 'LOW',      40,  '2026-10-01', '2026-12-31', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000007', '5eed0000-0000-0000-000c-000000000001', NULL, '5eed0000-0000-0000-0007-00000000000c', 'W-2026-0007', 'Mở tài khoản cho KH khu Đống Đa', NULL, 'HIGH',   16, '2026-11-01', '2026-12-31', 'NEW'),
 ('5eed0000-0000-0000-0016-000000000008', '5eed0000-0000-0000-000c-000000000002', NULL, '5eed0000-0000-0000-0007-00000000000c', 'W-2026-0008', 'Huy động tiền gửi KH doanh nghiệp nhỏ', NULL, 'MEDIUM', 24, '2026-11-01', '2026-12-31', 'IN_PROGRESS')
)
INSERT INTO work (id, work_type_id, business_campaign_id, assignee_employee_id, created_by, code, title, description, priority, estimated_hours, planned_start_date, planned_end_date, status)
SELECT sw.id::uuid, sw.work_type_id::uuid, sw.business_campaign_id::uuid, sw.assignee_employee_id::uuid, COALESCE(e.manager_id, e.id),
       sw.code, sw.title, sw.description, sw.priority::work_priority, sw.estimated_hours,
       sw.planned_start_date::date, sw.planned_end_date::date, sw.status::work_status
FROM seed_work sw
JOIN employee e ON e.id = sw.assignee_employee_id::uuid
ON CONFLICT DO NOTHING;

-- ============================================================================
-- Phase 2: Seed data for 2025 (comparison year) and complete 2026 H1+Q1-Q3
-- ============================================================================

-- ---------------------------------------------------------------------------
-- 2025: Complete year (Status = CLOSED) for comparison
-- H1 -> Q1,Q2; H2 -> Q3,Q4; each quarter -> 3 months; each month -> all days
-- Note: WEEK rows use date range lookup; DAY rows point to MONTH (simpler approach)
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-002f-000000000001', '2025',    'Năm 2025',          'YEAR',      NULL,                                   '2025-01-01', '2025-12-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000002', '2025-H1', '6 tháng đầu 2025',  'HALF_YEAR', '5eed0000-0000-0000-002f-000000000001', '2025-01-01', '2025-06-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000003', '2025-H2', '6 tháng cuối 2025', 'HALF_YEAR', '5eed0000-0000-0000-002f-000000000001', '2025-07-01', '2025-12-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000004', '2025-Q1', 'Quý 1/2025',        'QUARTER',   '5eed0000-0000-0000-002f-000000000002', '2025-01-01', '2025-03-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000005', '2025-Q2', 'Quý 2/2025',        'QUARTER',   '5eed0000-0000-0000-002f-000000000002', '2025-04-01', '2025-06-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000006', '2025-Q3', 'Quý 3/2025',        'QUARTER',   '5eed0000-0000-0000-002f-000000000003', '2025-07-01', '2025-09-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000007', '2025-Q4', 'Quý 4/2025',        'QUARTER',   '5eed0000-0000-0000-002f-000000000003', '2025-10-01', '2025-12-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000008', '2025-01', 'Tháng 01/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000004', '2025-01-01', '2025-01-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000009', '2025-02', 'Tháng 02/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000004', '2025-02-01', '2025-02-28', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000a', '2025-03', 'Tháng 03/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000004', '2025-03-01', '2025-03-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000b', '2025-04', 'Tháng 04/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000005', '2025-04-01', '2025-04-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000c', '2025-05', 'Tháng 05/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000005', '2025-05-01', '2025-05-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000d', '2025-06', 'Tháng 06/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000005', '2025-06-01', '2025-06-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000e', '2025-07', 'Tháng 07/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000006', '2025-07-01', '2025-07-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-00000000000f', '2025-08', 'Tháng 08/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000006', '2025-08-01', '2025-08-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000010', '2025-09', 'Tháng 09/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000006', '2025-09-01', '2025-09-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000011', '2025-10', 'Tháng 10/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000007', '2025-10-01', '2025-10-31', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000012', '2025-11', 'Tháng 11/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000007', '2025-11-01', '2025-11-30', 'CLOSED'),
 ('5eed0000-0000-0000-002f-000000000013', '2025-12', 'Tháng 12/2025',     'MONTH',     '5eed0000-0000-0000-002f-000000000007', '2025-12-01', '2025-12-31', 'CLOSED')
ON CONFLICT DO NOTHING;

-- Add all DAY rows for 2025
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status)
SELECT md5('progest-seed-day-' || d::date)::uuid,
       'D' || to_char(d, 'YYYY-MM-DD'),
       'Ngày ' || to_char(d, 'DD/MM/YYYY'),
       'DAY',
       CASE EXTRACT(MONTH FROM d)
           WHEN 1 THEN '5eed0000-0000-0000-002f-000000000008'::uuid
           WHEN 2 THEN '5eed0000-0000-0000-002f-000000000009'::uuid
           WHEN 3 THEN '5eed0000-0000-0000-002f-00000000000a'::uuid
           WHEN 4 THEN '5eed0000-0000-0000-002f-00000000000b'::uuid
           WHEN 5 THEN '5eed0000-0000-0000-002f-00000000000c'::uuid
           WHEN 6 THEN '5eed0000-0000-0000-002f-00000000000d'::uuid
           WHEN 7 THEN '5eed0000-0000-0000-002f-00000000000e'::uuid
           WHEN 8 THEN '5eed0000-0000-0000-002f-00000000000f'::uuid
           WHEN 9 THEN '5eed0000-0000-0000-002f-000000000010'::uuid
           WHEN 10 THEN '5eed0000-0000-0000-002f-000000000011'::uuid
           WHEN 11 THEN '5eed0000-0000-0000-002f-000000000012'::uuid
           ELSE '5eed0000-0000-0000-002f-000000000013'::uuid
       END,
       d::date, d::date, 'CLOSED'
FROM generate_series('2025-01-01'::date, '2025-12-31'::date, interval '1 day') AS d
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- WEEK periods for both 2025 and 2026
-- Code format: W2025-10-1 (year-month-weekIndex), one WEEK per (week ∩ month)
-- Parent: the MONTH period containing the Monday of that week
-- Simpler approach: DAY rows point to MONTH; service looks up weeks by date range in code
-- So we add WEEK rows here but DAYs remain pointing to MONTH
-- ---------------------------------------------------------------------------
-- 2025 WEEK rows with generate_series over Mondays
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status)
WITH mondays AS (
  SELECT d::date as monday,
         EXTRACT(YEAR FROM d)::int as year,
         EXTRACT(MONTH FROM d)::int as month,
         ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM d), EXTRACT(MONTH FROM d) ORDER BY d) as week_index
  FROM generate_series('2025-01-06'::date, '2025-12-29'::date, interval '1 week') AS d
),
week_dates AS (
  SELECT monday,
         year, month, week_index,
         LEAST((monday + interval '6 days')::date,
               (date_trunc('month', make_date(year, month, 1)) + interval '1 month' - interval '1 day')::date) as end_date,
         CASE WHEN month < 10 THEN '5eed0000-0000-0000-002f-0000000000'::text
              WHEN month = 10 THEN '5eed0000-0000-0000-002f-000000000011'::text
              WHEN month = 11 THEN '5eed0000-0000-0000-002f-000000000012'::text
              ELSE '5eed0000-0000-0000-002f-000000000013'::text
         END ||
         LPAD((CASE WHEN month < 10 THEN 8 + month ELSE month - 2 END)::text, 2, '0') as month_period_id_prefix
  FROM mondays
)
SELECT (md5('progest-seed-week-' || monday::text)::uuid),
       'W' || year || '-' || LPAD(month::text, 2, '0') || '-' || week_index,
       'Tuần ' || week_index || ' tháng ' || month || '/' || year,
       'WEEK',
       CASE WHEN month = 1 THEN '5eed0000-0000-0000-002f-000000000008'::uuid
            WHEN month = 2 THEN '5eed0000-0000-0000-002f-000000000009'::uuid
            WHEN month = 3 THEN '5eed0000-0000-0000-002f-00000000000a'::uuid
            WHEN month = 4 THEN '5eed0000-0000-0000-002f-00000000000b'::uuid
            WHEN month = 5 THEN '5eed0000-0000-0000-002f-00000000000c'::uuid
            WHEN month = 6 THEN '5eed0000-0000-0000-002f-00000000000d'::uuid
            WHEN month = 7 THEN '5eed0000-0000-0000-002f-00000000000e'::uuid
            WHEN month = 8 THEN '5eed0000-0000-0000-002f-00000000000f'::uuid
            WHEN month = 9 THEN '5eed0000-0000-0000-002f-000000000010'::uuid
            WHEN month = 10 THEN '5eed0000-0000-0000-002f-000000000011'::uuid
            WHEN month = 11 THEN '5eed0000-0000-0000-002f-000000000012'::uuid
            ELSE '5eed0000-0000-0000-002f-000000000013'::uuid
       END::uuid,
       monday, end_date, 'CLOSED'
FROM week_dates
ON CONFLICT DO NOTHING;

-- 2026 WEEK rows (Jan-Sep only as per plan)
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status)
WITH mondays AS (
  SELECT d::date as monday,
         EXTRACT(YEAR FROM d)::int as year,
         EXTRACT(MONTH FROM d)::int as month,
         ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM d), EXTRACT(MONTH FROM d) ORDER BY d) as week_index
  FROM generate_series('2026-01-05'::date, '2026-09-28'::date, interval '1 week') AS d
),
week_dates AS (
  SELECT monday,
         year, month, week_index,
         LEAST((monday + interval '6 days')::date,
               (date_trunc('month', make_date(year, month, 1)) + interval '1 month' - interval '1 day')::date) as end_date
  FROM mondays
)
SELECT (md5('progest-seed-week-' || monday::text)::uuid),
       'W' || year || '-' || LPAD(month::text, 2, '0') || '-' || week_index,
       'Tuần ' || week_index || ' tháng ' || month || '/' || year,
       'WEEK',
       CASE WHEN month = 1 THEN '5eed0000-0000-0000-000f-00000000000b'::uuid
            WHEN month = 2 THEN '5eed0000-0000-0000-000f-00000000000c'::uuid
            WHEN month = 3 THEN '5eed0000-0000-0000-000f-00000000000d'::uuid
            WHEN month = 4 THEN '5eed0000-0000-0000-000f-00000000000e'::uuid
            WHEN month = 5 THEN '5eed0000-0000-0000-000f-00000000000f'::uuid
            WHEN month = 6 THEN '5eed0000-0000-0000-000f-000000000010'::uuid
            WHEN month = 7 THEN '5eed0000-0000-0000-000f-000000000011'::uuid
            WHEN month = 8 THEN '5eed0000-0000-0000-000f-000000000012'::uuid
            ELSE '5eed0000-0000-0000-000f-000000000013'::uuid
       END::uuid,
       monday, end_date, 'OPEN'
FROM week_dates
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2025 Target tree for comparison (all ACTIVE, version = 1)
-- L1: Branch-Year 2025, L2: Departments, L3: Quarters, L4: Months
-- Using same KPI and department IDs as 2026, clean values: 1000 branch
-- Structure: L1 (branch, 1000) -> L2 (2 depts, 500 each) -> L3 (4 quarters, 125 each) -> L4 (months)
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, version, status, assigned_at, approved_at, created_at, effective_from)
SELECT v.id, v.business_objective_id, v.kpi_definition_id, v.department_id, v.evaluation_period_id, v.parent_assignment_id,
       v.split_dimension, v.target_value, v.version, v.status, v.assigned_at, v.approved_at, v.created_at, p.start_date
FROM (VALUES
 -- L1: Branch-Year 2025
 ('5eed0000-0000-0000-0025-000000000001'::uuid, NULL::uuid, '5eed0000-0000-0000-0005-000000000001'::uuid, '5eed0000-0000-0000-0001-000000000001'::uuid, '5eed0000-0000-0000-002f-000000000001'::uuid, NULL::uuid, NULL::target_split_dimension, 1000, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 -- L2: Dept-Year (2 departments, 500 each)
 ('5eed0000-0000-0000-0025-000000000002', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000001', '5eed0000-0000-0000-0025-000000000001', 'ORG'::target_split_dimension, 500, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000003', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-002f-000000000001', '5eed0000-0000-0000-0025-000000000001', 'ORG'::target_split_dimension, 500, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 -- L3: Quarters (4 quarters × 2 depts = 8 rows, 125 per quarter)
 ('5eed0000-0000-0000-0025-000000000004', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000004', '5eed0000-0000-0000-0025-000000000002', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000005', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000005', '5eed0000-0000-0000-0025-000000000002', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000006', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000006', '5eed0000-0000-0000-0025-000000000002', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000007', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000007', '5eed0000-0000-0000-0025-000000000002', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000008', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-002f-000000000004', '5eed0000-0000-0000-0025-000000000003', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-000000000009', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-002f-000000000005', '5eed0000-0000-0000-0025-000000000003', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-00000000000a', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-002f-000000000006', '5eed0000-0000-0000-0025-000000000003', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-00000000000b', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-002f-000000000007', '5eed0000-0000-0000-0025-000000000003', 'PERIOD'::target_split_dimension, 125, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 -- L4: Months (one month example: Q1 = 3 months, 125/3 ≈ 42, last gets remainder)
 ('5eed0000-0000-0000-0025-00000000000c', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000008', '5eed0000-0000-0000-0025-000000000004', 'PERIOD'::target_split_dimension, 42, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-00000000000d', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-000000000009', '5eed0000-0000-0000-0025-000000000004', 'PERIOD'::target_split_dimension, 42, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
 ('5eed0000-0000-0000-0025-00000000000e', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-002f-00000000000a', '5eed0000-0000-0000-0025-000000000004', 'PERIOD'::target_split_dimension, 41, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0))
) AS v(id, business_objective_id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, version, status, assigned_at, approved_at, created_at)
JOIN evaluation_period p ON p.id = v.evaluation_period_id
ORDER BY v.id
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2025 detail for the comparison year: branch quarters, and for HDV/KH_MOI/DU_NO every department quarter split into
-- teams, employees and months (the 2025 progress below picks the month rows up). All ACTIVE, sums exact.
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-KH-5eed0000-0000-0000-0001-000000000001--2025', 'KH_MOI', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025', NULL::target_split_dimension, 420, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000001--2025', 'DU_NO', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025', NULL::target_split_dimension, 1000, 'ACTIVE'::target_assignment_status, NULL::uuid, NULL::uuid, NULL::text)
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-HDV-5eed0000-0000-0000-0001-000000000001--2025-Q1', 'HDV', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 250, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000001--2025-Q2', 'HDV', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 250, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000001--2025-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 250, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000001--2025-Q4', 'HDV', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 250, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000001--2025-Q1', 'KH_MOI', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 90, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000001--2025-Q2', 'KH_MOI', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000001--2025-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000001--2025-Q4', 'KH_MOI', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 120, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000001--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 220, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000001--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 240, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000001--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 260, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000001--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 280, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-KH-5eed0000-0000-0000-0001-000000000002--2025', 'KH_MOI', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025', 'ORG'::target_split_dimension, 420, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000002--2025', 'DU_NO', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025', 'ORG'::target_split_dimension, 600, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000003--2025', 'DU_NO', '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025', 'ORG'::target_split_dimension, 400, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000001'::uuid, NULL::uuid, '2025')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-KH-5eed0000-0000-0000-0001-000000000002--2025-Q1', 'KH_MOI', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 90, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000002--2025-Q2', 'KH_MOI', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 100, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000002--2025-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000002--2025-Q4', 'KH_MOI', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 120, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000002--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 130, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000002--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 145, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000002--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 155, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000002--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 170, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000003--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q1', 'PERIOD'::target_split_dimension, 90, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000003--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q2', 'PERIOD'::target_split_dimension, 95, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000003--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q3', 'PERIOD'::target_split_dimension, 105, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000003--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q4', 'PERIOD'::target_split_dimension, 110, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-HDV-5eed0000-0000-0000-0001-000000000004--2025-Q1', 'HDV', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 65, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000005--2025-Q1', 'HDV', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 60, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000006--2025-Q1', 'HDV', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 71, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000007--2025-Q1', 'HDV', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 54, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000004--2025-Q2', 'HDV', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 68, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000005--2025-Q2', 'HDV', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 57, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000006--2025-Q2', 'HDV', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 68, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000007--2025-Q2', 'HDV', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 57, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000004--2025-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 62, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000005--2025-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 63, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000006--2025-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 72, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000007--2025-Q3', 'HDV', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 53, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000004--2025-Q4', 'HDV', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 66, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000005--2025-Q4', 'HDV', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 59, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000006--2025-Q4', 'HDV', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 70, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV-5eed0000-0000-0000-0001-000000000007--2025-Q4', 'HDV', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000004--2025-Q1', 'KH_MOI', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 46, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000005--2025-Q1', 'KH_MOI', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 44, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000004--2025-Q2', 'KH_MOI', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000005--2025-Q2', 'KH_MOI', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000004--2025-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000005--2025-Q3', 'KH_MOI', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000004--2025-Q4', 'KH_MOI', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 63, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH-5eed0000-0000-0000-0001-000000000005--2025-Q4', 'KH_MOI', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 57, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000004--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 67, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000005--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 63, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000004--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 79, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000005--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 66, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000004--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 77, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000005--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 78, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000004--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 90, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000005--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 80, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000002'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000006--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 51, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000007--2025-Q1', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q1', 'ORG'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000006--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 52, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000007--2025-Q2', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q2', 'ORG'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000006--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 60, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000007--2025-Q3', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q3', 'ORG'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000006--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 61, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO-5eed0000-0000-0000-0001-000000000007--2025-Q4', 'DU_NO', '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q4', 'ORG'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000003'::uuid, NULL::uuid, '2025-Q4')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-Q1', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 54, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 35, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-Q2', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 57, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 29, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-Q3', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 53, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 27, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 38, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-Q4', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 55, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-Q1', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-Q1', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-Q1', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 20, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-Q1', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 24, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-Q2', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-Q2', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 27, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-Q2', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-Q2', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 24, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 27, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 25, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-Q3', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 30, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-Q4', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 32, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-Q4', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-Q4', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 26, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-Q4', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 29, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 34, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 41, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 38, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 31, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 35, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 40, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 36, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 42, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 46, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 44, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000004'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 37, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000005'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 23, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-Q1', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1', 'ORG'::target_split_dimension, 39, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 24, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-Q2', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2', 'ORG'::target_split_dimension, 43, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 27, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-Q3', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3', 'ORG'::target_split_dimension, 45, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 33, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 28, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000006'::uuid, NULL::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-Q4', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4', 'ORG'::target_split_dimension, 49, 'ACTIVE'::target_assignment_status, '5eed0000-0000-0000-0001-000000000007'::uuid, NULL::uuid, '2025-Q4')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-target-' || t.key)::uuid,
       NULL::uuid,
       kpi.id, t.dept_id, t.emp_id, per.id, par.id, t.split, t.target_value, t.status,
       '2025-01-01 08:00+00'::timestamptz, CASE WHEN t.status = 'ACTIVE' THEN '2025-01-15 17:00+00'::timestamptz END, '2025-01-01 08:00+00'::timestamptz, per.start_date
FROM (VALUES
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-01', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-02', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-03', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 21, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-04', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-05', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-06', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 20, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-07', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-08', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-09', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 20, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000004-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000005-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000006-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000007-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000008-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-000000000009-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-10', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 19, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-11', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4'),
    ('2025-HDV--5eed0000-0000-0000-0007-00000000000a-2025-12', 'HDV', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 6, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 6, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-01', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-02', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-03', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 6, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-04', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-05', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-06', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-07', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-08', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-09', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-10', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-11', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000004-2025-12', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-10', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-11', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000005-2025-12', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-10', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-11', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000006-2025-12', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-10', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-11', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-KH--5eed0000-0000-0000-0007-000000000007-2025-12', 'KH_MOI', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000004-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000004'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000005-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000005'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000006-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000006'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000007-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000007'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-01', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-01', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-02', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-02', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-03', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-03', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q1'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 7, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-04', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-04', 'PERIOD'::target_split_dimension, 15, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-05', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-05', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-06', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-06', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q2'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 8, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-07', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-07', 'PERIOD'::target_split_dimension, 13, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-08', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-08', 'PERIOD'::target_split_dimension, 14, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-09', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-09', 'PERIOD'::target_split_dimension, 18, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q3'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 11, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000008-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 12, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000008'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 9, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-000000000009-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 10, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-000000000009'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-10', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-10', 'PERIOD'::target_split_dimension, 17, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-11', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-11', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4'),
    ('2025-DU_NO--5eed0000-0000-0000-0007-00000000000a-2025-12', 'DU_NO', NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-12', 'PERIOD'::target_split_dimension, 16, 'ACTIVE'::target_assignment_status, NULL::uuid, '5eed0000-0000-0000-0007-00000000000a'::uuid, '2025-Q4')
) AS t(key, kpi_code, dept_id, emp_id, period_code, split, target_value, status, p_dept, p_emp, p_period)
JOIN kpi_definition kpi ON kpi.code = t.kpi_code
JOIN evaluation_period per ON per.code = t.period_code
LEFT JOIN evaluation_period pper ON pper.code = t.p_period
LEFT JOIN target_assignment par ON par.kpi_definition_id = kpi.id AND par.evaluation_period_id = pper.id
     AND ((t.p_dept IS NOT NULL AND par.department_id = t.p_dept) OR (t.p_emp IS NOT NULL AND par.employee_id = t.p_emp))
     AND par.status IN ('DRAFT', 'ACTIVE')
WHERE t.p_period IS NULL OR par.id IS NOT NULL
ON CONFLICT DO NOTHING;


-- ---------------------------------------------------------------------------
-- 2025 Actual progress (one row per month L4, value = target × factor 0.80–1.20)
-- Fixed factors (not random) for reproducibility
-- ---------------------------------------------------------------------------
INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, status)
SELECT md5('progest-actual-' || ta.id::text || '-' || m.month_no::text)::uuid,
       ta.id,
       MAKE_DATE(2025, m.month_no::int, 15)::date,
       ta.target_value * m.factor,
       'VALID'::actual_progress_status
FROM target_assignment ta
CROSS JOIN (VALUES
    (1, 0.92),
    (2, 1.05),
    (3, 0.88),
    (4, 1.15),
    (5, 0.95),
    (6, 1.08),
    (7, 0.80),
    (8, 1.10),
    (9, 0.98),
    (10, 1.12),
    (11, 0.90),
    (12, 1.18)
) AS m(month_no, factor)
WHERE ta.evaluation_period_id = CASE m.month_no
        WHEN 1 THEN '5eed0000-0000-0000-002f-000000000008'::uuid
        WHEN 2 THEN '5eed0000-0000-0000-002f-000000000009'::uuid
        WHEN 3 THEN '5eed0000-0000-0000-002f-00000000000a'::uuid
        WHEN 4 THEN '5eed0000-0000-0000-002f-00000000000b'::uuid
        WHEN 5 THEN '5eed0000-0000-0000-002f-00000000000c'::uuid
        WHEN 6 THEN '5eed0000-0000-0000-002f-00000000000d'::uuid
        WHEN 7 THEN '5eed0000-0000-0000-002f-00000000000e'::uuid
        WHEN 8 THEN '5eed0000-0000-0000-002f-00000000000f'::uuid
        WHEN 9 THEN '5eed0000-0000-0000-002f-000000000010'::uuid
        WHEN 10 THEN '5eed0000-0000-0000-002f-000000000011'::uuid
        WHEN 11 THEN '5eed0000-0000-0000-002f-000000000012'::uuid
        ELSE '5eed0000-0000-0000-002f-000000000013'::uuid
END
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2025 Employee Year Targets (for MyTargets previousYear overview & comparison)
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, employee_id, evaluation_period_id,
    target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2025-emp-year-' || q.employee_id::text || '-' || q.kpi_definition_id::text)::uuid,
       NULL::uuid,
       q.kpi_definition_id,
       q.employee_id,
       yp.id,
       CASE WHEN k.aggregation_type = 'SUM' THEN sum(q.target_value) ELSE round(avg(q.target_value), 4) END,
       'ACTIVE'::target_assignment_status,
       make_timestamp(2025, 1, 1, 8, 0, 0),
       make_timestamp(2025, 1, 15, 17, 0, 0),
       make_timestamp(2025, 1, 1, 8, 0, 0),
       yp.start_date
FROM target_assignment q
JOIN evaluation_period qp ON qp.id = q.evaluation_period_id AND qp.period_type = 'QUARTER' AND qp.code LIKE '2025%'
JOIN evaluation_period yp ON yp.code = '2025'
JOIN kpi_definition k ON k.id = q.kpi_definition_id
WHERE q.employee_id IS NOT NULL AND q.status = 'ACTIVE'
GROUP BY q.employee_id, q.kpi_definition_id, yp.id, yp.start_date, k.aggregation_type
ON CONFLICT (kpi_definition_id, employee_id, evaluation_period_id) WHERE employee_id IS NOT NULL AND status = 'ACTIVE'
DO UPDATE SET target_value = EXCLUDED.target_value;

-- ============================================================================
-- Evaluation / performance manual-test dataset. Fixed identities, no updates to
-- existing evaluations: running the seed again preserves changes made in the UI.
-- Select 2026-09 through 2026-12 on the evaluation and performance screens.
-- 2026-09 and 2026-10 are FINAL (past periods, used as the comparison base); 2026-11 and 2026-12 stay DRAFT.
-- Sample volumes are data, not changes to scoring/ranking configuration.
-- ============================================================================
CREATE TEMP TABLE seed_evaluation_case ON COMMIT DROP AS
SELECT e.id AS employee_id, e.employee_code, e.manager_id, e.job_position_id,
       ep.id AS period_id, ep.code AS period_code,
       ep.start_date + 14 AS progress_date,
       ep.start_date::timestamp AT TIME ZONE 'UTC' AS created_at,
       (ep.start_date + 15)::timestamp AT TIME ZONE 'UTC' AS calculated_at,
       CASE WHEN ep.code IN ('2026-09', '2026-10') THEN 'FINAL' ELSE 'DRAFT' END::performance_evaluation_status AS evaluation_status,
       md5('evaluation-demo-' || e.id::text || '-' || ep.id::text)::uuid AS evaluation_id
FROM (VALUES
    ('NV002'), ('NV003'), ('NV004'), ('NV005'), ('NV006'), ('NV007'),
    ('NV008'), ('NV009'), ('NV010'), ('NV011'), ('NV012')
) sample(employee_code)
JOIN employee e ON e.employee_code = sample.employee_code
CROSS JOIN (VALUES
    ('2026-09'), ('2026-10'), ('2026-11'), ('2026-12')
) month_sample(code)
JOIN evaluation_period ep ON ep.code = month_sample.code;

-- Each KPI is backed by a target, approved submission, verification, result item
-- and progress. NV010 deliberately has no DU_NO target to exercise the N/A state.
CREATE TEMP TABLE seed_evaluation_metric ON COMMIT DROP AS
SELECT c.*, k.id AS kpi_id, k.code AS kpi_code, k.aggregation_type,
       COALESCE(existing.target_value, metric.target_value) AS target_value,
        round(CASE WHEN k.code = 'CSAT' THEN LEAST(COALESCE(existing.target_value, metric.target_value) * fluctuation.factor, 5)
            WHEN k.code = 'TUAN_THU' THEN LEAST(COALESCE(existing.target_value, metric.target_value) * fluctuation.factor, 100)
            WHEN k.code = 'KH_MOI' THEN round(COALESCE(existing.target_value, metric.target_value) * fluctuation.factor)
            WHEN k.direction = 'LOWER_BETTER'
            THEN COALESCE(existing.target_value, metric.target_value) / fluctuation.factor
            ELSE COALESCE(existing.target_value, metric.target_value) * fluctuation.factor END, 4) AS actual_value,
       CASE WHEN k.aggregation_type = 'RATIO' THEN 100::numeric END AS denominator,
       wt.id AS work_type_id,
       COALESCE(existing.id, md5('evaluation-demo-target-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid) AS target_id,
       md5('evaluation-demo-progress-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid AS progress_id,
       md5('evaluation-demo-work-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid AS work_id,
       md5('evaluation-demo-submission-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid AS submission_id,
       md5('evaluation-demo-verification-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid AS verification_id,
       md5('evaluation-demo-result-' || c.employee_id::text || '-' || c.period_id::text || '-' || k.id::text)::uuid AS result_id
FROM seed_evaluation_case c
CROSS JOIN (VALUES
    ('HDV', 100::numeric, 'HUY_DONG', 1), ('KH_MOI', 40::numeric, 'MO_TAI_KHOAN', 2),
    ('DU_NO', 50::numeric, 'GIAI_NGAN', 3), ('CSAT', 4.5::numeric, 'GIAO_DICH', 4),
    ('TY_LE_LOI', 2::numeric, 'GIAO_DICH', 5), ('TUAN_THU', 95::numeric, 'MO_TAI_KHOAN', 6)
) metric(kpi_code, target_value, work_type_code, metric_index)
-- Sample performance has separate strengths/weaknesses per employee and KPI.
-- Arrays follow metric_index above; month offsets deliberately move KPIs in
-- different directions. Fixed data makes each run reproducible, unlike random().
JOIN (VALUES
    ('NV002', ARRAY[1.08, 0.84, 1.19, 0.96, 0.72, 1.02]::numeric[]),
    ('NV003', ARRAY[0.81, 1.14, 0.92, 1.04, 1.17, 0.98]::numeric[]),
    ('NV004', ARRAY[1.28, 0.82, 1.09, 0.94, 0.73, 1.03]::numeric[]),
    ('NV005', ARRAY[0.69, 1.18, 0.86, 1.01, 0.92, 0.96]::numeric[]),
    ('NV006', ARRAY[1.16, 0.77, 1.04, 0.89, 1.12, 0.97]::numeric[]),
    ('NV007', ARRAY[0.54, 0.83, 0.61, 0.91, 0.68, 0.88]::numeric[]),
    ('NV008', ARRAY[1.35, 1.08, 0.87, 1.06, 0.79, 0.93]::numeric[]),
    ('NV009', ARRAY[0.95, 1.21, 0.78, 0.98, 1.09, 1.02]::numeric[]),
    ('NV010', ARRAY[0.72, 0.94, 0.65, 0.87, 0.81, 0.91]::numeric[]),
    ('NV011', ARRAY[1.03, 0.88, 1.12, 0.93, 1.04, 0.99]::numeric[]),
    ('NV012', ARRAY[0.86, 0.71, 1.02, 0.95, 0.89, 0.94]::numeric[])
) employee_sample(employee_code, factors) ON employee_sample.employee_code = c.employee_code
JOIN (VALUES
    ('2026-09', ARRAY[-0.09, 0.06, -0.12, -0.02, 0.08, -0.04]::numeric[]),
    ('2026-10', ARRAY[0.00, 0.00, 0.00, 0.00, 0.00, 0.00]::numeric[]),
    ('2026-11', ARRAY[0.07, -0.11, 0.04, 0.03, -0.06, -0.02]::numeric[]),
    ('2026-12', ARRAY[-0.04, 0.14, 0.09, -0.05, 0.11, 0.02]::numeric[])
) period_sample(code, offsets) ON period_sample.code = c.period_code
CROSS JOIN LATERAL (
    SELECT employee_sample.factors[metric.metric_index]
        + period_sample.offsets[metric.metric_index] AS factor
) fluctuation
JOIN kpi_definition k ON k.code = metric.kpi_code
-- Reuse the month target of the 2026 target tree when the employee has one, so the evaluation screens, the target
-- overview and the progress below agree; otherwise the sample constant is used.
LEFT JOIN LATERAL (
    SELECT t.id, t.target_value FROM target_assignment t
    WHERE t.kpi_definition_id = k.id AND t.employee_id = c.employee_id AND t.evaluation_period_id = c.period_id
      AND t.status = 'ACTIVE'
    LIMIT 1
) existing ON TRUE
JOIN work_type wt ON wt.code = metric.work_type_code
WHERE NOT (c.employee_code = 'NV010' AND k.code = 'DU_NO');

INSERT INTO target_assignment (id, kpi_definition_id, employee_id, evaluation_period_id,
    target_value, version, status, created_at, assigned_at, approved_at, effective_from)
SELECT m.target_id, m.kpi_id, m.employee_id, m.period_id, m.target_value, 1, 'ACTIVE',
       m.created_at, m.created_at, m.created_at, p.start_date
FROM seed_evaluation_metric m
JOIN evaluation_period p ON p.id = m.period_id
ON CONFLICT DO NOTHING;

-- The sample months above can sit without a quarter target (Q3 for the staff outside the 2026 tree, Q4 for every KPI
-- the Q4 chain does not cover). Give each such employee/KPI a standalone quarter target, as the quality KPIs have
-- in Q1-Q3, and split it into its months: SUM KPIs total the months, the others take their average.
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, employee_id, evaluation_period_id,
    target_value, status, assigned_at, approved_at, effective_from)
SELECT md5('seed-quarter-target-' || m.employee_id::text || '-' || m.kpi_definition_id::text || '-' || q.id::text)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       m.kpi_definition_id, m.employee_id, q.id,
       CASE WHEN k.aggregation_type = 'SUM' THEN sum(m.target_value) ELSE round(avg(m.target_value), 4) END,
       'ACTIVE'::target_assignment_status, now(), now(), q.start_date
FROM target_assignment m
JOIN evaluation_period mp ON mp.id = m.evaluation_period_id AND mp.period_type = 'MONTH'
JOIN evaluation_period q ON q.id = mp.parent_period_id
JOIN kpi_definition k ON k.id = m.kpi_definition_id
WHERE m.employee_id IS NOT NULL AND m.parent_assignment_id IS NULL AND m.status = 'ACTIVE'
  AND q.start_date >= '2026-01-01'
  AND NOT EXISTS (
      SELECT 1 FROM target_assignment x
      WHERE x.employee_id = m.employee_id AND x.kpi_definition_id = m.kpi_definition_id
        AND x.evaluation_period_id = q.id AND x.status IN ('DRAFT', 'ACTIVE'))
GROUP BY m.employee_id, m.kpi_definition_id, q.id, k.aggregation_type, q.start_date
ON CONFLICT DO NOTHING;

UPDATE target_assignment m
SET parent_assignment_id = p.id, split_dimension = 'PERIOD'
FROM evaluation_period mp, target_assignment p
WHERE mp.id = m.evaluation_period_id AND mp.period_type = 'MONTH'
  AND m.employee_id IS NOT NULL AND m.parent_assignment_id IS NULL AND m.status = 'ACTIVE'
  AND p.id = md5('seed-quarter-target-' || m.employee_id::text || '-' || m.kpi_definition_id::text || '-' || mp.parent_period_id::text)::uuid;

-- ---------------------------------------------------------------------------
-- 2026 Employee Year Targets (for MyTargets year overview & quarter resolution)
-- ---------------------------------------------------------------------------
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, employee_id, evaluation_period_id,
    target_value, status, assigned_at, approved_at, created_at, effective_from)
SELECT md5('seed-2026-emp-year-' || q.employee_id::text || '-' || q.kpi_definition_id::text)::uuid,
       '5eed0000-0000-0000-0010-000000000001'::uuid,
       q.kpi_definition_id,
       q.employee_id,
       yp.id,
       CASE WHEN k.aggregation_type = 'SUM' THEN sum(q.target_value) ELSE round(avg(q.target_value), 4) END,
       'ACTIVE'::target_assignment_status,
       make_timestamp(2026, 1, 1, 8, 0, 0),
       make_timestamp(2026, 1, 15, 17, 0, 0),
       make_timestamp(2026, 1, 1, 8, 0, 0),
       yp.start_date
FROM target_assignment q
JOIN evaluation_period qp ON qp.id = q.evaluation_period_id AND qp.period_type = 'QUARTER' AND qp.code LIKE '2026%'
JOIN evaluation_period yp ON yp.code = '2026'
JOIN kpi_definition k ON k.id = q.kpi_definition_id
WHERE q.employee_id IS NOT NULL AND q.status = 'ACTIVE'
GROUP BY q.employee_id, q.kpi_definition_id, yp.id, yp.start_date, k.aggregation_type
ON CONFLICT (kpi_definition_id, employee_id, evaluation_period_id) WHERE employee_id IS NOT NULL AND status = 'ACTIVE'
DO UPDATE SET target_value = EXCLUDED.target_value;

INSERT INTO work (id, work_type_id, assignee_employee_id, created_by, code, title,
    description, planned_start_date, planned_end_date, status, target_value)
SELECT work_id, work_type_id, employee_id, manager_id,
       'EVL-' || period_code || '-' || employee_code || '-' || kpi_code,
       'Kết quả mẫu ' || kpi_code || ' / ' || employee_code || ' / ' || period_code,
       'Dữ liệu test tay evaluation và performance, có kết quả đã xác minh.',
       progress_date, progress_date, 'DONE', target_value
FROM seed_evaluation_metric ON CONFLICT DO NOTHING;

INSERT INTO result_submission (id, work_id, submitter_employee_id, submission_no,
    submitted_at, status, comment)
SELECT submission_id, work_id, employee_id, 1, calculated_at - interval '1 hour',
       'SUBMITTED', 'Kết quả mẫu đã đối chiếu với chỉ tiêu.'
FROM seed_evaluation_metric ON CONFLICT DO NOTHING;

-- Filter before insertion: the approval lifecycle trigger also executes before
-- ON CONFLICT, so attempting to insert an existing approved round would fail.
INSERT INTO verification (id, result_submission_id, verifier_employee_id,
    round_no, decision, verified_at, comment)
SELECT m.verification_id, m.submission_id, m.manager_id, 1, 'APPROVED',
       m.calculated_at, 'Đã đối chiếu kết quả mẫu; người duyệt khác người nộp.'
FROM seed_evaluation_metric m
WHERE NOT EXISTS (SELECT 1 FROM verification v WHERE v.result_submission_id = m.submission_id)
ON CONFLICT DO NOTHING;

INSERT INTO verified_work_result (id, verification_id, status, verified_at)
SELECT result_id, verification_id, 'VALID', calculated_at
FROM seed_evaluation_metric ON CONFLICT DO NOTHING;

INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, denominator, status)
SELECT m.progress_id, m.target_id, m.progress_date, m.actual_value, m.denominator, 'VALID'
FROM seed_evaluation_metric m
JOIN target_assignment t ON t.id = m.target_id
ON CONFLICT DO NOTHING;

INSERT INTO work_result_item (id, verified_work_result_id, actual_progress_id, employee_id, value, raw_value, denominator)
SELECT md5('evaluation-demo-item-' || m.progress_id::text)::uuid,
       m.result_id, m.progress_id, m.employee_id, m.actual_value, m.actual_value, m.denominator
FROM seed_evaluation_metric m
JOIN actual_progress ap ON ap.id = m.progress_id
ON CONFLICT DO NOTHING;

-- Confirmed risks exercise point deductions and the configured severe-risk cap;
-- a dismissed event exercises exclusion from evaluation and ranking.
CREATE TEMP TABLE seed_evaluation_risk ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('NV007', 'HIGH', 'CONFIRMED', 10, 'Hồ sơ mẫu thiếu bước kiểm soát, cần kèm cặp.'),
    ('NV008', 'CRITICAL', 'CONFIRMED', 20, 'Vi phạm quy trình mẫu: kiểm tra hạ hạng dù KPI kinh doanh cao.'),
    ('NV010', 'LOW', 'CONFIRMED', 2, 'Sai sót tác nghiệp mẫu đã được ghi nhận.'),
    ('NV004', 'MEDIUM', 'DISMISSED', 0, 'Phản ánh mẫu đã đối chiếu và bác bỏ, không trừ điểm.')
) sample(employee_code, severity, status, penalty_points, description);

INSERT INTO risk_event (id, employee_id, recorded_by, code, event_type, severity,
    description, occurred_at, status, resolved_at)
SELECT md5('evaluation-demo-risk-' || c.employee_id::text || '-' || c.period_id::text)::uuid,
       c.employee_id, c.manager_id, 'EVL-RISK-' || c.period_code || '-' || c.employee_code,
       'PROCEDURE_REVIEW', r.severity::risk_event_severity, r.description,
       c.calculated_at - interval '1 day', r.status::risk_event_status,
       CASE WHEN r.status = 'DISMISSED' THEN c.calculated_at END
FROM seed_evaluation_case c
JOIN seed_evaluation_risk r ON r.employee_code = c.employee_code
WHERE c.period_code <> '2026-09'
ON CONFLICT DO NOTHING;

-- Evaluate the real target/progress records using the current flat seed profile.
-- One RATIO row uses denominator 100, so both current screen calculations agree
-- on its percentage. No manually entered overall or KPI contribution score.
CREATE TEMP TABLE seed_evaluation_score ON COMMIT DROP AS
SELECT m.*, p.id AS profile_id, leaf.weight_percent AS leaf_weight,
       root.id AS root_id, root.weight_percent AS root_weight,
       round(CASE WHEN k.direction = 'LOWER_BETTER'
           THEN t.target_value / ap.value * 100
           ELSE ap.value / t.target_value * 100 END, 4) AS raw_score,
       LEAST(round(CASE WHEN k.direction = 'LOWER_BETTER'
           THEN t.target_value / ap.value * 100
           ELSE ap.value / t.target_value * 100 END, 4), k.max_achievement_percent) AS component_score
FROM seed_evaluation_metric m
JOIN evaluation_profile p ON p.job_position_id = m.job_position_id AND p.code IN ('EP-SALES', 'EP-TELLER')
JOIN evaluation_profile_item leaf ON leaf.evaluation_profile_id = p.id AND leaf.kpi_definition_id = m.kpi_id
JOIN evaluation_profile_item root ON root.id = leaf.parent_item_id
JOIN kpi_definition k ON k.id = m.kpi_id
JOIN target_assignment t ON t.id = m.target_id
JOIN actual_progress ap ON ap.id = m.progress_id;

CREATE TEMP TABLE seed_evaluation_total ON COMMIT DROP AS
SELECT employee_id, period_id, profile_id,
       round(sum(group_score * root_weight / 100), 2) AS total_score
FROM (
    SELECT employee_id, period_id, profile_id, root_id, root_weight,
           round(sum(component_score * leaf_weight) / sum(leaf_weight), 2) AS group_score
    FROM seed_evaluation_score GROUP BY employee_id, period_id, profile_id, root_id, root_weight
) groups GROUP BY employee_id, period_id, profile_id;

-- Only newly inserted evaluations receive seed snapshots and history.
CREATE TEMP TABLE seed_evaluation_inserted (id uuid PRIMARY KEY) ON COMMIT DROP;
WITH inserted AS (
    INSERT INTO performance_evaluation (id, employee_id, evaluation_period_id,
        evaluation_profile_id, total_score, status, calculated_at, approved_at, comment)
    SELECT c.evaluation_id, c.employee_id, c.period_id, totals.profile_id,
           GREATEST(totals.total_score - COALESCE(penalty.points, 0), 0),
           c.evaluation_status, c.calculated_at,
           CASE WHEN c.evaluation_status = 'FINAL' THEN c.calculated_at + interval '1 day' END,
           'Đánh giá mẫu từ kết quả đã duyệt / ' || c.period_code ||
           CASE WHEN c.employee_code = 'NV007' THEN E'\n<!-- COACHING_PLAN: Đối chiếu hồ sơ hằng tuần và rà soát tiến độ với quản lý. -->' ELSE '' END
    FROM seed_evaluation_case c
    JOIN seed_evaluation_total totals ON totals.employee_id = c.employee_id AND totals.period_id = c.period_id
    LEFT JOIN LATERAL (
        SELECT sum(sample.penalty_points) AS points
        FROM risk_event risk JOIN seed_evaluation_risk sample ON sample.employee_code = c.employee_code
        WHERE risk.code = 'EVL-RISK-' || c.period_code || '-' || c.employee_code
            AND risk.status <> 'DISMISSED'
    ) penalty ON true
    ON CONFLICT DO NOTHING RETURNING id
)
INSERT INTO seed_evaluation_inserted SELECT id FROM inserted;

INSERT INTO performance_contribution (id, performance_evaluation_id, employee_id,
    evaluation_period_id, actual_progress_id, target_value_snapshot,
    target_version_snapshot, actual_value, actual_denominator, achievement_percent_raw,
    achievement_percent_capped, weight_snapshot, component_score)
SELECT md5('evaluation-demo-contribution-' || s.progress_id::text)::uuid,
       s.evaluation_id, s.employee_id, s.period_id, s.progress_id, t.target_value,
       t.version, ap.value, ap.denominator, s.raw_score, s.component_score, s.leaf_weight, s.component_score
FROM seed_evaluation_score s JOIN seed_evaluation_inserted fresh ON fresh.id = s.evaluation_id
JOIN target_assignment t ON t.id = s.target_id
JOIN actual_progress ap ON ap.id = s.progress_id
ON CONFLICT DO NOTHING;

INSERT INTO performance_contribution (id, performance_evaluation_id, employee_id,
    evaluation_period_id, risk_event_id, component_score)
SELECT md5('evaluation-demo-risk-contribution-' || r.id::text)::uuid,
       c.evaluation_id, c.employee_id, c.period_id, r.id,
       -sample.penalty_points
FROM seed_evaluation_case c JOIN seed_evaluation_inserted fresh ON fresh.id = c.evaluation_id
JOIN risk_event r ON r.employee_id = c.employee_id AND r.code = 'EVL-RISK-' || c.period_code || '-' || c.employee_code
JOIN seed_evaluation_risk sample ON sample.employee_code = c.employee_code
WHERE r.status = 'CONFIRMED' ON CONFLICT DO NOTHING;

INSERT INTO performance_evaluation_history (id, performance_evaluation_id, action,
    from_status, to_status, new_total_score, changed_by, comment, created_at)
SELECT md5('evaluation-demo-history-' || c.evaluation_id::text)::uuid,
       c.evaluation_id, 'CALCULATED', 'DRAFT', 'CALCULATED', pe.total_score,
       c.manager_id, 'Tính điểm mẫu từ các kết quả đã duyệt.', c.calculated_at
FROM seed_evaluation_case c JOIN seed_evaluation_inserted fresh ON fresh.id = c.evaluation_id
JOIN performance_evaluation pe ON pe.id = c.evaluation_id
WHERE c.evaluation_status <> 'DRAFT'
ON CONFLICT DO NOTHING;

INSERT INTO performance_evaluation_history (id, performance_evaluation_id, action,
    from_status, to_status, old_total_score, new_total_score, changed_by, comment, created_at)
SELECT md5('evaluation-demo-status-history-' || c.evaluation_id::text)::uuid,
       c.evaluation_id, 'STATUS_CHANGED', 'CALCULATED', c.evaluation_status,
       pe.total_score, pe.total_score, c.manager_id,
       'Trạng thái mẫu để kiểm tra giao diện.', c.calculated_at + interval '1 day'
FROM seed_evaluation_case c JOIN seed_evaluation_inserted fresh ON fresh.id = c.evaluation_id
JOIN performance_evaluation pe ON pe.id = c.evaluation_id
WHERE c.evaluation_status NOT IN ('CALCULATED', 'DRAFT') ON CONFLICT DO NOTHING;

-- Pending submissions have no progress or KPI points until verification succeeds.
INSERT INTO work (id, work_type_id, assignee_employee_id, created_by, code, title, status)
SELECT md5('evaluation-demo-pending-work-' || c.evaluation_id::text)::uuid,
       wt.id, c.employee_id, c.manager_id,
       'EVL-PENDING-' || c.period_code || '-' || c.employee_code,
       'Kết quả mẫu đang chờ duyệt / ' || c.employee_code, 'SUBMITTED'
FROM seed_evaluation_case c JOIN work_type wt ON wt.code = 'HUY_DONG'
WHERE c.employee_code IN ('NV004', 'NV005') AND c.period_code <> '2026-09'
ON CONFLICT DO NOTHING;

INSERT INTO result_submission (id, work_id, submitter_employee_id, submitted_at, status, comment)
SELECT md5('evaluation-demo-pending-submission-' || c.evaluation_id::text)::uuid,
       md5('evaluation-demo-pending-work-' || c.evaluation_id::text)::uuid,
       c.employee_id, c.calculated_at,
       CASE WHEN c.employee_code = 'NV005' THEN 'UNDER_REVIEW' ELSE 'SUBMITTED' END::submission_status,
       'Chưa được ghi nhận KPI vì đang chờ duyệt.'
FROM seed_evaluation_case c
WHERE c.employee_code IN ('NV004', 'NV005') AND c.period_code <> '2026-09'
ON CONFLICT DO NOTHING;

-- ============================================================================
-- Transaction reconcile demo (docs/api/WORK_TRANSACTION_CONTRACT.md)
-- work_type_kpi.value_source defaults to COUNT, so existing and seeded mappings credit 1 per transaction.
-- NV004 holds one DONE work (2 matched codes, KPI credited), one SUBMITTED work with mismatching codes and
-- one SUBMITTED work whose code is not in any import yet.
-- ============================================================================
INSERT INTO target_assignment (id, kpi_definition_id, employee_id, evaluation_period_id, target_value, version, status, created_at, assigned_at, approved_at, effective_from)
SELECT '5eed0000-0000-0000-0047-000000000001', '5eed0000-0000-0000-0005-000000000007', '5eed0000-0000-0000-0007-000000000004',
       '5eed0000-0000-0000-000f-000000000003', 20, 1, 'ACTIVE', now(), now(), now(),
       (SELECT start_date FROM evaluation_period WHERE id = '5eed0000-0000-0000-000f-000000000003'::uuid)
WHERE NOT EXISTS (
    SELECT 1 FROM target_assignment t
    WHERE t.kpi_definition_id = '5eed0000-0000-0000-0005-000000000007'
      AND t.employee_id = '5eed0000-0000-0000-0007-000000000004'
      AND t.evaluation_period_id = '5eed0000-0000-0000-000f-000000000003' AND t.status = 'ACTIVE')
ON CONFLICT DO NOTHING;

INSERT INTO import_batch (id, file_name, total_rows, imported_rows, duplicate_rows, error_rows, imported_by, imported_at)
SELECT '5eed0000-0000-0000-0040-000000000001', 'demo-transactions.xlsx', 4, 4, 0, 0, e.id, now()
FROM employee e WHERE e.employee_code = 'NV013'
ON CONFLICT DO NOTHING;

INSERT INTO imported_transaction (id, import_batch_id, transaction_code, employee_id, product_code, transaction_date, amount, denominator)
SELECT t.id::uuid, '5eed0000-0000-0000-0040-000000000001', t.code, e.id, t.product, DATE '2026-11-02', t.amount, NULL
FROM (VALUES
    ('5eed0000-0000-0000-0041-000000000001', 'TX-DEMO-001', 'NV004', 'TD_CLASSIC', 1500000),
    ('5eed0000-0000-0000-0041-000000000002', 'TX-DEMO-002', 'NV004', 'TD_GOLD',    2000000),
    ('5eed0000-0000-0000-0041-000000000003', 'TX-DEMO-003', 'NV005', 'TD_CLASSIC', 1000000),
    ('5eed0000-0000-0000-0041-000000000004', 'TX-DEMO-004', 'NV004', 'TD_CLASSIC', 3000000)
) AS t(id, code, emp, product, amount)
JOIN employee e ON e.employee_code = t.emp
WHERE EXISTS (SELECT 1 FROM import_batch WHERE id = '5eed0000-0000-0000-0040-000000000001')
ON CONFLICT DO NOTHING;

INSERT INTO work (id, work_type_id, assignee_employee_id, created_by, code, title, status)
SELECT w.id::uuid, '5eed0000-0000-0000-000c-000000000005', e.id, COALESCE(e.manager_id, e.id), w.code, w.title, w.status::work_status
FROM (VALUES
    ('5eed0000-0000-0000-0042-000000000001', 'W-DEMO-TX-MATCHED',    'Classic card - all codes matched',   'DONE'),
    ('5eed0000-0000-0000-0042-000000000002', 'W-DEMO-TX-NOTMATCHED', 'Classic card - codes not matched',   'SUBMITTED'),
    ('5eed0000-0000-0000-0042-000000000003', 'W-DEMO-TX-INREVIEW',   'Classic card - waiting for import',  'SUBMITTED')
) AS w(id, code, title, status)
JOIN employee e ON e.employee_code = 'NV004'
ON CONFLICT DO NOTHING;

INSERT INTO work_transaction (id, work_id, transaction_code, status, reason, imported_transaction_id, created_at, updated_at)
SELECT x.id::uuid, x.work_id::uuid, x.code, x.status, x.reason, it.id, now(), now()
FROM (VALUES
    ('5eed0000-0000-0000-0043-000000000001', '5eed0000-0000-0000-0042-000000000001', 'TX-DEMO-001',   'MATCHED',     NULL,               TRUE),
    ('5eed0000-0000-0000-0043-000000000002', '5eed0000-0000-0000-0042-000000000001', 'TX-DEMO-004',   'MATCHED',     NULL,               TRUE),
    ('5eed0000-0000-0000-0043-000000000003', '5eed0000-0000-0000-0042-000000000002', 'TX-DEMO-002',   'NOT_MATCHED', 'PRODUCT_MISMATCH', FALSE),
    ('5eed0000-0000-0000-0043-000000000004', '5eed0000-0000-0000-0042-000000000002', 'TX-DEMO-003',   'NOT_MATCHED', 'EMPLOYEE_MISMATCH', FALSE),
    ('5eed0000-0000-0000-0043-000000000005', '5eed0000-0000-0000-0042-000000000003', 'TX-DEMO-PENDING', 'IN_REVIEW', NULL,               FALSE)
) AS x(id, work_id, code, status, reason, linked)
LEFT JOIN imported_transaction it ON x.linked AND it.transaction_code = x.code
WHERE EXISTS (SELECT 1 FROM work WHERE id = x.work_id::uuid)
ON CONFLICT DO NOTHING;

INSERT INTO verified_work_result (id, work_transaction_id, status, verified_at)
SELECT ('5eed0000-0000-0000-0044-00000000000' || right(wt.id::text, 1))::uuid, wt.id, 'VALID', now()
FROM work_transaction wt WHERE wt.id IN ('5eed0000-0000-0000-0043-000000000001', '5eed0000-0000-0000-0043-000000000002')
ON CONFLICT DO NOTHING;

INSERT INTO actual_progress (id, target_assignment_id, progress_date, value, status)
SELECT '5eed0000-0000-0000-0045-000000000001', ta.id, DATE '2026-11-02', 2, 'VALID'
FROM target_assignment ta
WHERE ta.kpi_definition_id = '5eed0000-0000-0000-0005-000000000007'
  AND ta.employee_id = '5eed0000-0000-0000-0007-000000000004'
  AND ta.evaluation_period_id = '5eed0000-0000-0000-000f-000000000003' AND ta.status = 'ACTIVE'
  AND EXISTS (SELECT 1 FROM verified_work_result WHERE id = '5eed0000-0000-0000-0044-000000000001')
ON CONFLICT DO NOTHING;

INSERT INTO work_result_item (id, verified_work_result_id, actual_progress_id, employee_id, value, raw_value, multiplier_applied)
SELECT ('5eed0000-0000-0000-0046-00000000000' || right(vr.id::text, 1))::uuid, vr.id, '5eed0000-0000-0000-0045-000000000001',
       '5eed0000-0000-0000-0007-000000000004', 1, 1, 1.0
FROM verified_work_result vr
WHERE vr.work_transaction_id IN ('5eed0000-0000-0000-0043-000000000001', '5eed0000-0000-0000-0043-000000000002')
  AND EXISTS (SELECT 1 FROM actual_progress WHERE id = '5eed0000-0000-0000-0045-000000000001')
ON CONFLICT DO NOTHING;

-- ============================================================================
-- Target Allocation v3: employee "my-targets" flow (D10, D20, BR-36)
-- EmployeeTargetAck (year-level ack) + TargetChangeLog (D10 structured log) + a feedback/reply example.
-- NV008 = PENDING (tier-3 just published), NV009 = CONFIRMED, NV010 = RECONFIRM (a change-log row below
-- explains which KPI changed). Feedback is attached to NV008's existing ACTIVE 2026 employee target row.
-- =====================================================================
-- ============================================================================
INSERT INTO employee_target_ack (id, employee_id, year, status, confirmed_at)
SELECT md5('seed-2026-ack-' || e.employee_code)::uuid, e.id, 2026, v.status,
       CASE WHEN v.status = 'CONFIRMED' THEN make_timestamptz(2026, 1, 10, 9, 0, 0) END
FROM employee e
JOIN (VALUES ('NV008', 'PENDING'), ('NV009', 'CONFIRMED'), ('NV010', 'RECONFIRM')) AS v(employee_code, status)
  ON v.employee_code = e.employee_code
ON CONFLICT (employee_id, year) DO UPDATE
SET status = EXCLUDED.status, confirmed_at = EXCLUDED.confirmed_at;

-- RECONFIRM reason: a mid-term adjustment raised NV010's HDV year target (D12/D13).
INSERT INTO target_change_log (id, actor_id, action, tier, kpi_definition_id, unit, period, old_value, new_value,
    basis, employee_id, department_id, year, occurred_at)
SELECT md5('seed-2026-changelog-nv010-hdv')::uuid, mgr.id, 'ADJUST', 3, k.id, k.unit, 'YEAR', 400, 440,
       'MANUAL', emp.id, emp.department_id, 2026, make_timestamptz(2026, 6, 15, 10, 30, 0)
FROM employee emp
JOIN employee mgr ON mgr.employee_code = 'NV011'
JOIN kpi_definition k ON k.code = 'HDV'
WHERE emp.employee_code = 'NV010'
ON CONFLICT (id) DO UPDATE
SET old_value = EXCLUDED.old_value, new_value = EXCLUDED.new_value, year = EXCLUDED.year;

-- Employee feedback with a proposed quarter value + DM reply, attached to NV008's existing ACTIVE 2026
-- employee target row.
INSERT INTO target_assignment_feedback (id, target_assignment_id, employee_id, type, comment, proposed_q4, reply,
    replied_by, replied_at, created_at)
SELECT md5('seed-2026-feedback-nv008')::uuid, ta.id, ta.employee_id, 'COMMENT'::target_assignment_feedback_type,
       'Đề xuất tăng chỉ tiêu quý 4 do nhu cầu khách hàng tăng.', 90, 'Đã ghi nhận, sẽ xem xét ở kỳ điều chỉnh giữa năm.',
       mgr.id, make_timestamptz(2026, 1, 12, 14, 0, 0), make_timestamptz(2026, 1, 11, 10, 0, 0)
FROM target_assignment ta
JOIN employee mgr ON mgr.employee_code = 'NV011'
WHERE ta.id = '5eed0000-0000-0000-0035-000000000084'
ON CONFLICT (id) DO UPDATE
SET comment = EXCLUDED.comment, reply = EXCLUDED.reply;

-- ============================================================================
-- Demo notifications (GB-914: inbox, bell dropdown, filters, deep links)
-- ============================================================================
INSERT INTO notification (id, recipient_employee_id, sender_employee_id, type, severity, title, body, related_type, related_id, created_at, read_at)
SELECT n.id::uuid, rec.id, snd.id, n.type, n.severity, n.title, n.body, n.related_type, n.related_id::uuid, now() - n.age, CASE WHEN n.is_read THEN now() - (n.age / 2) END
FROM (VALUES
    -- Demo Employee (NV012: employee@bpms.com)
    ('5eed0000-0000-0000-0050-000000000001', 'NV012', 'NV011', 'ANNOUNCEMENT', 'IMPORTANT',
     'Họp triển khai chỉ tiêu Quý 4/2026',
     'Đề nghị toàn bộ cán bộ Phòng Kinh doanh có mặt tại phòng họp tầng 3 lúc 08:30 sáng mai để rà soát tiến độ huy động vốn cuối năm.',
     NULL, NULL, interval '15 minutes', FALSE),
    ('5eed0000-0000-0000-0050-000000000003', 'NV012', 'NV011', 'WORK_ASSIGNED', 'NORMAL',
     'Bạn được giao công việc mới',
     'Demo Manager đã giao cho bạn công việc W-2026-0007 - Mở tài khoản cho KH khu Đống Đa (hạn 31/12/2026).',
     'WORK', '5eed0000-0000-0000-0016-000000000007', interval '5 hours', FALSE),
    ('5eed0000-0000-0000-0050-000000000004', 'NV012', NULL, 'TARGET_REVISED', 'IMPORTANT',
     'Chỉ tiêu của bạn đã được phân bổ',
     'Chỉ tiêu KH_MOI (Khách hàng mới) Quý 4/2026 đã được kích hoạt với định mức 30 khách hàng. Vui lòng xác nhận hoặc gửi phản hồi.',
     'TARGET', '5eed0000-0000-0000-0015-00000000000f', interval '1 day', TRUE),

    -- Teller / Sales Officer NV004 (chau.le@progest.local)
    ('5eed0000-0000-0000-0050-000000000005', 'NV004', NULL, 'TRANSACTION_MISMATCH', 'IMPORTANT',
     'Mã giao dịch đối soát không khớp',
     'Công việc W-DEMO-TX-NOTMATCHED - Classic card - codes not matched có 2 mã giao dịch không khớp Core Banking: TX-DEMO-002 (sai mã sản phẩm), TX-DEMO-003 (sai mã nhân viên).',
     'WORK', '5eed0000-0000-0000-0042-000000000002', interval '30 minutes', FALSE),
    ('5eed0000-0000-0000-0050-000000000006', 'NV004', 'NV002', 'ANNOUNCEMENT', 'NORMAL',
     'Nhắc nhở hoàn tất đối soát mã giao dịch thẻ',
     'Các giao dịch viên và chuyên viên kinh doanh kiểm tra lại các mã giao dịch đang ở trạng thái Không khớp trước kỳ khóa sổ tháng.',
     'WORK', '5eed0000-0000-0000-0016-000000000001', interval '4 hours', FALSE),

    -- Demo Manager (NV011: manager@bpms.com)
    ('5eed0000-0000-0000-0050-000000000008', 'NV011', 'NV001', 'ANNOUNCEMENT', 'IMPORTANT',
     'Rà soát hiệu chuẩn xếp hạng cấp Phòng trước hạn khóa sổ',
     'Đề nghị các Trưởng phòng hoàn tất chấm điểm định tính và gửi bảng hiệu chuẩn thứ hạng về Ban Giám đốc chi nhánh đúng hạn.',
     NULL, NULL, interval '3 hours', FALSE),
    ('5eed0000-0000-0000-0050-000000000009', 'NV011', 'NV004', 'SUBMISSION_SUBMITTED', 'NORMAL',
     'Hồ sơ kết quả chờ thẩm định',
     'Lê Minh Châu đã nộp kết quả công việc đang chờ thẩm định trong kỳ.',
     NULL, NULL, interval '1 day', TRUE),

    -- Branch Manager (NV001: an.nguyen@progest.local)
    ('5eed0000-0000-0000-0050-00000000000a', 'NV001', 'NV002', 'CALIBRATION_SUBMITTED', 'IMPORTANT',
     'Bảng hiệu chuẩn phòng ban chờ phê duyệt',
     'Trần Thị Bình đã trình duyệt bảng hiệu chuẩn xếp hạng của Phòng Kinh doanh cho kỳ đánh giá Quý 3/2026.',
     'EVALUATION_PERIOD', '5eed0000-0000-0000-000f-000000000002', interval '1 hour', FALSE),
    ('5eed0000-0000-0000-0050-00000000000b', 'NV001', NULL, 'WORK_OVERDUE', 'NORMAL',
     'Tổng hợp cảnh báo tiến độ công việc chi nhánh',
     'Hệ thống ghi nhận một số đầu việc chiến dịch sắp chạm mốc hạn chót Quý 4/2026, đề nghị theo dõi trên bảng điều hành.',
     NULL, NULL, interval '6 hours', TRUE),

    -- System Admin (NV013: sysadmin@bpms.com)
    ('5eed0000-0000-0000-0050-00000000000c', 'NV013', NULL, 'IMPORT_COMPLETED_WITH_ERRORS', 'IMPORTANT',
     'Import giao dịch Core Banking có dòng cảnh báo đối soát',
     ' Lô import demo-transactions.xlsx đã hoàn tất; một số mã giao dịch của nhân viên đang ở trạng thái lệch sản phẩm/nhân sự cần theo dõi.',
     'IMPORT_BATCH', '5eed0000-0000-0000-0040-000000000001', interval '25 minutes', FALSE),
    ('5eed0000-0000-0000-0050-00000000000d', 'NV013', 'NV001', 'KPI_APPROVED', 'IMPORTANT',
     'KPI đã được phê duyệt ban hành',
     'KPI THE_TD - Thẻ tín dụng đã được Giám đốc chi nhánh phê duyệt và có hiệu lực áp dụng.',
     'KPI', '5eed0000-0000-0000-0005-000000000007', interval '3 hours', FALSE),
    ('5eed0000-0000-0000-0050-00000000000e', 'NV013', NULL, 'SYSTEM_ALERT', 'IMPORTANT',
     'Cảnh báo bảo mật: Giám sát đăng nhập sai nhiều lần',
     'Hệ thống phát hiện phiên đăng nhập sai mật khẩu liên tiếp vượt ngưỡng cảnh báo và đã kích hoạt cơ chế khóa tạm thời tự động.',
     NULL, NULL, interval '2 days', TRUE)
) AS n(id, recipient_code, sender_code, type, severity, title, body, related_type, related_id, age, is_read)
JOIN employee rec ON rec.employee_code = n.recipient_code
LEFT JOIN employee snd ON snd.employee_code = n.sender_code
ON CONFLICT DO NOTHING;

