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
  quantitativeScoreChange: number | null;
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
  /** The single qualitative score saved with the evaluation; null when none was entered. */
  generalQualitativeScore: number | null;
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
  generalQualitativeScore?: number | null;
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

export type EvaluationPeriodDisplayStatus =
  'IN_PROGRESS' | 'EVALUATING' | 'IN_REVIEW' | 'PUBLISHED' | 'LOCKED';

export interface AppealWindowInfo {
  startDate: string | null;
  endDate: string | null;
  isExpired: boolean;
  isClosed: boolean;
}

export interface BranchEvaluationPeriodItem {
  id: string;
  code: string;
  name: string;
  startDate: string;
  endDate: string;
  periodType: string;
  status: EvaluationPeriodStatus;
  displayStatus: EvaluationPeriodDisplayStatus;
  qualityRatio: number | null;
  submittedDepartmentCount: number;
  approvedDepartmentCount: number;
  totalDepartmentCount: number;
  appealWindow: AppealWindowInfo | null;
  canOpen: boolean;
  canLock: boolean;
  canViewHistory: boolean;
}

export interface BranchEvaluationPeriodsOverview {
  branchName: string;
  branchCode: string;
  availableYears: number[];
  periods: BranchEvaluationPeriodItem[];
}

export interface FinalizePeriodRequest {
  periodId: string;
  signatureToken?: string | null;
  approvalComments?: string | null;
}

export interface FinalizePeriodResult {
  periodId: string;
  periodStatus: string;
  finalizedAt: string;
  totalEvaluationsFinalized: number;
  message: string;
}

export interface DepartmentApprovalSummary {
  departmentId: string;
  departmentCode: string;
  departmentName: string;
  managerName: string | null;
  totalEmployees: number;
  evaluatedEmployees: number;
  status: string;
  averageScore: number;
  tierACount: number;
  tierAPercent: number;
  tierBCount: number;
  tierCCount: number;
  tierDCount: number;
  isQuotaCompliant: boolean;
  reviewerComment: string | null;
  submittedAt: string | null;
}

export interface BranchApprovalOverview {
  period: EvaluationPeriod;
  totalEmployees: number;
  totalEvaluated: number;
  totalTierACount: number;
  branchTierAPercent: number;
  branchCapPercent: number;
  isBranchCapCompliant: boolean;
  canFinalize: boolean;
  departments: DepartmentApprovalSummary[];
  disputedPendingCount: number;
}

export interface CalibrationEmployeeRow {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionId: string;
  jobPositionTitle: string;
  departmentId: string;
  departmentName: string;
  businessScore: number | null;
  serviceQualityScore: number | null;
  operationsScore: number | null;
  qualitativeScore: number | null;
  totalScore: number | null;
  rank: number | null;
  proposedTier: string | null;
  calibratedTier: string | null;
  isAdjusted: boolean;
  adjustmentReason: string | null;
  coachingFlag: boolean;
  disciplinaryFlag: string | null;
  evaluationStatus: string;
  evaluationId: string | null;
  calculatedAt: string | null;
}

export interface CalibrationQuota {
  totalEmployees: number;
  maxTierAQuota: number;
  proposedTierACount: number;
  calibratedTierACount: number;
  calibratedTierAPercent: number;
  calibratedTierBCount: number;
  calibratedTierBPercent: number;
  calibratedTierCCount: number;
  calibratedTierCPercent: number;
  calibratedTierDCount: number;
  calibratedTierDPercent: number;
  isCompliant: boolean;
  complianceNote: string;
}

export interface CalibrationOverview {
  department: {
    id: string;
    code: string;
    name: string;
    managerName: string | null;
  };
  period: EvaluationPeriod;
  quota: CalibrationQuota;
  rankedRoster: CalibrationEmployeeRow[];
  canSubmit: boolean;
  overallStatus: string | null;
  disputedPendingCount: number;
}

export interface ApproveDepartmentRankingRequest {
  periodId: string;
  note?: string | null;
}

export interface DepartmentApprovalResult {
  departmentId: string;
  periodId: string;
  status: string;
  approvedEvaluationsCount: number;
  message: string;
}

export interface RejectDepartmentRequest {
  periodId: string;
  reason: string;
}

export interface CalibrationDecisionResult {
  departmentId: string;
  periodId: string;
  status: string;
  message: string;
}

export interface PreCloseCheckItem {
  code: string;
  title: string;
  remainingCount: number;
  isPassed: boolean;
  targetScreenCode?: string | null;
  targetRoute?: string | null;
}

export interface PreCloseCheckOverview {
  periodId: string;
  periodCode: string;
  periodName: string;
  startDate: string;
  endDate: string;
  preCloseDeadline: string | null;
  canPublish: boolean;
  totalUnresolvedCount: number;
  items: PreCloseCheckItem[];
}
