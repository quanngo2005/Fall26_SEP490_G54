/** Mirrors G54.BLL.Dtos.Evaluation. See docs/api/EVALUATION_CONTRACT.md. */

export type EvaluationPeriodStatus = 'OPEN' | 'IN_REVIEW' | 'LOCKED' | 'CLOSED';

export type PerformanceEvaluationStatus =
  'DRAFT' | 'DISPUTED' | 'CALCULATED' | 'SUBMITTED' | 'FINAL';

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
  /** The root node of the profile tree. */
  evaluationProfileItemId: string;
  groupCode: string;
  groupName: string;
  weightPercent: number;
  /** Null when no KPI of the group can be scored. */
  groupScore: number | null;
  kpiItems: EvaluationKpiItem[];
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
  /** Set only when the record is FINAL. */
  finalTierCode: string | null;
}

export interface PreviousPeriodSummary {
  periodCode: string;
  totalScore: number;
  tier: string;
  /** Null when the current total is not available. */
  scoreDifference: number | null;
}

export interface EvaluationCriteriaSummary {
  achievedCount: number;
  nearCount: number;
  belowCount: number;
  totalCount: number;
  achievedRatePercent: number | null;
}

export interface EvaluationSlaSummary {
  onTimeCompletedCount: number;
  overdueCount: number;
  inProgressCount: number;
  onTimeRatePercent: number | null;
}

export interface EvaluationFormSettings {
  maxCommentLength: number;
  kpiMetThresholdPercent: number;
  kpiNearThresholdPercent: number;
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
  canComplete: boolean;
  pendingSubmissionCount: number;
  openAppealCount: number;
  hasOpenDispute: boolean;
  previousPeriod: PreviousPeriodSummary | null;
  criteriaSummary: EvaluationCriteriaSummary;
  slaSummary: EvaluationSlaSummary;
  formSettings: EvaluationFormSettings;
  comparePeriod: EvaluationPeriodOption | null;
  compareOptions: EvaluationPeriodOption[];
}

export interface SubmitEvaluationRequest {
  employeeId: string;
  periodId: string;
  comment: string | null;
  coachingPlan?: string | null;
  coachingRequired?: boolean;
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

export type RankingTierCode = 'A1' | 'A2' | 'A3' | 'B' | 'C';

export interface RankingBoardTier {
  tierCode: 'A1' | 'A2' | 'A3' | 'B';
  headcount: number | null;
  actualCount: number;
  isFilled: boolean;
}

export interface RankingBoardDelta {
  value: number | null;
  previousValue: number | null;
  change: number | null;
}

export interface RankingBoardCriterion {
  kpiName: string;
  actualValue: number | null;
  targetValue: number;
  unit: string;
  isAchieved: boolean;
}

export interface RankingBoardRiskEvent {
  code: string;
  description: string;
  severity: string;
}

export interface RankingBoardStaffRow {
  rank: number;
  employeeId: string;
  employeeCode: string;
  fullName: string;
  onTimeSla: RankingBoardDelta;
  onTimeCompletedCount: number;
  completedWithDeadlineCount: number;
  achievedCriteria: number | null;
  totalCriteria: number | null;
  criteria: RankingBoardCriterion[];
  riskEventCount: RankingBoardDelta;
  riskEvents: RankingBoardRiskEvent[];
  riskPenalty: RankingBoardDelta;
  quantitativeScore: RankingBoardDelta;
  systemTier: RankingTierCode | null;
  finalTier: RankingTierCode | null;
  adjustmentReason: string | null;
  evaluationStatus: PerformanceEvaluationStatus;
}

export interface RankingBoardOverview {
  departmentId: string;
  departmentName: string;
  periodId: string;
  periodCode: string;
  periodName: string;
  comparePeriodId: string | null;
  comparePeriodCode: string | null;
  rankingLockDay: number;
  rankingLockDate: string;
  isLocked: boolean;
  canSendToDirector: boolean;
  isSentToDirector: boolean;
  sendBlockers: string[];
  tiers: RankingBoardTier[];
  staffRows: RankingBoardStaffRow[];
  generatedAt: string;
}

export interface RankingBoardAdjustmentItem {
  employeeId: string;
  finalTier: RankingTierCode;
  reason: string | null;
}

export interface SaveRankingBoardRequest {
  departmentId: string;
  periodId: string;
  adjustments: RankingBoardAdjustmentItem[];
}

export interface AutoAssignRankingBoardRequest {
  departmentId: string;
  periodId: string;
}

export interface RankingBoardSendResult {
  departmentId: string;
  periodId: string;
  status: string;
  sentAt: string;
  message: string;
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

export interface PersonalEvaluationCriteriaItem {
  id: string;
  name: string;
  weightPercent: number;
  actualTargetDisplay?: string;
  targetDisplay?: string;
  actualDisplay?: string;
  score: number | null;
  weightedScore: number | null;
  hasDrilldown: boolean;
  kpiDefinitionId?: string | null;
}

export interface PersonalEvaluationCriteriaGroup {
  evaluationProfileItemId: string;
  groupCode?: string;
  groupName: string;
  weightPercent: number;
  groupScore: number | null;
  criteria: PersonalEvaluationCriteriaItem[];
}

export interface PersonalEvaluationView {
  periodId: string;
  periodName: string;
  periodCode: string;
  status: string;
  appealDeadlineText: string;
  appealRemainingDays: number;
  canAppeal: boolean;
  totalPeScore: number | null;
  scoreDelta: number | null;
  previousPeriodName: string | null;
  previousPeriodScore: number | null;
  tier: string | null;
  allTiers: string[];
  rankingPercentile: string | null;
  rankingPosition: number | null;
  rankingTotalEmployees: number | null;
  rankingRoleTitle: string;
  criteria: PersonalEvaluationCriteriaItem[];
  criteriaGroups?: PersonalEvaluationCriteriaGroup[];
  criteriaSummary?: EvaluationCriteriaSummary;
  slaSummary?: EvaluationSlaSummary;
  riskCount?: number;
  riskEvents?: EvaluationRiskEvent[];
  qualityRatio: number | null;
  riskPenalty: number;
  managerFeedback: string | null;
}

export interface EvaluationFlowRequest {
  employeeId: string;
  periodId: string;
}

export interface ReturnEvaluationRequest extends EvaluationFlowRequest {
  reason: string;
}

export interface EvaluationFlowResult {
  evaluationId: string;
  status: PerformanceEvaluationStatus;
  message: string;
}

export interface FinalizeEvaluationResult extends EvaluationFlowResult {
  finalTierCode: string;
  finalizedAt: string;
}

export interface PersonalCriteriaLedgerItem {
  id: string;
  txCode: string;
  date: string;
  amount: string;
  kpiContribution: number;
  status: string;
  occurredOn: string;
  workId?: string | null;
  workCode?: string | null;
  workTitle?: string | null;
}

export interface DepartmentEvaluationDepartment {
  id: string;
  code: string;
  name: string;
  managerName: string | null;
}

export interface DepartmentEvaluationGroupKpi {
  kpiDefinitionId: string;
  code: string;
  name: string;
  unit: string;
  direction: string;
  targetValue: number;
  actualValue: number | null;
  achievementPercent: number | null;
  previousActualValue: number | null;
  previousAchievementPercent: number | null;
  changeFromPreviousPercent: number | null;
  weight: number | null;
  score?: number | null;
  previousScore?: number | null;
}

export interface DepartmentEvaluationKpiGroup {
  name: string | null;
  displayOrder: number;
  weightPercent: number;
  averageScore: number | null;
  kpis: DepartmentEvaluationGroupKpi[];
}

export interface DepartmentEvaluationTrendPoint {
  periodId: string;
  periodCode: string;
  periodName: string;
  average: number | null;
}

export interface DepartmentEvaluationPerformance {
  average: number | null;
  previousAverage: number | null;
  changeFromPrevious: number | null;
  evaluatedEmployeeCount: number;
  totalEmployeeCount: number;
  unscoredEmployeeCount: number;
  pipCount: number;
  appealCount: number;
  kpiGroups: DepartmentEvaluationKpiGroup[];
  trend: DepartmentEvaluationTrendPoint[];
}

export interface DepartmentEvaluationOperations {
  onTimeCompletedCount: number;
  overdueCount: number;
  inProgressCount: number;
  onTimeRatePercent: number | null;
  loadPerStaff: number | null;
}

export interface DepartmentEvaluationTierItem {
  tierCode: string;
  rankOrder: number;
  minScore: number;
  maxScore: number | null;
  count: number;
  percent: number;
}

export type DepartmentEvaluationAlertKind = 'PIP' | 'APPEAL' | 'TOP';

export interface DepartmentEvaluationActionAlert {
  kind: DepartmentEvaluationAlertKind;
  employeeId: string;
  employeeCode: string;
  fullName: string;
  tierCode: string | null;
  totalScore: number | null;
  metaText: string | null;
}

export interface DepartmentEvaluationTierDistribution {
  tierACapPercent: number;
  tiers: DepartmentEvaluationTierItem[];
  actionAlerts: DepartmentEvaluationActionAlert[];
}

export interface DepartmentEvaluationProfileGroupColumn {
  name: string;
  displayOrder: number;
  weightPercent: number;
}

export interface DepartmentEvaluationStaffGroupScore {
  groupName: string;
  displayOrder: number;
  weightPercent: number;
  score: number | null;
  previousScore?: number | null;
}

export type DepartmentEvaluationStaffRowStatus = 'FINAL' | 'APPEAL' | 'PIP' | 'PENDING';

export interface DepartmentEvaluationStaffRow {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionId: string;
  jobPositionTitle: string;
  groupScores: DepartmentEvaluationStaffGroupScore[];
  totalScore: number | null;
  previousTotalScore: number | null;
  scoreDelta: number | null;
  tierCode: string | null;
  onTimeSlaPercent: number | null;
  previousOnTimeSlaPercent?: number | null;
  status: DepartmentEvaluationStaffRowStatus;
  evaluationStatus: string;
  canOpenForm: boolean;
  onTimeCompletedCount?: number;
  inProgressCount?: number;
  overdueCount?: number;
}

export interface DepartmentEvaluationSelectOption {
  id: string;
  code: string;
  name: string;
}

export interface DepartmentEvaluationDashboard {
  department: DepartmentEvaluationDepartment;
  period: EvaluationPeriodOption | null;
  comparePeriod: EvaluationPeriodOption | null;
  availablePeriods: EvaluationPeriodOption[];
  compareOptions: EvaluationPeriodOption[];
  performance: DepartmentEvaluationPerformance;
  operations: DepartmentEvaluationOperations;
  tierDistribution: DepartmentEvaluationTierDistribution;
  profileGroups: DepartmentEvaluationProfileGroupColumn[];
  staffRows: DepartmentEvaluationStaffRow[];
  generatedAt: string;
  availableDepartments?: DepartmentEvaluationSelectOption[];
}
