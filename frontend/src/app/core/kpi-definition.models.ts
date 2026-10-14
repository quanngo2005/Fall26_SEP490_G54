export type KpiAggregationType = 'Sum' | 'Ratio' | 'Avg' | 'NoAggregation';
export type KpiDirection = 'HigherBetter' | 'LowerBetter' | 'OnTarget';
export type KpiStatus = 'Draft' | 'Active' | 'Inactive' | 'Archived';
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
  version?: string;
  effectiveDate?: string;
  measurementType?: KpiMeasurementType;
  applyProductMultiplier?: boolean;
  depositMultipliers?: MultiplierItem[];
  roleWeights?: RoleWeightItem[];
}

export interface KpiGroupOption {
  id: string;
  code: string;
  name: string;
}

export interface KpiFilter {
  keyword?: string;
  groupId?: string;
  status?: KpiStatus;
  page?: number;
  pageSize?: number;
}

export interface KpiDefinitionSavePayload {
  kpiGroupId: string;
  code: string;
  name: string;
  description?: string | null;
  formula?: string | null;
  unit: string;
  aggregationType: KpiAggregationType;
  direction: KpiDirection;
  maxAchievementPercent?: number | null;
}

export interface ApiResponse<T> {
  success: boolean;
  status: number;
  data: T;
  message?: string | null;
  errorCode?: string | null;
  traceId?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
