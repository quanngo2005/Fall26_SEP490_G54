/** API values are UPPER_SNAKE strings (docs/api/EVALUATION_PROFILE_CONTRACT.md). */
export type EvaluationProfileStatus =
  'DRAFT' | 'PENDING_APPROVAL' | 'REJECTED' | 'ACTIVE' | 'INACTIVE' | 'ARCHIVED';

export interface EvaluationProfileListItem {
  id: string;
  code: string;
  name: string;
  jobPositionId: string;
  jobPositionCode: string;
  jobPositionTitle: string;
  version: number;
  /** Date-only, YYYY-MM-DD. */
  effectiveFrom: string;
  /** Date-only, YYYY-MM-DD; null means open-ended. */
  effectiveTo: string | null;
  status: EvaluationProfileStatus;
  itemCount: number;
  totalWeightPercent: number;
  updatedAt: string;
}

export interface EvaluationProfileFilter {
  keyword?: string;
  status?: EvaluationProfileStatus;
  jobPositionId?: string;
  page?: number;
  pageSize?: number;
}

export interface EvaluationProfileItem {
  id: string;
  parentItemId: string | null;
  /** Set for a group node; null for a KPI leaf. */
  name: string | null;
  /** Set for a KPI leaf; null for a group node. */
  kpiDefinitionId: string | null;
  kpiCode: string | null;
  kpiName: string | null;
  /** Relative to the node's siblings. */
  weightPercent: number;
  displayOrder: number;
  children: EvaluationProfileItem[];
}

export interface EvaluationProfileDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  jobPositionId: string;
  jobPositionCode: string;
  jobPositionTitle: string;
  version: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: EvaluationProfileStatus;
  rejectReason: string | null;
  isInUse: boolean;
  totalWeightPercent: number;
  updatedAt: string;
  items: EvaluationProfileItem[];
}

export interface EvaluationProfileItemInput {
  name?: string;
  kpiDefinitionId?: string;
  weightPercent: number;
  displayOrder: number;
  children?: EvaluationProfileItemInput[];
}

/** Body of `PUT /evaluation-profiles/{id}`; `POST` adds `code`. */
export interface EvaluationProfileSavePayload {
  name: string;
  description: string | null;
  jobPositionId: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  items: EvaluationProfileItemInput[];
}

export interface EvaluationProfileCreatePayload extends EvaluationProfileSavePayload {
  code: string;
}
