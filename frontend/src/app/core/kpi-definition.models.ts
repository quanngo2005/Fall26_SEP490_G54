// Kept as a re-export so any legacy imports of the envelope types keep working.
export type { ApiResponse } from './api-response.model';
export type { PagedResult } from './paged-result.model';

/**
 * API values are UPPER_SNAKE strings, sent and received as-is (docs/api/KPI_DEFINITION_CONTRACT.md).
 * The lists also feed the select options of the KPI screens.
 */
export const KPI_DIRECTIONS = ['HIGHER_BETTER', 'LOWER_BETTER', 'ON_TARGET'] as const;
export const KPI_AGGREGATIONS = ['SUM', 'RATIO', 'AVG', 'NO_AGGREGATION'] as const;
export const KPI_STATUSES = ['DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED'] as const;

export type KpiDirection = (typeof KPI_DIRECTIONS)[number];
export type KpiAggregationType = (typeof KPI_AGGREGATIONS)[number];
export type KpiStatus = (typeof KPI_STATUSES)[number];

export type KpiMeasurementType = 'Quantitative' | 'Qualitative';

export interface MultiplierItem {
  id?: string;
  label: string;
  value: number;
}

export interface RoleWeightItem {
  id?: string;
  role: string;
  weight: number;
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
  measurementType?: KpiMeasurementType;
  applyProductMultiplier?: boolean;
  depositMultipliers?: MultiplierItem[];
  roleWeights?: RoleWeightItem[];
  version?: string;
  effectiveDate?: string;
}

export interface KpiGroupOption {
  id: string;
  code: string;
  name: string;
}

export interface KpiFilter {
  keyword?: string;
  kpiGroupId?: string;
  page?: number;
  pageSize?: number;
}

export interface KpiDefinitionSavePayload {
  kpiGroupId: string;
  code: string;
  name: string;
  description: string | null;
  formula: string | null;
  unit: string;
  aggregationType: KpiAggregationType;
  direction: KpiDirection;
  maxAchievementPercent: number | null;
}
