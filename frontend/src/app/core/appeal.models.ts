/** Mirrors G54.BLL.Dtos.Appeal (docs/api/APPEAL_CONTRACT.md). Enums are UPPER_SNAKE strings. */
export type AppealStatus = 'OPEN' | 'UNDER_REVIEW' | 'ACCEPTED' | 'REJECTED' | 'WITHDRAWN';
/** `MINE` only the caller's own appeals, `TEAM` only the other employees' appeals the caller may see. */
export type AppealScope = 'MINE' | 'TEAM';
/** An appeal never changes a score directly: `ACCEPT` adds the verified KPI contribution, the system recalculates. */
export type AppealDecision = 'ACCEPT' | 'ADJUST' | 'REJECT';

export interface AppealListItem {
  id: string;
  resultSubmissionId: string;
  workId: string;
  workCode: string;
  workTitle: string;
  submissionNo: number;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  status: AppealStatus;
  filedAt: string;
  decidedAt: string | null;
  updatedAt: string;
  jobPositionTitle: string | null;
  resolvedByEmployeeName: string | null;
  note: string | null;
}

export interface AppealEvidence {
  id: string;
  fileName: string;
  fileSize: number | null;
  mimeType: string | null;
  previewUrl: string;
  downloadUrl: string;
}

export interface AppealDetail extends Omit<
  AppealListItem,
  'jobPositionTitle' | 'resolvedByEmployeeName' | 'note'
> {
  reason: string;
  note: string | null;
  resolvedByEmployeeId: string | null;
  resolvedByEmployeeName: string | null;
  evidences: AppealEvidence[];
  submittedAt: string;
  workTypeName: string | null;
  referrerEmployeeCode: string | null;
  referrerEmployeeName: string | null;
}

export interface AppealFilter {
  /** One or more statuses; sent as a comma separated list, empty means every status. */
  status?: readonly AppealStatus[];
  scope?: AppealScope;
  periodId?: string;
  jobPositionId?: string;
  page?: number;
  pageSize?: number;
}

export interface DecideAppealRequest {
  decision: AppealDecision;
  note: string | null;
  /** Only with `ACCEPT`. */
  verifiedValue?: number | null;
}

export interface AppealDecisionResult {
  appeal: AppealDetail;
  decision: AppealDecision;
  submissionStatus: string;
  workStatus: string;
  verificationId: string | null;
}
