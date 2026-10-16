export type DepartmentStatus = 'active' | 'inactive' | 'pending';

export interface DepartmentRef {
  readonly id: string;
  readonly name: string;
  readonly code: string;
}

export interface DepartmentManager {
  readonly employeeId: string;
  readonly employeeCode: string;
  readonly fullName: string;
  readonly positionTitle: string;
  readonly email: string;
  readonly phone: string;
}

export interface ChildDepartment extends DepartmentRef {
  readonly managerName: string;
  readonly employeeCount: number;
  readonly status: DepartmentStatus;
}

export interface PositionOption {
  readonly id: string;
  readonly name: string;
}

export interface EmployeePreview {
  readonly id: string;
  readonly code: string;
  readonly fullName: string;
  readonly positionId: string;
  readonly positionTitle: string;
  readonly status: DepartmentStatus;
  readonly managerName: string;
}

/** Which downstream screens the backend allows the current user to open. */
export interface DepartmentDetailPermissions {
  readonly canViewEmployees: boolean;
  readonly canViewKpi: boolean;
  readonly canViewWork: boolean;
  readonly canViewPerformance: boolean;
}

/** Mirrors the backend DepartmentDetailReadModel (display + navigation only). */
export interface DepartmentDetail extends DepartmentRef {
  readonly type: string;
  readonly status: DepartmentStatus;
  readonly description: string;
  readonly location: string;
  readonly createdDate: string;
  readonly headcountQuota: number;
  readonly parent: DepartmentRef | null;
  readonly ancestors: readonly DepartmentRef[];
  readonly manager: DepartmentManager | null;
  readonly employeeCount: number;
  readonly activeEmployeeCount: number;
  readonly children: readonly ChildDepartment[];
  readonly positions: readonly PositionOption[];
  readonly employeePreview: readonly EmployeePreview[];
  readonly permissions: DepartmentDetailPermissions;
}

export type DepartmentDetailViewState = 'loading' | 'success' | 'notFound' | 'forbidden' | 'error';
