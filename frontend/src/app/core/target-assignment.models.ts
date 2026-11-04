/** API values are UPPER_SNAKE strings (docs/api/TARGET_ASSIGNMENT_CONTRACT.md). */
export type TargetAssignmentStatus = 'DRAFT' | 'ACTIVE' | 'SUPERSEDED' | 'CLOSED';
export type TargetFeedbackStatus = 'NOT_VIEWED' | 'CONFIRMED' | 'COMMENTED';
export type TargetFeedbackType = 'CONFIRM' | 'COMMENT';

/** Exactly one of `departmentId` / `employeeId` is set. `feedbackStatus` is null for department-owned rows. */
export interface TargetAssignment {
  id: string;
  kpiDefinitionId: string;
  evaluationPeriodId: string;
  departmentId: string | null;
  employeeId: string | null;
  parentAssignmentId: string | null;
  targetValue: number;
  status: TargetAssignmentStatus;
  updatedAt: string;
  feedbackStatus: TargetFeedbackStatus | null;
  /** When the employee confirmed or commented; null unless `feedbackStatus` is CONFIRMED or COMMENTED. */
  feedbackAt: string | null;
}

export interface TargetAssignmentFilter {
  evaluationPeriodId?: string;
  kpiDefinitionId?: string;
  departmentId?: string;
  status?: TargetAssignmentStatus;
}

export interface TargetAssignmentFeedback {
  id: string;
  targetAssignmentId: string;
  employeeId: string;
  type: TargetFeedbackType;
  comment: string | null;
  createdAt: string;
}

/** `POST /target-assignments/{id}/allocations`: exactly one of `departmentId` / `employeeId` per row. */
export interface TargetAllocationRow {
  departmentId?: string;
  employeeId?: string;
  targetValue: number;
}

export interface TargetAllocationRequest {
  allocations: TargetAllocationRow[];
  changeReason?: string;
}

/** `PUT /target-assignments/{id}/allocations`: the new value of every owner already allocated. */
export interface TargetReallocationRequest {
  allocations: TargetAllocationRow[];
  changeReason: string;
}

/** One working-day bucket of `GET /target-assignments/{id}/breakdown`. */
export interface TargetBreakdownItem {
  start: string;
  end: string;
  workingDays: number;
  value: number;
}

export interface TargetBreakdown {
  targetAssignmentId: string;
  periodStart: string;
  periodEnd: string;
  totalValue: number;
  workingDays: number;
  granularity: 'MONTH' | 'WEEK' | 'DAY';
  items: TargetBreakdownItem[];
}

/** One month of a target in `GET /target-assignments/employees/{id}/suggested-daily`; `daily` / `weekly` are display figures. */
export interface SuggestedDailyMonth {
  month: string;
  workingDays: number;
  value: number;
  daily: number | null;
  weekly: number | null;
}

/** `months` is empty for a KPI that is not spread over the days (average, ratio, lower is better ...). */
export interface SuggestedDailyItem {
  targetAssignmentId: string;
  kpiDefinitionId: string;
  kpiCode: string;
  unit: string;
  isWholeNumber: boolean;
  targetValue: number;
  plannedDaily: number | null;
  plannedWeekly: number | null;
  months: SuggestedDailyMonth[];
}

export interface SuggestedDailyTarget {
  employeeId: string;
  evaluationPeriodId: string;
  periodStart: string;
  periodEnd: string;
  workingDays: number;
  items: SuggestedDailyItem[];
}
