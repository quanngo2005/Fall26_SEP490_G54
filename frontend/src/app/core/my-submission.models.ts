import { SubmissionEvidence } from './result-submission.models';

/** How the transaction codes of a work were reconciled (docs/api/MY_SUBMISSION_CONTRACT.md). */
export type MatchStatus = 'NONE' | 'MATCHED' | 'NOT_MATCHED' | 'IN_REVIEW';

export type SubmissionStatusCode =
  'DRAFT' | 'SUBMITTED' | 'UNDER_REVIEW' | 'APPROVED' | 'REJECTED' | 'WITHDRAWN';

export type AppealWindowState = 'NOT_APPLICABLE' | 'AVAILABLE' | 'EXPIRED' | 'IN_PROGRESS';

export type CheckedField = 'TRANSACTION_CODE' | 'EMPLOYEE' | 'PRODUCT' | 'AMOUNT';

export interface MySubmissionFilter {
  matchStatus?: MatchStatus;
  periodId?: string;
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export interface MySubmissionListItem {
  submissionId: string;
  submissionCode: string;
  submissionNo: number;
  workId: string;
  workCode: string;
  workTitle: string;
  workTypeName: string | null;
  submittedAt: string;
  status: SubmissionStatusCode;
  matchStatus: MatchStatus;
  transactionCount: number;
  matchedTransactionCount: number;
  firstTransactionCode: string | null;
  /** Only set while the submission is rejected and can still be appealed. */
  appealDeadline: string | null;
}

/** `matched` is null when there is nothing to compare (no import yet, or the amount has no declared value). */
export interface MySubmissionFieldCheck {
  field: CheckedField;
  declared: string | null;
  imported: string | null;
  matched: boolean | null;
}

export interface MySubmissionTransaction {
  transactionCode: string;
  status: 'IN_REVIEW' | 'MATCHED' | 'NOT_MATCHED';
  reason: string | null;
  fields: MySubmissionFieldCheck[];
}

export interface MySubmissionVersion {
  submissionId: string;
  submissionNo: number;
  submittedAt: string;
  status: SubmissionStatusCode;
  matchStatus: MatchStatus;
}

export interface MySubmissionAppeal {
  state: AppealWindowState;
  deadline: string | null;
  appealId: string | null;
}

export interface MySubmissionDetail {
  submissionId: string;
  submissionCode: string;
  submissionNo: number;
  workId: string;
  workCode: string;
  workTitle: string;
  workTypeName: string | null;
  submittedAt: string;
  status: SubmissionStatusCode;
  matchStatus: MatchStatus;
  evidences: SubmissionEvidence[];
  transactions: MySubmissionTransaction[];
  versions: MySubmissionVersion[];
  appeal: MySubmissionAppeal;
}
