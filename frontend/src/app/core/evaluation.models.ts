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

export interface EvaluationEmployee {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentName: string;
  departmentId: string;
  jobPositionTitle: string;
  jobPositionId: string;
}

export interface EvaluationPeriod {
  id: string;
  code: string;
  name: string;
  startDate: string;
  endDate: string;
  periodType: string;
  status: EvaluationPeriodStatus;
}

export interface EvaluationProfileSummary {
  id: string;
  code: string;
  name: string;
  version: number;
}

export interface EvaluationKpiItem {
  kpiDefinitionId: string;
  kpiCode: string;
  kpiName: string;
  unit: string;
  /** Share of the root group in percent; the active KPIs of a group add up to 100. */
  weightPercent: number;
  targetValue: number | null;
  /** Null when the KPI has no progress in the period (not applicable, never 0). */
  actualValue: number | null;
  achievementPercent: number | null;
  componentScore: number | null;
}

export interface EvaluationCriterionGroup {
  /** The root node of the profile tree; also the key of the qualitative score. */
  evaluationProfileItemId: string;
  groupCode: string;
  groupName: string;
  weightPercent: number;
  /** Null when no KPI of the group can be scored. */
  groupScore: number | null;
  kpiItems: EvaluationKpiItem[];
  qualitativeScore: number | null;
  qualitativeNotes: string | null;
  isQualitative: boolean;
}

export interface EvaluationRiskEvent {
  id: string;
  code: string;
  eventType: string;
  description: string;
  severity: string;
  penaltyPoints: number;
  occurredAt: string;
}

/** The total, tier and weighted score are null when no group can be scored (not applicable, never 0). */
export interface EvaluationCalculationSummary {
  businessScore: number | null;
  serviceQualityScore: number | null;
  operationsScore: number | null;
  totalRiskPenalty: number;
  totalScore: number | null;
  proposedTier: string | null;
  weightedScore: number | null;
}

export interface PerformanceEvaluationState {
  id: string | null;
  totalScore: number | null;
  status: PerformanceEvaluationStatus;
  reviewerEmployeeId: string | null;
  reviewerName: string | null;
  calculatedAt: string | null;
  approvedAt: string | null;
  comment: string | null;
  coachingPlan: string | null;
  coachingRequired: boolean;
}

export interface PreviousPeriodSummary {
  periodCode: string;
  totalScore: number;
  tier: string;
  /** Null when the current total is not available. */
  scoreDifference: number | null;
}

export interface EvaluationFormModel {
  employee: EvaluationEmployee;
  period: EvaluationPeriod;
  profile: EvaluationProfileSummary;
  criteriaGroups: EvaluationCriterionGroup[];
  riskEvents: EvaluationRiskEvent[];
  summary: EvaluationCalculationSummary;
  currentEvaluation: PerformanceEvaluationState;
  canEdit: boolean;
  pendingSubmissionCount: number;
  openAppealCount: number;
  hasOpenDispute: boolean;
  previousPeriod: PreviousPeriodSummary | null;
}

export interface QualitativeScoreInput {
  evaluationProfileItemId: string;
  score: number;
  notes?: string | null;
}

export interface SubmitEvaluationRequest {
  employeeId: string;
  periodId: string;
  comment: string | null;
  coachingPlan?: string | null;
  coachingRequired?: boolean;
  qualitativeScores: QualitativeScoreInput[];
  isDraft: boolean;
}

export interface EvaluationResult {
  evaluationId: string;
  employeeId: string;
  periodId: string;
  totalScore: number | null;
  status: PerformanceEvaluationStatus;
  proposedTier: string | null;
  calculatedAt: string | null;
  message: string;
}
