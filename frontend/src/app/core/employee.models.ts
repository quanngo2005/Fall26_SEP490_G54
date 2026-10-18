import { PagedResult } from './api.models';

/**
 * Canonical values are UPPER_SNAKE; the list order matches the backend enum declaration because the
 * API currently serializes the enum as an ordinal (see enum-wire.ts, docs/api/EMPLOYEE_CONTRACT.md).
 */
export const EMPLOYEE_STATUSES = ['ACTIVE', 'INACTIVE', 'ON_LEAVE', 'TERMINATED'] as const;
export type EmployeeStatus = (typeof EMPLOYEE_STATUSES)[number];

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
  status: EmployeeStatus | null;
  directReportsCount: number;
  lastSyncAt: string;
  updatedAt: string;
}

export interface EmployeeFilter {
  keyword?: string;
  departmentId?: string;
  jobPositionId?: string;
  managerId?: string;
  page?: number;
  pageSize?: number;
}

export type EmployeePagedResult = PagedResult<EmployeeDetail>;
