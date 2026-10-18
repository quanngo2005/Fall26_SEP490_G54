export type WorkTypeStatus = 'Active' | 'Inactive' | string;

export interface WorkTypeKpiDetail {
  kpiDefinitionId: string;
  kpiCode: string;
  kpiName: string;
  isRequired: boolean;
  effectiveFrom: string;
  effectiveTo: string | null;
}

export interface WorkTypeDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  workflowDefinitionId: string;
  workflowName: string;
  status: WorkTypeStatus;
  createdAt: string;
  kpis: WorkTypeKpiDetail[];
}

export interface WorkTypeFilter {
  keyword?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface KpiOption {
  id: string;
  code: string;
  name: string;
}

export interface WorkflowOption {
  id: string;
  code: string;
  name: string;
}
