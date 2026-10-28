import { PagedResult } from './paged-result.model';

/** API values are UPPER_SNAKE strings (docs/api/EMPLOYEE_CONTRACT.md). */
export type EmployeeStatus = 'ACTIVE' | 'INACTIVE' | 'ON_LEAVE' | 'TERMINATED';

export interface EmployeeDetail {
  id: string;
  employeeCode: string;
  fullName: string;
  username?: string | null;
  email: string;
  phone: string | null;
  departmentId: string;
  departmentCode: string;
  departmentName: string;
  jobPositionId: string;
  jobPositionCode: string;
  jobPositionTitle: string;
  managerId: string | null;
  managerCode: string | null;
  managerName: string | null;
  status: EmployeeStatus;
  directReportsCount: number;
  lastSyncAt: string;
  updatedAt: string;
}

export interface EmployeeFilter {
  keyword?: string;
  departmentId?: string;
  jobPositionId?: string;
  managerId?: string;
  status?: EmployeeStatus;
  page?: number;
  pageSize?: number;
}

export interface EmployeeDepartmentOption {
  id: string;
  code: string;
  name: string;
  levelType: number;
}

export interface EmployeeJobPositionOption {
  id: string;
  code: string;
  title: string;
}

export type EmployeePagedResult = PagedResult<EmployeeDetail>;
