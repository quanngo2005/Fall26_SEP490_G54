import {
  PERMISSION_ACTIONS,
  PermissionAction,
  PermissionChange,
  PermissionModule,
  PermissionScope,
  RoleAccount,
  RolePermissionMatrix,
  RolePermissionMatrixSavePayload,
  RoleSummary,
} from '../role-permission.models';
import { MockRoute, fail, ok } from './mock-route';

/** Mock data for docs/api/ROLE_PERMISSION_MATRIX_CONTRACT.md until the `/roles` API ships. */
const ROLES: RoleSummary[] = [
  role('1', 'EMPLOYEE', 'Cán bộ', 'Thực hiện công việc và nộp kết quả KPI cá nhân.', 42),
  role('2', 'DEPT_MANAGER', 'Trưởng phòng', 'Giao việc, xác minh kết quả trong phòng ban.', 8),
  role('3', 'BRANCH_MANAGER', 'Giám đốc chi nhánh', 'Phê duyệt mục tiêu và đánh giá chi nhánh.', 3),
  role('4', 'HR', 'Nhân sự', 'Quản lý hồ sơ nhân viên và kỳ đánh giá.', 5),
  role('5', 'QA', 'Đảm bảo chất lượng', 'Kiểm tra tuân thủ và xử lý khiếu nại.', 4),
  role('6', 'SYSTEM_ADMIN', 'Quản trị hệ thống', 'Cấu hình hệ thống, vai trò và phân quyền.', 2),
];

const ACCOUNT_NAMES = [
  ['Nguyễn Văn An', 'Phòng Giao dịch 1'],
  ['Trần Thị Bình', 'Phòng Tín dụng'],
  ['Lê Hoàng Cường', 'Phòng Kế toán'],
  ['Phạm Thu Dung', 'Phòng Giao dịch 2'],
  ['Hoàng Minh Đức', 'Phòng Nhân sự'],
] as const;

interface ResourceTemplate {
  resource: string;
  name: string;
  actions: PermissionAction[];
  locked?: Partial<Record<PermissionAction, string>>;
}

const MODULES: { module: string; name: string; resources: ResourceTemplate[] }[] = [
  {
    module: 'kpi',
    name: 'KPI & Mục tiêu',
    resources: [
      {
        resource: 'kpiDefinition',
        name: 'Định nghĩa KPI',
        actions: ['view', 'create', 'update', 'import', 'export'],
        locked: { create: 'Chỉ Quản trị hệ thống tạo KPI' },
      },
      {
        resource: 'target',
        name: 'Mục tiêu nhân viên',
        actions: ['view', 'create', 'update', 'approve'],
      },
    ],
  },
  {
    module: 'work',
    name: 'Công việc & Kết quả',
    resources: [
      { resource: 'work', name: 'Công việc', actions: ['view', 'create', 'update', 'export'] },
      { resource: 'submission', name: 'Kết quả nộp', actions: ['view', 'create', 'approve'] },
    ],
  },
  {
    module: 'organization',
    name: 'Tổ chức',
    resources: [
      { resource: 'department', name: 'Phòng ban', actions: ['view', 'create', 'update'] },
      {
        resource: 'employee',
        name: 'Nhân viên',
        actions: ['view', 'create', 'update', 'import', 'export'],
      },
    ],
  },
];

/** Default scope per role code; `null` = not granted. */
const DEFAULT_SCOPE: Record<string, PermissionScope | null> = {
  EMPLOYEE: 'Own',
  DEPT_MANAGER: 'Dept',
  BRANCH_MANAGER: 'Branch',
  HR: 'All',
  QA: 'All',
  SYSTEM_ADMIN: 'All',
};

const matrices = new Map<string, RolePermissionMatrix>(ROLES.map((r) => [r.id, buildMatrix(r)]));

export const ROLE_MOCK_ROUTES: MockRoute[] = [
  { method: 'GET', path: /^\/roles$/, handle: () => ok(ROLES) },
  {
    method: 'GET',
    path: /^\/roles\/([^/]+)\/accounts$/,
    handle: (_request, [roleId]) => {
      const found = findRole(roleId);
      return found ? ok(accountsFor(found)) : roleNotFound();
    },
  },
  {
    method: 'GET',
    path: /^\/roles\/([^/]+)\/permission-matrix$/,
    handle: (_request, [roleId]) => {
      const matrix = matrices.get(decodeURIComponent(roleId));
      return matrix ? ok(matrix) : roleNotFound();
    },
  },
  {
    method: 'PUT',
    path: /^\/roles\/([^/]+)\/permission-matrix$/,
    handle: (request, [roleId]) => {
      const matrix = matrices.get(decodeURIComponent(roleId));
      if (!matrix) return roleNotFound();
      const payload = request.body as RolePermissionMatrixSavePayload;
      if (payload.version !== matrix.version) {
        return fail(409, 'VERSION_CONFLICT', 'Ma trận quyền đã thay đổi. Vui lòng tải lại.');
      }
      const updated = applyChanges(matrix, payload.changes);
      if (!('roleId' in updated)) return updated;
      matrices.set(matrix.roleId, updated);
      return ok(updated);
    },
  },
];

function role(
  id: string,
  code: string,
  name: string,
  description: string,
  accountCount: number,
): RoleSummary {
  return {
    id: `00000000-0000-4000-8000-00000000000${id}`,
    code,
    name,
    isSystem: true,
    description,
    accountCount,
    status: 'Active',
  };
}

function findRole(roleId: string): RoleSummary | undefined {
  return ROLES.find((r) => r.id === decodeURIComponent(roleId));
}

function roleNotFound() {
  return fail(404, 'ROLE_NOT_FOUND', 'Không tìm thấy vai trò.');
}

function accountsFor(found: RoleSummary): RoleAccount[] {
  const count = Math.min(found.accountCount, ACCOUNT_NAMES.length);
  return ACCOUNT_NAMES.slice(0, count).map(([fullName, departmentName], index) => ({
    id: `00000000-0000-4000-9000-${`${found.id.slice(-1)}${index + 1}`.padStart(12, '0')}`,
    employeeCode: `NV${found.id.slice(-1)}0${index + 1}`,
    fullName,
    departmentName,
  }));
}

function buildMatrix(found: RoleSummary): RolePermissionMatrix {
  const scope = DEFAULT_SCOPE[found.code] ?? null;
  const modules: PermissionModule[] = MODULES.map((m) => ({
    module: m.module,
    name: m.name,
    resources: m.resources.map((r) => ({
      resource: r.resource,
      name: r.name,
      cells: PERMISSION_ACTIONS.map((action) => {
        const applicable = r.actions.includes(action);
        const lockReason = r.locked?.[action] ?? null;
        const granted = applicable && (action === 'view' || found.code !== 'EMPLOYEE');
        return {
          action,
          applicable,
          scope:
            applicable && granted && (!lockReason || found.code === 'SYSTEM_ADMIN') ? scope : null,
          locked: lockReason !== null,
          lockReason,
          allowedScopes: null,
        };
      }),
    })),
  }));
  return { roleId: found.id, version: 1, modules };
}

function applyChanges(
  matrix: RolePermissionMatrix,
  changes: PermissionChange[],
): RolePermissionMatrix | ReturnType<typeof fail> {
  const next: RolePermissionMatrix = structuredClone(matrix);
  for (const change of changes) {
    const cell = next.modules
      .flatMap((m) => m.resources)
      .find((r) => r.resource === change.resource)
      ?.cells.find((c) => c.action === change.action);
    if (!cell || !cell.applicable) {
      return fail(
        400,
        'VALIDATION_ERROR',
        `Ô quyền không hợp lệ: ${change.resource}.${change.action}`,
      );
    }
    if (cell.locked) {
      return fail(403, 'FORBIDDEN', `Ô quyền bị khóa: ${change.resource}.${change.action}`);
    }
    cell.scope = change.scope;
  }
  next.version = matrix.version + 1;
  return next;
}
