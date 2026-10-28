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
--   └─ Phòng Giao dịch   -> Quầy 1, Quầy 2            (TELLER)
-- ---------------------------------------------------------------------------
INSERT INTO department (id, code, name, description, parent_department_id, level_type) VALUES
 ('5eed0000-0000-0000-0001-000000000001', 'CN-HN',       'Chi nhánh Hà Nội',      'Chi nhánh trọng tâm của hệ thống', NULL,                                   'BRANCH'),
 ('5eed0000-0000-0000-0001-000000000002', 'CN-HN-KD',    'Phòng Kinh doanh',      'Khối bán hàng (Sales)',            '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-000000000003', 'CN-HN-GD',    'Phòng Giao dịch',       'Khối giao dịch viên (Teller)',     '5eed0000-0000-0000-0001-000000000001', 'DEPARTMENT'),
 ('5eed0000-0000-0000-0001-000000000004', 'CN-HN-KD-N1', 'Nhóm Kinh doanh 1',     'Sales - khách hàng cá nhân',       '5eed0000-0000-0000-0001-000000000002', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000005', 'CN-HN-KD-N2', 'Nhóm Kinh doanh 2',     'Sales - hộ kinh doanh',            '5eed0000-0000-0000-0001-000000000002', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000006', 'CN-HN-GD-Q1', 'Nhóm Giao dịch Quầy 1', 'Teller - quầy tiền gửi',           '5eed0000-0000-0000-0001-000000000003', 'TEAM'),
 ('5eed0000-0000-0000-0001-000000000007', 'CN-HN-GD-Q2', 'Nhóm Giao dịch Quầy 2', 'Teller - quầy dịch vụ',            '5eed0000-0000-0000-0001-000000000003', 'TEAM')
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
 ('5eed0000-0000-0000-0005-000000000005', '5eed0000-0000-0000-0003-000000000002', 'TY_LE_LOI', 'Tỷ lệ lỗi giao dịch',        'Giao dịch lỗi / tổng giao dịch', 'giao dịch lỗi / tổng giao dịch', '%',          'RATIO', 'LOWER_BETTER',  120),
 ('5eed0000-0000-0000-0005-000000000006', '5eed0000-0000-0000-0003-000000000003', 'TUAN_THU',  'Tỷ lệ hồ sơ đúng quy trình', 'Hồ sơ đạt / tổng hồ sơ',         'hồ sơ đạt / tổng hồ sơ',         '%',          'RATIO', 'HIGHER_BETTER', 100)
ON CONFLICT DO NOTHING;

-- Only two job positions; managers hold one of them and get the MANAGER role.
INSERT INTO job_position (id, code, title, description) VALUES
 ('5eed0000-0000-0000-0006-000000000001', 'SALES',  'Chuyên viên Kinh doanh', 'Bán hàng, phát triển khách hàng'),
 ('5eed0000-0000-0000-0006-000000000002', 'TELLER', 'Giao dịch viên',         'Giao dịch tại quầy')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Evaluation profiles, one per position (ACTIVE profiles must total 100% - checked at commit)
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_profile (id, job_position_id, code, name, description, version, effective_from, status) VALUES
 ('5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0006-000000000001', 'EP-SALES',  'Hồ sơ đánh giá Sales',          'Chuyên viên kinh doanh',  1, '2026-01-01', 'ACTIVE'),
 ('5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0006-000000000002', 'EP-TELLER', 'Hồ sơ đánh giá Giao dịch viên', 'Giao dịch viên tại quầy', 1, '2026-01-01', 'ACTIVE')
ON CONFLICT DO NOTHING;

-- SALES: KD 70 / CLDV 20 / TT 10 - TELLER: KD 20 / CLDV 50 / TT 30
INSERT INTO evaluation_profile_item (id, evaluation_profile_id, kpi_group_id, weight_percent, display_order) VALUES
 ('5eed0000-0000-0000-0004-000000000001', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0003-000000000001', 70, 1),
 ('5eed0000-0000-0000-0004-000000000002', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0003-000000000002', 20, 2),
 ('5eed0000-0000-0000-0004-000000000003', '5eed0000-0000-0000-0002-000000000001', '5eed0000-0000-0000-0003-000000000003', 10, 3),
 ('5eed0000-0000-0000-0004-000000000004', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0003-000000000001', 20, 1),
 ('5eed0000-0000-0000-0004-000000000005', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0003-000000000002', 50, 2),
 ('5eed0000-0000-0000-0004-000000000006', '5eed0000-0000-0000-0002-000000000002', '5eed0000-0000-0000-0003-000000000003', 30, 3)
ON CONFLICT DO NOTHING;

-- NV001 giám đốc CN, NV002 trưởng phòng KD, NV003 trưởng phòng GD; managers first.
INSERT INTO employee (id, employee_code, full_name, email, phone, department_id, job_position_id, manager_id) VALUES
 ('5eed0000-0000-0000-0007-000000000001', 'NV001', 'Nguyễn Văn An',  'an.nguyen@progest.local', '0901000001', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-0006-000000000001', NULL),
 ('5eed0000-0000-0000-0007-000000000002', 'NV002', 'Trần Thị Bình',  'binh.tran@progest.local', '0901000002', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000003', 'NV003', 'Đỗ Thị Lan',     'lan.do@progest.local',    '0901000003', '5eed0000-0000-0000-0001-000000000003', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-000000000004', 'NV004', 'Lê Minh Châu',   'chau.le@progest.local',   '0901000004', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000005', 'NV005', 'Phạm Quốc Dũng', 'dung.pham@progest.local', '0901000005', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000006', 'NV006', 'Vũ Đức Khang',   'khang.vu@progest.local',  '0901000006', '5eed0000-0000-0000-0001-000000000005', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000007', 'NV007', 'Ngô Thanh Mai',  'mai.ngo@progest.local',   '0901000007', '5eed0000-0000-0000-0001-000000000005', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-000000000008', 'NV008', 'Hoàng Thu Hà',   'ha.hoang@progest.local',  '0901000008', '5eed0000-0000-0000-0001-000000000006', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-000000000009', 'NV009', 'Bùi Văn Nam',    'nam.bui@progest.local',   '0901000009', '5eed0000-0000-0000-0001-000000000006', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-00000000000a', 'NV010', 'Đặng Ngọc Oanh', 'oanh.dang@progest.local', '0901000010', '5eed0000-0000-0000-0001-000000000007', '5eed0000-0000-0000-0006-000000000002', '5eed0000-0000-0000-0007-000000000003'),
 ('5eed0000-0000-0000-0007-00000000000b', 'NV011', 'Demo Manager',    'manager@bpms.com',        '0901000011', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001'),
 ('5eed0000-0000-0000-0007-00000000000c', 'NV012', 'Demo Employee',   'employee@bpms.com',       '0901000012', '5eed0000-0000-0000-0001-000000000004', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000002'),
 ('5eed0000-0000-0000-0007-00000000000d', 'NV013', 'Demo System Admin', 'sysadmin@bpms.com',     '0901000013', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-0006-000000000001', NULL),
 ('5eed0000-0000-0000-0007-00000000000e', 'NV014', 'Demo Verifier',     'verifier@bpms.com',     '0901000014', '5eed0000-0000-0000-0001-000000000002', '5eed0000-0000-0000-0006-000000000001', '5eed0000-0000-0000-0007-000000000001')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Access control (looked up by code so a pre-existing ADMIN role is reused)
-- ---------------------------------------------------------------------------
INSERT INTO role (id, code, name, description) VALUES
 ('5eed0000-0000-0000-0008-000000000001', 'ADMIN',    'Administrator', 'Quản trị hệ thống'),
 ('5eed0000-0000-0000-0008-000000000002', 'MANAGER',  'Quản lý',       'Giao chỉ tiêu, theo dõi đơn vị'),
 ('5eed0000-0000-0000-0008-000000000003', 'VERIFIER', 'Người duyệt',   'Thẩm định kết quả công việc'),
 ('5eed0000-0000-0000-0008-000000000004', 'EMPLOYEE', 'Nhân viên',     'Thực hiện và nộp kết quả công việc'),
 ('5eed0000-0000-0000-0008-000000000005', 'SYSTEM_ADMIN',   'System Administrator', 'Quản trị hệ thống'),
 ('5eed0000-0000-0000-0008-000000000006', 'BRANCH_MANAGER', 'Giám đốc chi nhánh',   'Quản lý chi nhánh và các phòng ban trực thuộc'),
 ('5eed0000-0000-0000-0008-000000000007', 'DEPT_MANAGER',   'Trưởng phòng',         'Quản lý phòng ban của mình')
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
 ('5eed0000-0000-0000-0009-00000000000f', 'CAMPAIGN_READ',       'Xem chiến dịch',       'campaign',     'read'),
 ('5eed0000-0000-0000-0009-000000000010', 'CAMPAIGN_MANAGE',     'Quản lý chiến dịch',   'campaign',     'manage'),
 ('5eed0000-0000-0000-0009-000000000011', 'HANDOVER_CREATE',     'Yêu cầu bàn giao',     'handover',     'create'),
 ('5eed0000-0000-0000-0009-000000000012', 'HANDOVER_SET_SHARE',  'Đặt tỷ lệ chia điểm khi bàn giao', 'handover', 'set_share'),
 ('5eed0000-0000-0000-0009-000000000013', 'HANDOVER_RESPOND',    'Chấp nhận hoặc từ chối bàn giao', 'handover', 'respond'),
 ('5eed0000-0000-0000-0009-000000000014', 'HANDOVER_MANAGE',     'Quản lý duyệt, đổi người nhận hoặc từ chối bàn giao', 'handover', 'manage'),
 ('5eed0000-0000-0000-0009-000000000015', 'ORGANIZATION_READ',    'Xem tổ chức (phòng ban, vị trí công việc)', 'organization', 'read'),
 ('5eed0000-0000-0000-0009-000000000016', 'JOB_POSITION_MANAGE', 'Quản lý loại công việc của vị trí công việc', 'job-position', 'manage')
ON CONFLICT DO NOTHING;

INSERT INTO role_permission (role_id, permission_id, scope)
SELECT r.id, p.id, m.scope
FROM (VALUES
    ('ADMIN', 'WORK_READ', 'All'), ('ADMIN', 'WORK_CREATE', 'All'), ('ADMIN', 'WORK_UPDATE', 'All'),
    ('ADMIN', 'SUBMISSION_READ', 'All'), ('ADMIN', 'SUBMISSION_CREATE', 'All'), ('ADMIN', 'VERIFICATION_DECIDE', 'All'),
    ('ADMIN', 'KPI_READ', 'All'), ('ADMIN', 'KPI_MANAGE', 'All'), ('ADMIN', 'TARGET_MANAGE', 'All'), ('ADMIN', 'EVALUATION_READ', 'All'),
    ('ADMIN', 'EMPLOYEE_READ', 'All'), ('ADMIN', 'EVALUATION_SUBMIT', 'All'), ('ADMIN', 'EVALUATION_CALIBRATE', 'All'),
    ('ADMIN', 'EVALUATION_FINALIZE', 'All'), ('ADMIN', 'CAMPAIGN_READ', 'All'), ('ADMIN', 'CAMPAIGN_MANAGE', 'All'), 
    ('ADMIN', 'HANDOVER_CREATE', 'All'), ('ADMIN', 'HANDOVER_SET_SHARE', 'All'),
    ('ADMIN', 'HANDOVER_RESPOND', 'All'), ('ADMIN', 'HANDOVER_MANAGE', 'All'),
    ('MANAGER', 'WORK_READ', 'Department'), ('MANAGER', 'WORK_CREATE', 'Department'), ('MANAGER', 'WORK_UPDATE', 'Department'),
    ('MANAGER', 'SUBMISSION_READ', 'Department'), ('MANAGER', 'VERIFICATION_DECIDE', 'Department'),
    ('MANAGER', 'KPI_READ', 'All'), ('MANAGER', 'TARGET_MANAGE', 'Department'), ('MANAGER', 'EVALUATION_READ', 'Department'),
    ('MANAGER', 'EMPLOYEE_READ', 'Department'), ('MANAGER', 'EVALUATION_SUBMIT', 'Department'), ('MANAGER', 'EVALUATION_CALIBRATE', 'Department'),
    ('MANAGER', 'CAMPAIGN_READ', 'Department'), ('MANAGER', 'CAMPAIGN_MANAGE', 'Department'), ('MANAGER', 'HANDOVER_CREATE', 'Own'), ('MANAGER', 'HANDOVER_SET_SHARE', 'Own'), ('MANAGER', 'HANDOVER_RESPOND', 'Own'), ('MANAGER', 'HANDOVER_MANAGE', 'Own'),
    ('VERIFIER', 'SUBMISSION_READ', 'Department'), ('VERIFIER', 'VERIFICATION_DECIDE', 'Department'), ('VERIFIER', 'KPI_READ', 'All'),
    ('EMPLOYEE', 'WORK_READ', 'Own'), ('EMPLOYEE', 'WORK_UPDATE', 'Own'), ('EMPLOYEE', 'SUBMISSION_READ', 'Own'),
    ('EMPLOYEE', 'SUBMISSION_CREATE', 'Own'), ('EMPLOYEE', 'KPI_READ', 'All'), ('EMPLOYEE', 'EVALUATION_READ', 'Own'),
    ('EMPLOYEE', 'EMPLOYEE_READ', 'Own'), ('EMPLOYEE', 'HANDOVER_CREATE', 'Own'), ('EMPLOYEE', 'HANDOVER_RESPOND', 'Own'),
    ('SYSTEM_ADMIN', 'ORGANIZATION_READ', 'All'), ('ADMIN', 'ORGANIZATION_READ', 'All'), ('BRANCH_MANAGER', 'ORGANIZATION_READ', 'Branch'),
    ('DEPT_MANAGER', 'ORGANIZATION_READ', 'Dept'), ('MANAGER', 'ORGANIZATION_READ', 'Dept'),
    -- Managers only: the system administrator administers the system, not the organization's job positions.
    ('BRANCH_MANAGER', 'JOB_POSITION_MANAGE', 'Branch'), ('DEPT_MANAGER', 'JOB_POSITION_MANAGE', 'Dept'), ('MANAGER', 'JOB_POSITION_MANAGE', 'Dept')
) AS m(role_code, permission_code, scope)
JOIN role r ON r.code = m.role_code
JOIN permission p ON p.code = m.permission_code
ON CONFLICT DO NOTHING;

-- Each organization role is usable on its own (one role per account). The same copy the migration
-- GrantWorkAccessToOrganizationRoles performs, repeated here because a fresh database has no MANAGER/ADMIN grants
-- when migrations run. DEPT_MANAGER = MANAGER; BRANCH_MANAGER = MANAGER with department scopes widened to Branch;
-- SYSTEM_ADMIN = ADMIN without the development-only FULL_ACCESS wildcard.
INSERT INTO role_permission (role_id, permission_id, scope)
SELECT target.id, rp.permission_id, rp.scope
FROM role_permission rp
JOIN role source ON source.id = rp.role_id AND source.code = 'MANAGER'
JOIN role target ON target.code = 'DEPT_MANAGER'
ON CONFLICT DO NOTHING;

INSERT INTO role_permission (role_id, permission_id, scope)
SELECT target.id, rp.permission_id,
       CASE WHEN lower(rp.scope) IN ('dept', 'department') THEN 'Branch' ELSE rp.scope END
FROM role_permission rp
JOIN role source ON source.id = rp.role_id AND source.code = 'MANAGER'
JOIN role target ON target.code = 'BRANCH_MANAGER'
ON CONFLICT DO NOTHING;

INSERT INTO role_permission (role_id, permission_id, scope)
SELECT target.id, rp.permission_id, rp.scope
FROM role_permission rp
JOIN permission p ON p.id = rp.permission_id AND p.code <> 'FULL_ACCESS'
JOIN role source ON source.id = rp.role_id AND source.code = 'ADMIN'
JOIN role target ON target.code = 'SYSTEM_ADMIN'
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Work type catalog
-- ---------------------------------------------------------------------------
INSERT INTO work_type (id, code, name, description) VALUES
 ('5eed0000-0000-0000-000c-000000000001', 'MO_TAI_KHOAN', 'Mở tài khoản khách hàng', NULL),
 ('5eed0000-0000-0000-000c-000000000002', 'HUY_DONG',     'Huy động tiền gửi',       NULL),
 ('5eed0000-0000-0000-000c-000000000003', 'GIAI_NGAN',    'Giải ngân khoản vay',     NULL),
 ('5eed0000-0000-0000-000c-000000000004', 'GIAO_DICH',    'Giao dịch tại quầy',      NULL)
ON CONFLICT DO NOTHING;

-- Fixed ids are required: the overlap trigger excludes rows with the same id.
INSERT INTO work_type_kpi (id, work_type_id, kpi_definition_id, is_required, effective_from) VALUES
 ('5eed0000-0000-0000-000d-000000000001', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0005-000000000002', TRUE,  '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000002', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0005-000000000006', FALSE, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000003', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0005-000000000001', TRUE,  '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000004', '5eed0000-0000-0000-000c-000000000003', '5eed0000-0000-0000-0005-000000000003', TRUE,  '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000005', '5eed0000-0000-0000-000c-000000000003', '5eed0000-0000-0000-0005-000000000006', FALSE, '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000006', '5eed0000-0000-0000-000c-000000000004', '5eed0000-0000-0000-0005-000000000004', TRUE,  '2026-01-01'),
 ('5eed0000-0000-0000-000d-000000000007', '5eed0000-0000-0000-000c-000000000004', '5eed0000-0000-0000-0005-000000000005', TRUE,  '2026-01-01')
ON CONFLICT DO NOTHING;

INSERT INTO job_position_work_type (job_position_id, work_type_id)
SELECT jp.id, wt.id
FROM (VALUES
    ('SALES', 'MO_TAI_KHOAN'), ('SALES', 'HUY_DONG'), ('SALES', 'GIAI_NGAN'),
    ('TELLER', 'MO_TAI_KHOAN'), ('TELLER', 'GIAO_DICH')
) AS m(position_code, work_type_code)
JOIN job_position jp ON jp.code = m.position_code
JOIN work_type wt ON wt.code = m.work_type_code
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- Evaluation periods: 2026 -> H2 -> Q4 -> Oct/Nov/Dec -> every day of Q4
-- ---------------------------------------------------------------------------
INSERT INTO evaluation_period (id, code, name, period_type, parent_period_id, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-000f-000000000001', '2026',    'Năm 2026',          'YEAR',      NULL,                                   '2026-01-01', '2026-12-31', 'OPEN'),
 ('5eed0000-0000-0000-000f-000000000002', '2026-H2', '6 tháng cuối 2026', 'HALF_YEAR', '5eed0000-0000-0000-000f-000000000001', '2026-07-01', '2026-12-31', 'OPEN'),
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

INSERT INTO business_campaign (id, business_objective_id, code, name, description, start_date, end_date, status) VALUES
 ('5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0010-000000000001', 'CMP-2026-Q4-HDV', 'Chiến dịch huy động tiền gửi Q4/2026', 'Đẩy mạnh huy động và mở mới khách hàng cuối năm', '2026-10-01', '2026-12-31', 'RUNNING')
ON CONFLICT DO NOTHING;

INSERT INTO campaign_work_type (id, business_campaign_id, work_type_id) VALUES
 ('5eed0000-0000-0000-0013-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-000c-000000000001'),
 ('5eed0000-0000-0000-0013-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-000c-000000000002')
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
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, employee_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, status, assigned_at, approved_at, change_reason)
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
       t.reason
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

-- ---------------------------------------------------------------------------
-- Work items (open, ready for submission / verification flows)
-- ---------------------------------------------------------------------------
WITH seed_work (id, work_type_id, business_campaign_id, assignee_employee_id, code, title, description, priority, estimated_hours, planned_start_date, planned_end_date, status) AS (VALUES
 ('5eed0000-0000-0000-0016-000000000001', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000004', 'W-2026-0001', 'Mở tài khoản cho nhóm KH khu Cầu Giấy',  NULL, 'HIGH',     16,  '2026-10-01', '2026-10-31', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000002', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000004', 'W-2026-0002', 'Huy động tiền gửi KH hiện hữu',           NULL, 'MEDIUM',   24,  '2026-10-05', '2026-11-30', 'NEW'),
 ('5eed0000-0000-0000-0016-000000000003', '5eed0000-0000-0000-000c-000000000002', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000006', 'W-2026-0003', 'Huy động tiền gửi hộ kinh doanh chợ Hôm', NULL, 'HIGH',     24,  '2026-10-01', '2026-11-15', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000004', '5eed0000-0000-0000-000c-000000000003', NULL,                                   '5eed0000-0000-0000-0007-000000000007', 'W-2026-0004', 'Giải ngân vay tiêu dùng hồ sơ A102',      NULL, 'CRITICAL', 8,   '2026-10-10', '2026-10-20', 'NEW'),
 ('5eed0000-0000-0000-0016-000000000005', '5eed0000-0000-0000-000c-000000000004', NULL,                                   '5eed0000-0000-0000-0007-000000000008', 'W-2026-0005', 'Giao dịch tại quầy tháng 10',             NULL, 'MEDIUM',   160, '2026-10-01', '2026-10-31', 'IN_PROGRESS'),
 ('5eed0000-0000-0000-0016-000000000006', '5eed0000-0000-0000-000c-000000000001', '5eed0000-0000-0000-0012-000000000001', '5eed0000-0000-0000-0007-000000000009', 'W-2026-0006', 'Mở tài khoản tại quầy cho KH vãng lai',   NULL, 'LOW',      40,  '2026-10-01', '2026-12-31', 'IN_PROGRESS')
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
INSERT INTO target_assignment (id, business_objective_id, kpi_definition_id, department_id, evaluation_period_id, parent_assignment_id, split_dimension, target_value, version, status, assigned_at, approved_at, created_at)
VALUES
 -- L1: Branch-Year 2025
 ('5eed0000-0000-0000-0025-000000000001', NULL, '5eed0000-0000-0000-0005-000000000001', '5eed0000-0000-0000-0001-000000000001', '5eed0000-0000-0000-002f-000000000001', NULL, NULL, 1000, 1, 'ACTIVE'::target_assignment_status, make_timestamp(2025, 1, 1, 8, 0, 0), make_timestamp(2025, 1, 15, 17, 0, 0), make_timestamp(2025, 1, 1, 8, 0, 0)),
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
