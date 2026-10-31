import { PagedResult } from './paged-result.model';

export type DepartmentStatus = 'ACTIVE' | 'INACTIVE' | 'ARCHIVED';
export type DepartmentLevel = 'BRANCH' | 'DEPARTMENT' | 'TEAM';
export type MemberStatus = 'ACTIVE' | 'INACTIVE' | 'ON_LEAVE' | 'TERMINATED';
export interface DepartmentSummary {
  id: string;
  code: string;
  name: string;
  levelType: DepartmentLevel;
  status: DepartmentStatus;
  parentDepartmentId: string | null;
  parentDepartmentName: string | null;
  employeeCount: number;
  childDepartmentCount: number;
  /** Derived manager; null when none can be determined (render N/A). */
  managerName: string | null;
  /** Active employees in the subtree holding the TELLER position; 0 when none. */
  tellerCount: number;
  /** Active employees in the subtree holding the SALES position; 0 when none. */
  salesCount: number;
  lastSyncAt: string;
}
export interface DepartmentDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  levelType: DepartmentLevel;
  status: DepartmentStatus;
  parentDepartmentId: string | null;
  ancestors: { id: string; code: string; name: string }[];
  manager: { id: string; employeeCode: string; fullName: string; jobPositionTitle: string } | null;
  directEmployeeCount: number;
  employeeCount: number;
  childDepartmentCount: number;
  lastSyncAt: string;
  updatedAt: string;
}
export interface DepartmentPeriod {
  id: string;
  code: string;
  name: string;
  periodType: 'YEAR' | 'HALF_YEAR' | 'QUARTER' | 'MONTH';
  startDate: string;
  endDate: string;
}
export interface DepartmentOverview {
  department: DepartmentDetail;
  period: DepartmentPeriod | null;
  availablePeriods: DepartmentPeriod[];
  metrics: {
    employeeCount: number;
    activeEmployeeCount: number;
    onLeaveEmployeeCount: number;
    openWorkCount: number;
    attentionWorkCount: number;
    pendingReviewCount: number;
  };
  performance: {
    average: number | null;
    target: number | null;
    changeFromPrevious: number | null;
    evaluatedEmployeeCount: number;
    kpis: {
      kpiDefinitionId: string;
      code: string;
      name: string;
      unit: string;
      direction: 'HIGHER_BETTER' | 'LOWER_BETTER' | 'ON_TARGET';
      targetValue: number;
      actualValue: number | null;
      achievementPercent: number | null;
    }[];
  };
  workMix: { workTypeId: string | null; code: string | null; name: string | null; count: number }[];
  operational: {
    waiting: number;
    inProgress: number;
    overdue: number;
    escalated: number | null;
    completedInPeriod: number | null;
  };
  generatedAt: string;
}
export interface DepartmentMember {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentId: string;
  departmentName: string;
  jobPositionId: string;
  jobPositionTitle: string;
  status: MemberStatus;
  openWorkCount: number;
  performanceScore: number | null;
}
export interface DepartmentFilter {
  keyword?: string;
  status?: DepartmentStatus;
  levelType?: DepartmentLevel;
  parentDepartmentId?: string;
  page?: number;
  pageSize?: number;
}
export interface DepartmentMemberFilter {
  keyword?: string;
  status?: MemberStatus;
  jobPositionId?: string;
  periodId?: string;
  page?: number;
  pageSize?: number;
}
export type DepartmentMembers = PagedResult<DepartmentMember>;
