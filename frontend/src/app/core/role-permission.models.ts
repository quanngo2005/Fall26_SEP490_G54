/** Role permission matrix (SCR-ADM-03-M2). See docs/api/ROLE_PERMISSION_MATRIX_CONTRACT.md. */
export const PERMISSION_ACTIONS = [
  'view',
  'create',
  'update',
  'approve',
  'import',
  'export',
] as const;
export type PermissionAction = (typeof PERMISSION_ACTIONS)[number];

/** Mirrors G54.BLL.Common.Constants.PermissionScopes; `null` means not granted. */
export const PERMISSION_SCOPES = ['Own', 'Dept', 'Branch', 'All'] as const;
export type PermissionScope = (typeof PERMISSION_SCOPES)[number];

export interface RoleOption {
  id: string;
  code: string;
  name: string;
  isSystem: boolean;
}

export interface PermissionCell {
  action: PermissionAction;
  /** False when the action does not apply to the resource (rendered as a grey dash). */
  applicable: boolean;
  scope: PermissionScope | null;
  /** Fixed by business rule; not editable. */
  locked: boolean;
  lockReason?: string | null;
  /** Scopes the admin may pick for this cell; defaults to all scopes. */
  allowedScopes?: PermissionScope[] | null;
}

export interface PermissionResourceRow {
  resource: string;
  name: string;
  cells: PermissionCell[];
}

export interface PermissionModule {
  module: string;
  name: string;
  resources: PermissionResourceRow[];
}

export interface RolePermissionMatrix {
  roleId: string;
  version: number;
  modules: PermissionModule[];
}

export interface PermissionChange {
  resource: string;
  action: PermissionAction;
  scope: PermissionScope | null;
}

export interface RolePermissionMatrixSavePayload {
  version: number;
  changes: PermissionChange[];
}

/** Role list row (SCR-ADM-03). `GET /roles` returns these; extra fields are optional for the selector. */
export type RoleStatus = 'Active' | 'Inactive';

export interface RoleSummary extends RoleOption {
  description?: string | null;
  accountCount: number;
  status: RoleStatus;
}

export interface RoleAccount {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentName?: string | null;
}
