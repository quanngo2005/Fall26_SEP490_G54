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
/** `compare` query value (docs/api/DEPARTMENT_CONTRACT.md). */
export type PerformanceCompareMode = 'PREVIOUS' | 'SAME_LAST_YEAR';
export const COMPARE_MODES: readonly PerformanceCompareMode[] = ['PREVIOUS', 'SAME_LAST_YEAR'];
/** `sort` query value of the member list; applied inside each job position. */
export type MemberSort = 'CHANGE_DESC' | 'TOTAL_ASC' | 'CODE';
export type KpiDirection = 'HIGHER_BETTER' | 'LOWER_BETTER' | 'ON_TARGET';
export type AttentionReason = 'SCORE_DROP' | 'LOW_SCORE' | 'OVERDUE_WORK';
export type MemberEvaluationStatus =
  'DRAFT' | 'CALCULATED' | 'IN_REVIEW' | 'APPROVED' | 'FINAL' | 'DISPUTED';
export interface DepartmentKpi {
  kpiDefinitionId: string;
  code: string;
  name: string;
  unit: string;
  direction: KpiDirection;
  targetValue: number;
  actualValue: number | null;
  achievementPercent: number | null;
}
export interface DepartmentGroupKpi extends DepartmentKpi {
  previousAchievementPercent: number | null;
  /** achievementPercent - previousAchievementPercent (computed by the API). */
  achievementChange: number | null;
  /** Campaign part of a SUM actual (BR-26); null otherwise. */
  campaignActualValue: number | null;
}
export interface DepartmentKpiGroup {
  /** Root node name of the evaluation profiles; null for KPIs under no root ("Other"). */
  name: string | null;
  displayOrder: number;
  weights: { jobPositionId: string; jobPositionTitle: string; weightPercent: number }[];
  kpis: DepartmentGroupKpi[];
}
export interface DepartmentPositionGroup {
  profileItemId: string;
  name: string;
  displayOrder: number;
  weightPercent: number;
  average: number | null;
  isWeak: boolean;
}
export interface DepartmentPosition {
  jobPositionId: string;
  jobPositionTitle: string;
  employeeCount: number;
  evaluatedEmployeeCount: number;
  average: number | null;
  previousAverage: number | null;
  changeFromPrevious: number | null;
  groups: DepartmentPositionGroup[];
}
export interface DepartmentAttentionMember {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionTitle: string;
  totalScore: number;
  previousTotalScore: number | null;
  changeFromPrevious: number | null;
  weakestGroup: { name: string; score: number } | null;
  overdueWorkCount: number;
  reasons: AttentionReason[];
}
export interface DepartmentChildUnit {
  id: string;
  code: string;
  name: string;
  levelType: DepartmentLevel;
  employeeCount: number;
  evaluatedEmployeeCount: number;
  average: number | null;
  previousAverage: number | null;
  changeFromPrevious: number | null;
}
export interface DepartmentTrendPoint {
  periodId: string;
  periodName: string;
  average: number | null;
}
export interface DepartmentOverview {
  department: DepartmentDetail;
  period: DepartmentPeriod | null;
  comparePeriod: DepartmentPeriod | null;
  availablePeriods: DepartmentPeriod[];
  metrics: {
    employeeCount: number;
    activeEmployeeCount: number;
    onLeaveEmployeeCount: number;
    openWorkCount: number;
    attentionWorkCount: number;
    pendingReviewCount: number;
    declinedEmployeeCount: number | null;
    comparedEmployeeCount: number | null;
    declinedChildUnitCount: number | null;
  };
  performance: {
    average: number | null;
    previousAverage: number | null;
    target: number | null;
    changeFromPrevious: number | null;
    evaluatedEmployeeCount: number;
    kpis: DepartmentKpi[];
    trend: DepartmentTrendPoint[];
    kpiGroups: DepartmentKpiGroup[];
    positions: DepartmentPosition[];
  };
  workMix: { workTypeId: string | null; code: string | null; name: string | null; count: number }[];
  operational: {
    waiting: number;
    /** IN_PROGRESS + HANDED_OVER that are not overdue (disjoint from `overdue`). */
    inProgress: number;
    overdue: number;
    /** No data source yet: always null, the tile is hidden. */
    escalated: number | null;
    completedInPeriod: number | null;
    pendingReview: number;
  };
  attentionMembers: DepartmentAttentionMember[];
  childUnits: DepartmentChildUnit[];
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
  evaluationStatus: MemberEvaluationStatus | null;
  previousPerformanceScore: number | null;
  changeFromPrevious: number | null;
  groups: {
    profileItemId: string;
    name: string;
    displayOrder: number;
    weightPercent: number;
    score: number | null;
  }[];
  weakestProfileItemId: string | null;
  overdueWorkCount: number;
  pendingReviewCount: number;
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
  compare?: PerformanceCompareMode;
  sort?: MemberSort;
  page?: number;
  pageSize?: number;
}
export type DepartmentMembers = PagedResult<DepartmentMember>;
