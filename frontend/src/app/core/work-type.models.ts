/** API values are UPPER_SNAKE strings (docs/api/WORK_TYPE_CONTRACT.md). */
export type WorkTypeStatus = 'ACTIVE' | 'INACTIVE' | 'ARCHIVED';

export interface WorkTypeKpiDetail {
  kpiDefinitionId: string;
  kpiCode: string;
  kpiName: string;
  isRequired: boolean;
  /** Weight applied to the raw value when a verified result is recorded (BR-48 Task Multiplier). */
  multiplier: number;
  /** Date-only, YYYY-MM-DD. */
  effectiveFrom: string;
  /** Date-only, YYYY-MM-DD; null means no end date. */
  effectiveTo: string | null;
}

export interface WorkTypeDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  /** Product this work type represents (e.g. a credit card tier); null when not matched to any product. */
  productCode: string | null;
  status: WorkTypeStatus;
  createdAt: string;
  kpis: WorkTypeKpiDetail[];
}

export interface KpiOption {
  id: string;
  code: string;
  name: string;
  unit: string;
}

export interface WorkTypeFilter {
  keyword?: string;
  status?: WorkTypeStatus;
  page?: number;
  pageSize?: number;
}
