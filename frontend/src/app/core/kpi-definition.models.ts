// Kept as a re-export so any legacy imports of the envelope types keep working.
export type { ApiResponse } from './api-response.model';
export type { PagedResult } from './paged-result.model';

/**
 * API values are UPPER_SNAKE strings, sent and received as-is (docs/api/KPI_DEFINITION_CONTRACT.md).
 * The lists also feed the select options of the KPI screens.
 */
export const KPI_DIRECTIONS = ['HIGHER_BETTER', 'LOWER_BETTER', 'ON_TARGET'] as const;
export const KPI_AGGREGATIONS = ['SUM', 'RATIO', 'AVG', 'NO_AGGREGATION'] as const;
export const KPI_STATUSES = [
  'DRAFT',
  'PENDING_APPROVAL',
  'REJECTED',
  'ACTIVE',
  'INACTIVE',
  'ARCHIVED',
] as const;
/** Whose work earns the KPI: who handled the transaction, or who is named in its Refer ID column. */
export const KPI_CREDIT_ROLES = ['PROCESSOR', 'REFERRER'] as const;
export const KPI_DATA_SOURCES = ['TRANSACTION', 'SERVICE_RATING'] as const;

export type KpiDirection = (typeof KPI_DIRECTIONS)[number];
export type KpiAggregationType = (typeof KPI_AGGREGATIONS)[number];
export type KpiStatus = (typeof KPI_STATUSES)[number];
export type KpiCreditRole = (typeof KPI_CREDIT_ROLES)[number];
export type KpiDataSource = (typeof KPI_DATA_SOURCES)[number];

export interface KpiJobPosition {
  id: string;
  code: string;
  title: string;
}

/** An evaluation period a KPI starts or stops applying in; `code` looks like `2026-Q4`. */
export interface KpiPeriodRef {
  id: string;
  code: string;
  name: string;
  startDate: string;
  endDate: string;
}

export interface KpiDefinition {
  id: string;
  kpiGroupId: string;
  kpiGroupName: string;
  code: string;
  name: string;
  description: string | null;
  formula: string | null;
  unit: string;
  aggregationType: KpiAggregationType;
  direction: KpiDirection;
  maxAchievementPercent: number | null;
  status: KpiStatus;
  updatedAt: string;
  rejectReason: string | null;
  requestNote: string | null;
  creditRole: KpiCreditRole;
  dataSource: KpiDataSource;
  productName: string | null;
  baselineValue: number | null;
  jobPositions: KpiJobPosition[];
  effectiveFrom: KpiPeriodRef | null;
  effectiveTo: KpiPeriodRef | null;
  submittedAt: string | null;
}

export interface KpiGroupOption {
  id: string;
  code: string;
  name: string;
}

export interface KpiFilter {
  keyword?: string;
  kpiGroupId?: string;
  status?: KpiStatus;
  jobPositionId?: string;
  creditRole?: KpiCreditRole;
  dataSource?: KpiDataSource;
  /** Only the KPIs in force in this period. */
  effectiveAtPeriodId?: string;
  page?: number;
  pageSize?: number;
}

export interface KpiDefinitionSavePayload {
  kpiGroupId: string;
  /** Empty on create: the server generates the code from the name. */
  code: string;
  name: string;
  description: string | null;
  formula: string | null;
  unit: string;
  aggregationType: KpiAggregationType;
  direction: KpiDirection;
  maxAchievementPercent: number | null;
  creditRole?: KpiCreditRole;
  dataSource?: KpiDataSource;
  productName?: string | null;
  baselineValue?: number | null;
  jobPositionIds?: string[];
  effectiveFromPeriodId?: string | null;
  effectiveToPeriodId?: string | null;
}

/** How many KPIs are in each status (`GET /kpi-definitions/summary`). */
export interface KpiSummary {
  total: number;
  draft: number;
  pendingApproval: number;
  rejected: number;
  active: number;
  inactive: number;
  archived: number;
}

export interface KpiRelatedItem {
  id: string;
  code: string;
  name: string;
  creditRole: KpiCreditRole;
  dataSource: KpiDataSource;
  jobPositions: string[];
}

export interface KpiUsage {
  objectives: { id: string; code: string }[];
  activeTargetCount: number;
  /** The KPIs of the same product, the KPI itself included. */
  relatedItems: KpiRelatedItem[];
}

export type KpiHistoryAction =
  'CREATE' | 'UPDATE' | 'SUBMIT' | 'APPROVE' | 'REJECT' | 'DEACTIVATE' | 'REACTIVATE' | 'ARCHIVE';

export interface KpiHistoryEntry {
  occurredAt: string;
  actorName: string | null;
  action: KpiHistoryAction;
  summary: string;
}

/** `POST /kpi-definitions/requests`: a Branch Manager asks for a KPI missing from the catalog. */
export interface KpiRequestPayload {
  kpi: KpiDefinitionSavePayload;
  requestNote: string;
}
