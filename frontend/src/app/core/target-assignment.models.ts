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
