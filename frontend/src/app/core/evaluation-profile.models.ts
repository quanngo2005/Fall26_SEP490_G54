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
