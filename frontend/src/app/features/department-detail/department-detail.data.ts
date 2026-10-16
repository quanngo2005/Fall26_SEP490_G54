import { ChildDepartment, DepartmentDetail, EmployeePreview } from './department-detail.model';

/**
 * Sample read models used only when the backend is unreachable (local UI review).
 * Real authorization and data always come from the API.
 */
const POSITIONS = [
  { id: 'pos-head', name: 'Trưởng phòng' },
  { id: 'pos-lead', name: 'Trưởng tổ' },
  { id: 'pos-rm', name: 'Chuyên viên QHKH' },
];

const EMPLOYEES: readonly EmployeePreview[] = [
  {
    id: 'emp-00421',
    code: 'NV-00421',
    fullName: 'Trần Thu Hương',
    positionId: 'pos-head',
    positionTitle: 'Trưởng phòng Khách hàng Cá nhân',
    status: 'active',
    managerName: 'Nguyễn Thị Anh (Giám đốc Khối)',
  },
  {
    id: 'emp-00254',
    code: 'NV-00254',
    fullName: 'Lê Hoàng Mai',
    positionId: 'pos-lead',
    positionTitle: 'Trưởng tổ Khách hàng Ưu tiên',
    status: 'active',
    managerName: 'Trần Thu Hương (Trưởng phòng)',
  },
  {
    id: 'emp-00312',
    code: 'NV-00312',
    fullName: 'Nguyễn Viên An',
    positionId: 'pos-lead',
    positionTitle: 'Trưởng tổ Phát triển KH Mới',
    status: 'active',
    managerName: 'Trần Thu Hương (Trưởng phòng)',
  },
  {
    id: 'emp-00345',
    code: 'NV-00345',
    fullName: 'Vũ Đình Trọng',
    positionId: 'pos-lead',
    positionTitle: 'Trưởng tổ Sản phẩm Vay & Thẻ',
    status: 'active',
    managerName: 'Trần Thu Hương (Trưởng phòng)',
  },
  {
    id: 'emp-00298',
    code: 'NV-00298',
    fullName: 'Đặng Thảo Vy',
    positionId: 'pos-rm',
    positionTitle: 'Chuyên viên QHKH Ưu tiên (Premier RM)',
    status: 'pending',
    managerName: 'Lê Hoàng Mai (Trưởng tổ)',
  },
];

const CHILDREN: readonly ChildDepartment[] = [
  {
    id: 'dept-pri',
    name: 'Tổ Khách hàng Ưu tiên',
    code: 'RB-PRI-01',
    managerName: 'Lê Hoàng Mai',
    employeeCount: 8,
    status: 'active',
  },
  {
    id: 'dept-acq',
    name: 'Tổ Phát triển KH Mới',
    code: 'RB-ACQ-02',
    managerName: 'Nguyễn Viên An',
    employeeCount: 11,
    status: 'active',
  },
  {
    id: 'dept-lnd',
    name: 'Tổ Sản phẩm Vay & Thẻ',
    code: 'RB-LND-03',
    managerName: 'Vũ Đình Trọng',
    employeeCount: 9,
    status: 'active',
  },
];

const SALES_BLOCK = { id: 'dept-sales', name: 'Khối Bán hàng & Dịch vụ', code: 'RB-SALES' };

const ALL_PERMISSIONS = {
  canViewEmployees: true,
  canViewKpi: true,
  canViewWork: true,
  canViewPerformance: true,
};

const PERSONAL_BANKING: DepartmentDetail = {
  id: 'dept-pkh-cn',
  name: 'Phòng Khách hàng Cá nhân',
  code: 'RB-RM-01',
  type: 'Phòng nghiệp vụ (Cấp 2)',
  status: 'active',
  description:
    'Retail Banking Operations — Khối Bán hàng & Dịch vụ Tài chính Cá nhân. Chịu trách nhiệm hoạch định chiến lược và phát triển nhóm khách hàng cá nhân, thiết kế gói sản phẩm tín dụng, huy động vốn và quản trị danh mục khách hàng.',
  location: 'Tầng 8, HQ Tower',
  createdDate: '2018-03-10',
  headcountQuota: 30,
  parent: SALES_BLOCK,
  ancestors: [SALES_BLOCK],
  manager: {
    employeeId: 'emp-00421',
    employeeCode: 'NV-00421',
    fullName: 'Trần Thu Hương',
    positionTitle: 'Trưởng phòng Khách hàng Cá nhân',
    email: 'huong.tt@enterprise-core.vn',
    phone: 'Ext. 4211',
  },
  employeeCount: 28,
  activeEmployeeCount: 26,
  children: CHILDREN,
  positions: POSITIONS,
  employeePreview: EMPLOYEES,
  permissions: ALL_PERMISSIONS,
};

const SALES_BLOCK_DETAIL: DepartmentDetail = {
  ...PERSONAL_BANKING,
  ...SALES_BLOCK,
  type: 'Khối nghiệp vụ (Cấp 1)',
  description: 'Khối quản lý các phòng bán hàng và dịch vụ khách hàng cá nhân và doanh nghiệp.',
  parent: null,
  ancestors: [],
  headcountQuota: 80,
  employeeCount: 72,
  activeEmployeeCount: 70,
  manager: {
    employeeId: 'emp-00100',
    employeeCode: 'NV-00100',
    fullName: 'Nguyễn Thị Anh',
    positionTitle: 'Giám đốc Khối',
    email: 'anh.nt@enterprise-core.vn',
    phone: 'Ext. 4001',
  },
  children: [
    {
      id: 'dept-pkh-cn',
      name: 'Phòng Khách hàng Cá nhân',
      code: 'RB-RM-01',
      managerName: 'Trần Thu Hương',
      employeeCount: 28,
      status: 'active',
    },
  ],
  employeePreview: [],
};

const CHILD_DETAILS: readonly DepartmentDetail[] = CHILDREN.map((child) => ({
  ...PERSONAL_BANKING,
  ...child,
  type: 'Tổ chuyên môn (Cấp 3)',
  description: `${child.name} thuộc Phòng Khách hàng Cá nhân.`,
  parent: { id: PERSONAL_BANKING.id, name: PERSONAL_BANKING.name, code: PERSONAL_BANKING.code },
  ancestors: [SALES_BLOCK, PERSONAL_BANKING],
  manager: null,
  headcountQuota: child.employeeCount,
  activeEmployeeCount: child.employeeCount,
  children: [],
  employeePreview: [],
}));

export const SAMPLE_DEPARTMENTS: readonly DepartmentDetail[] = [
  PERSONAL_BANKING,
  SALES_BLOCK_DETAIL,
  ...CHILD_DETAILS,
];
