// Kept as a re-export so existing imports of the envelope types keep working.
export type { ApiResponse, PagedResult } from './api.models';

/**
 * Canonical values follow docs/api/API_CONVENTIONS.md (UPPER_SNAKE). The order of each list matches
 * the backend enum declaration because the API currently serializes these enums as ordinals
 * (see enum-wire.ts and docs/api/KPI_DEFINITION_CONTRACT.md).
 */
export const KPI_DIRECTIONS = ['HIGHER_BETTER', 'LOWER_BETTER', 'ON_TARGET'] as const;
export const KPI_AGGREGATIONS = ['SUM', 'RATIO', 'AVG', 'NO_AGGREGATION'] as const;
export const KPI_STATUSES = ['DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED'] as const;

export type KpiDirection = (typeof KPI_DIRECTIONS)[number];
export type KpiAggregationType = (typeof KPI_AGGREGATIONS)[number];
export type KpiStatus = (typeof KPI_STATUSES)[number];

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
  status: KpiStatus | null;
  updatedAt: string;
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
