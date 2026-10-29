import { PagedResult } from './paged-result.model';

/** API values are UPPER_SNAKE strings (docs/api/CAMPAIGN_CONTRACT.md). */
export type CampaignStatus = 'PLANNED' | 'RUNNING' | 'PAUSED' | 'COMPLETED' | 'CANCELLED';
export type AllocationRule = 'ROUND_ROBIN' | 'LOAD_BALANCING' | 'EQUAL_SPLIT';

export const ALLOCATION_RULES: readonly AllocationRule[] = [
  'ROUND_ROBIN',
  'LOAD_BALANCING',
  'EQUAL_SPLIT',
];

export interface CampaignSummary {
  id: string;
  code: string;
  name: string;
  description: string | null;
  /** Date-only, YYYY-MM-DD. */
  startDate: string;
  endDate: string;
  status: CampaignStatus;
  businessObjectiveId: string;
  businessObjectiveCode: string;
  businessObjectiveName: string;
  participantsCount: number;
  workTypesCount: number;
  generatedWorksCount: number;
  completedWorksCount: number;
  updatedAt: string;
}

export interface CampaignOverviewMetrics {
  totalCampaigns: number;
  runningCampaigns: number;
  plannedCampaigns: number;
  completedCampaigns: number;
  totalTasksGenerated: number;
  totalCompletedTasks: number;
}

export interface CampaignListOverview {
  metrics: CampaignOverviewMetrics;
  campaigns: PagedResult<CampaignSummary>;
}

export interface CampaignFilter {
  search?: string;
  status?: CampaignStatus | '';
  objectiveId?: string;
  page?: number;
  pageSize?: number;
}

export interface CampaignObjective {
  id: string;
  code: string;
  name: string;
  description: string | null;
}

export interface CampaignWorkType {
  workTypeId: string;
  code: string;
  name: string;
  process: string | null;
  expectedResult: string | null;
  requiredEvidence: string | null;
  /** Unit of the work type's KPI (accounts, VND billion, ...); null when it has no current KPI. */
  targetUnit: string | null;
}

export interface CampaignParticipant {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPositionTitle: string | null;
  departmentName: string | null;
  assignedTasksCount: number;
  completedTasksCount: number;
}

export interface CampaignMetrics {
  totalWorksCount: number;
  completedWorksCount: number;
  inExecutionWorksCount: number;
  pendingWorksCount: number;
  progressPercent: number;
}

export interface CampaignDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  startDate: string;
  endDate: string;
  status: CampaignStatus;
  businessObjective: CampaignObjective;
  workTypes: CampaignWorkType[];
  participants: CampaignParticipant[];
  metrics: CampaignMetrics;
  canGenerateBatchWork: boolean;
  canApprove: boolean;
  updatedAt: string;
}

export interface CreateCampaignPayload {
  businessObjectiveId: string;
  code: string;
  name: string;
  description: string | null;
  startDate: string;
  endDate: string;
  workTypeIds: string[];
  participantEmployeeIds: string[];
}

export interface BatchWorkPayload {
  workTypeId: string;
  leadPoolSource: string;
  allocationRule: AllocationRule;
  totalLeadsCount: number;
  plannedEndDate: string | null;
  targetEmployeeIds: string[];
  /** Total expected result of the generated works, in the work type's KPI unit; null for none. */
  targetValue: number | null;
}

export interface BatchWorkResult {
  campaignId: string;
  campaignCode: string;
  totalGenerated: number;
  participantCount: number;
  message: string;
}
