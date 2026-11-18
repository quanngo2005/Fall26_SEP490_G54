/** System parameters & Ranking Policy Studio models (SCR-ADM-04 / GB-910). See docs/api/SYSTEM_PARAMETER_CONTRACT.md. */

export const SYSTEM_PARAMETER_KEYS = [
  'kpiLockTime',
  'preCloseCheckDays',
  'preCloseDeadlineDays',
  'preCloseDeadlineHour',
  'manualApprovalHours',
  'workOverdueEnabled',
  'workOverdueSweepMinutes',
  'campaignExclusivityDays',
  'appealRejectedHours',
  'appealWindowDays',
  'minReasonLength',
  'maxEvidenceFiles',
  'qaSampleRatePercent',
  'riskPenaltyLow',
  'riskPenaltyMedium',
  'riskPenaltyHigh',
  'riskPenaltyCritical',
  'unitUnderperformanceThresholdPercent',
  'unitPenaltyMultiplier',
  'evalKpiMetThresholdPercent',
  'evalKpiNearThresholdPercent',
  'evalCommentMaxLength',
  'tierAQuotaPercent',
  'tierBTargetPercent',
  'tierCTargetPercent',
  'tierDTargetPercent',
  'minDeptSizeForOwnQuota',
  'importMaxFileMb',
  'targetImportMaxFileMb',
  'importMaxRows',
  'maxTransactionsPerWork',
  'handoverThresholdVnd',
  'importFormats',
  'perfAttentionDropPoints',
  'perfAttentionLowScore',
  'perfAttentionTopN',
  'perfWeakGroupScore',
  'perfTrendPeriodCount',
  'authMaxFailedAttempts',
  'authLockoutMinutes',
  'loginRateLimitPerMinute',
] as const;

export type SystemParameterKey = (typeof SYSTEM_PARAMETER_KEYS)[number];

export type SystemParameterValueType =
  'INTEGER' | 'DECIMAL' | 'PERCENT' | 'TIME' | 'BOOLEAN' | 'STRING';

export type SystemParameterGroupCode =
  | 'DEADLINES'
  | 'APPEALS'
  | 'SCORING_QA'
  | 'QUOTA_RANKING'
  | 'IMPORT_HANDOVER'
  | 'DASHBOARD_SECURITY';

export interface SystemParameter {
  key: SystemParameterKey;
  value: number | string | boolean;
  min: number | null;
  max: number | null;
  locked: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
  valueType?: SystemParameterValueType;
  group?: SystemParameterGroupCode;
}

export interface SystemParameterChange {
  key: SystemParameterKey;
  value: number | string | boolean;
}

export interface SystemParametersSavePayload {
  changes: SystemParameterChange[];
}

export type EvaluationComparisonBaseline =
  'PREVIOUS_PERIOD' | 'SAME_PERIOD_LAST_YEAR' | 'AVERAGE_LAST_N';

export interface EvaluationComparisonSetting {
  baseline: EvaluationComparisonBaseline;
  lookbackPeriods: number;
}

export interface TierThresholdSummary {
  code: string;
  minScore: number;
}

export interface TierCriterionItem {
  code: string;
  name: string;
  metric: string;
  threshold: number;
  required: Record<string, boolean>;
}

export interface TierCriteriaMatrix {
  tiers: TierThresholdSummary[];
  metrics: string[];
  criteria: TierCriterionItem[];
}

// ----- Ranking Policy Studio (GB-910) ---------------------------------------------------

export interface RankingTierDefinition {
  tierCode: string;
  rankOrder: number;
  minScore: number;
}

export interface RankingRuleConditionItem {
  metric: string;
  operator: string;
  value?: number | null;
  secondaryValue?: number | null;
  values?: string[] | null;
}

export interface RankingRuleConditionGroup {
  logicalOperator: 'AND' | 'OR';
  items: RankingRuleConditionItem[];
  groups?: RankingRuleConditionGroup[] | null;
}

export type RankingRuleActionType =
  | 'CAP_MAX_TIER'
  | 'SET_MIN_TIER'
  | 'SET_EXACT_TIER'
  | 'DEMOTE_STEPS'
  | 'PROMOTE_STEPS'
  | 'ENFORCE_GROUP_QUOTA_PERCENT'
  | 'SET_COACHING_FLAG'
  | 'SET_WARNING_LABEL';

export interface RankingRuleAction {
  type: RankingRuleActionType;
  tierCode?: string | null;
  numericValue?: number | null;
  flagValue?: boolean | null;
  message?: string | null;
}

export interface RankingAutomationRule {
  id: string;
  name: string;
  enabled: boolean;
  stopOnMatch: boolean;
  conditions: RankingRuleConditionGroup;
  actions: RankingRuleAction[];
}

export interface RankingPolicyDefinition {
  scoreFormula: string;
  tiers: RankingTierDefinition[];
  tieBreakers: string[];
  rules: RankingAutomationRule[];
}

export interface RankingPolicyCatalog {
  formulaVariables: string[];
  formulaFunctions: string[];
  metrics: string[];
  operators: string[];
  actionTypes: string[];
  tieBreakerFields: string[];
}

export interface RankingPolicy {
  id: string;
  code: string;
  name: string;
  description: string | null;
  version: number;
  evaluationPeriodId: string | null;
  hasPeriodOverride: boolean;
  isLockedSnapshot: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
  definition: RankingPolicyDefinition;
  catalog: RankingPolicyCatalog;
}

export interface UpdateRankingPolicyPayload {
  name: string;
  description: string | null;
  evaluationPeriodId: string | null;
  definition: RankingPolicyDefinition;
}

export interface SimulateRankingPolicyPayload {
  periodId: string | null;
  departmentId: string | null;
  definition: RankingPolicyDefinition;
}

export interface SimulatedTierDistributionItem {
  tierCode: string;
  beforeCount: number;
  beforePercent: number;
  afterCount: number;
  afterPercent: number;
}

export interface SimulatedEmployeeRankingRow {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionCode: string;
  jobPositionTitle: string;
  departmentName: string;
  totalScore: number;
  effectiveScore: number;
  businessScore: number | null;
  serviceQualityScore: number | null;
  operationsScore: number | null;
  qualitativeScore: number | null;
  openRiskEvents: number;
  maxRiskSeverity: string;
  beforeRank: number;
  afterRank: number;
  beforeTier: string;
  afterTier: string;
  changed: boolean;
  coachingFlag: boolean;
  warningLabel: string | null;
  matchedRules: string[];
  explanation: string[];
}

export interface SimulateRankingPolicyResult {
  periodId: string | null;
  periodLabel: string;
  isSyntheticSample: boolean;
  totalEmployees: number;
  changedCount: number;
  tierSummary: SimulatedTierDistributionItem[];
  rows: SimulatedEmployeeRankingRow[];
}
