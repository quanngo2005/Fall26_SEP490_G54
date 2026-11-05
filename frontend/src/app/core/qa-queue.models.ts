/** Mirrors G54.BLL.Dtos.Quality (docs/api/QUALITY_ASSURANCE_CONTRACT.md). */

export type QaTab = 'PENDING' | 'CONFIRMED' | 'REJECTED';
export type QaKind = 'FLAG' | 'SAMPLE';
export type QaSource = 'RANDOM_SAMPLE' | 'MANUAL_FLAG' | 'SAMPLE_FAILED';
export type QaSeverity = 'LOW' | 'MEDIUM' | 'HIGH';
export type QaItemStatus =
  'OPEN' | 'REWORK_REQUESTED' | 'DISMISSED' | 'PENDING' | 'PASSED' | 'FAILED';

export interface QaQueueFilter {
  tab?: QaTab;
  kind?: QaKind;
  departmentId?: string;
  periodId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface QaQueueItem {
  /** The record to act on: a flag raised from a sample is still a FLAG here. */
  kind: QaKind;
  /** What QA sees: a flag raised from a 5% sample stays a SAMPLE and keeps the sample's code. */
  type: QaKind;
  id: string;
  code: string;
  source: QaSource;
  resultSubmissionId: string;
  submissionNo: number;
  workId: string;
  workCode: string;
  workTitle: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  departmentName: string | null;
  tab: QaTab;
  status: QaItemStatus;
  severity: QaSeverity | null;
  createdAt: string;
  dueAt: string;
}

export interface QaQueueCounts {
  pending: number;
  confirmed: number;
  rejected: number;
}

export interface QaDepartmentOption {
  id: string;
  name: string;
}

export interface QaQueue {
  items: QaQueueItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  counts: QaQueueCounts;
  /** Every department that has a QA item, for the department filter. */
  departments: QaDepartmentOption[];
}

export interface QaEvidence {
  id: string;
  fileName: string;
  fileSize: number;
  mimeType: string;
}

export interface QaTransaction {
  transactionCode: string;
  status: 'IN_REVIEW' | 'MATCHED' | 'NOT_MATCHED';
  reason: string | null;
  importBatchFileName: string | null;
  productCode: string | null;
  amount: number | null;
  transactionDate: string | null;
}

export interface QaQueueDetail {
  item: QaQueueItem;
  reason: string | null;
  reworkComment: string | null;
  reviewedAt: string | null;
  reviewedByName: string | null;
  submissionComment: string | null;
  submittedAt: string;
  jobPositionTitle: string | null;
  evidences: QaEvidence[];
  transactions: QaTransaction[];
}

/** The two answers QA gives on the decision screen (the labels are "Confirm correct" and "Reject"). */
export type QaVerdict = 'CONFIRM' | 'REJECT';

export interface QaDecision {
  verdict: QaVerdict;
  comment: string;
  /** Only for a rejected 5% sample: the severity of the flag it raises. */
  severity: QaSeverity;
  /** Only for a rejected 5% sample: also send the work back to the employee. */
  requestRework: boolean;
}
