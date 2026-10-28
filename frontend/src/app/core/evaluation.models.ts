/** Mirrors G54.BLL.Dtos.Evaluation. See docs/api/EVALUATION_CONTRACT.md. */

export type EvaluationPeriodStatus = 'OPEN' | 'IN_REVIEW' | 'LOCKED' | 'CLOSED';

export type PerformanceEvaluationStatus =
  'NOT_STARTED' | 'DRAFT' | 'CALCULATED' | 'IN_REVIEW' | 'APPROVED' | 'DISPUTED' | 'FINAL';

export interface EvaluationPeriodOption {
  id: string;
  code: string;
  name: string;
  startDate: string;
  endDate: string;
  periodType: string;
  status: EvaluationPeriodStatus;
  isActive: boolean;
}

export interface DepartmentEmployeeEvaluation {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionTitle: string;
  jobPositionId: string;
  departmentId: string;
  departmentName: string;
  evaluationId: string | null;
  totalScore: number | null;
  evaluationStatus: PerformanceEvaluationStatus;
  proposedTier: string | null;
  calculatedAt: string | null;
  quantitativeScore: number | null;
  qualitativeScore: number | null;
  hasOpenDispute: boolean;
  pendingSubmissionCount: number;
  canEvaluate: boolean;
  updatedAt: string | null;
}
