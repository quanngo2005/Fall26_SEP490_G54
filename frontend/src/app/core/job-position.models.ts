export interface JobPositionSummary {
  id: string;
  code: string;
  title: string;
  description: string | null;
  employeeCount: number;
  evaluationProfileId: string | null;
  evaluationProfileName: string | null;
  workTypeCount: number;
  kpiItems: string[];
  lastSyncAt: string;
  status: string;
}

export interface JobPositionEvaluationProfileItem {
  id: string;
  groupName: string;
  groupCode: string;
  weightPercent: number;
  displayOrder: number;
}

export interface JobPositionEvaluationProfile {
  id: string;
  code: string;
  name: string;
  description: string | null;
  version: number;
  effectiveFrom: string;
  status: string;
  totalWeightPercent: number;
  items: JobPositionEvaluationProfileItem[];
}

export interface JobPositionKpi {
  id: string;
  code: string;
  name: string;
  isRequired: boolean;
  unit: string;
}

export interface JobPositionWorkType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  kpiCount: number;
  kpis: JobPositionKpi[];
}

export interface JobPositionDetail {
  id: string;
  code: string;
  title: string;
  description: string | null;
  employeeCount: number;
  lastSyncAt: string;
  status: string;
  evaluationProfile: JobPositionEvaluationProfile | null;
  workTypes: JobPositionWorkType[];
}

export interface JobPositionFilter {
  keyword?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}
