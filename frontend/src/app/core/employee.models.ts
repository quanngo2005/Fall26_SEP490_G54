import { PagedResult } from './kpi-definition.models';

export type EmployeeStatus = 'ACTIVE' | 'INACTIVE' | 'ON_LEAVE' | 'TERMINATED';

export interface EmployeeDetail {
  id: string;
  employeeCode: string;
  fullName: string;
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
  status?: EmployeeStatus | string;
  page?: number;
  pageSize?: number;
}

export interface DepartmentOption {
  id: string;
  code: string;
  name: string;
}

export interface JobPositionOption {
  id: string;
  code: string;
  title: string;
  departmentId: string;
}

export interface ManagerOption {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentName: string;
}

export interface CreateEmployeeRequest {
  employeeCode: string;
  fullName: string;
  email: string;
  phone?: string;
  departmentId: string;
  jobPositionId: string;
  managerId?: string;
}

export interface UpdateEmployeeRequest {
  fullName: string;
  phone?: string;
  departmentId: string;
  jobPositionId: string;
  managerId?: string;
}

export type EmployeePagedResult = PagedResult<EmployeeDetail>;
