/**
 * Canonical values are UPPER_SNAKE; the API currently serializes WorkTypeStatus as an ordinal that
 * starts at 1 (see enum-wire.ts, docs/api/WORK_TYPE_CONTRACT.md).
 */
export const WORK_TYPE_STATUSES = ['ACTIVE', 'INACTIVE', 'ARCHIVED'] as const;
export const WORK_TYPE_STATUS_FIRST_ORDINAL = 1;
export type WorkTypeStatus = (typeof WORK_TYPE_STATUSES)[number];

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
  workflowDefinitionId: string;
  workflowName: string;
  status: WorkTypeStatus | null;
  createdAt: string;
  kpis: WorkTypeKpiDetail[];
}

export interface WorkTypeFilter {
  keyword?: string;
  page?: number;
  pageSize?: number;
}
