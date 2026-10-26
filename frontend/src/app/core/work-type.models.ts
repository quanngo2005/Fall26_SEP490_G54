/** API values are UPPER_SNAKE strings (docs/api/WORK_TYPE_CONTRACT.md). */
export type WorkTypeStatus = 'ACTIVE' | 'INACTIVE' | 'ARCHIVED';

export interface WorkTypeKpiDetail {
  kpiDefinitionId: string;
  kpiCode: string;
  kpiName: string;
  isRequired: boolean;
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
  status: WorkTypeStatus;
  createdAt: string;
  kpis: WorkTypeKpiDetail[];
}

export interface WorkTypeFilter {
  keyword?: string;
  page?: number;
  pageSize?: number;
}
