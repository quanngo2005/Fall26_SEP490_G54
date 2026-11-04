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
