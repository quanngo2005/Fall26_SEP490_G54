/** Mirrors G54.BLL.Dtos.Appeal (docs/api/APPEAL_CONTRACT.md). Enums are UPPER_SNAKE strings. */
export type AppealStatus = 'OPEN' | 'UNDER_REVIEW' | 'ACCEPTED' | 'REJECTED' | 'WITHDRAWN';
export type AppealType = 'RESULT' | 'SCORE';
/** `MINE` only the caller's own appeals, `TEAM` only the other employees' appeals the caller may see. */
export type AppealScope = 'MINE' | 'TEAM';
/** `ACCEPT`, `ADJUST` and `REJECT` decide a result appeal; `ADJUST` and `UPHOLD` decide a score appeal. */
export type AppealDecision = 'ACCEPT' | 'ADJUST' | 'REJECT' | 'UPHOLD';

export interface AppealListItem {
  id: string;
  resultSubmissionId: string | null;
  workId: string | null;
  workCode: string | null;
  workTitle: string | null;
  submissionNo: number | null;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  status: AppealStatus;
  filedAt: string;
  decidedAt: string | null;
  updatedAt: string;
  type: AppealType;
  performanceEvaluationId: string | null;
  periodCode: string | null;
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
  scoreDisputeReason: string | null;
}

export interface AppealFilter {
  /** One or more statuses; sent as a comma separated list, empty means every status. */
  status?: readonly AppealStatus[];
  type?: AppealType;
  scope?: AppealScope;
  periodId?: string;
  jobPositionId?: string;
  page?: number;
  pageSize?: number;
}

export interface DecideAppealRequest {
  decision: AppealDecision;
  note: string | null;
  /** Only with `ACCEPT` on a result appeal. */
  verifiedValue?: number | null;
  /** Points added by `ADJUST` on a score appeal. */
  points?: number | null;
}

export interface AppealDecisionResult {
  appeal: AppealDetail;
  decision: AppealDecision;
  submissionStatus: string | null;
  workStatus: string | null;
  verificationId: string | null;
  scoreEntryId: string | null;
}
