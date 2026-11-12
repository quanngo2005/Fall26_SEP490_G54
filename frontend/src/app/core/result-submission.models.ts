export interface SubmissionEvidence {
  id: string;
  fileName: string;
  filePath: string;
  mimeType: string | null;
  fileSize: number | null;
  uploadedAt: string;
}

export interface SubmissionMetricItem {
  kpiDefinitionId: string;
  kpiCode: string;
  kpiName: string;
  unit: string;
  value: number;
  denominator: number | null;
}

export interface SubmissionDetail {
  id: string;
  workId: string;
  workCode: string;
  workTitle: string;
  submitterEmployeeId: string;
  submitterEmployeeName: string;
  submitterEmployeeCode: string;
  submissionNo: number;
  status: string;
  comment: string | null;
  submittedAt: string;
  updatedAt: string;
  evidences: SubmissionEvidence[];
  metrics: SubmissionMetricItem[];
}

/** One KPI the work type is scored on (`GET /works/{id}/submission-context`). */
export interface SubmissionKpiCriteria {
  kpiDefinitionId: string;
  code: string;
  name: string;
  description: string | null;
  unit: string;
  /** `SUM`, `COUNT`, `RATIO`, `AVG`, ... The ratio needs a denominator. */
  aggregationType: string;
  direction: string;
  /** A required KPI must have a value, or the submission is rejected with 400. */
  isRequired: boolean;
  formula: string | null;
}

/** What the "submit result" dialog needs to know about a work. */
export interface SubmissionContext {
  workId: string;
  workCode: string;
  workTitle: string;
  workStatus: string;
  assigneeEmployeeId: string;
  assigneeEmployeeName: string;
  assigneeEmployeeCode: string;
  workTypeId: string | null;
  workTypeName: string | null;
  nextSubmissionNo: number;
  previousSubmissionsCount: number;
  kpiCriteria: SubmissionKpiCriteria[];
  /** Extensions such as `.pdf`, lower-case. */
  allowedFileTypes: string[];
  maxFileSizeBytes: number;
  maxFileCount: number;
}

/** A KPI value typed in the dialog. */
export interface SubmissionMetricInput {
  kpiDefinitionId: string;
  value: number;
  denominator?: number | null;
}

/** Transaction codes attached to a work (`GET/POST /works/{id}/transactions`). */
export interface WorkTransactions {
  workId: string;
  workStatus: string;
  items: {
    id: string;
    transactionCode: string;
    status: 'IN_REVIEW' | 'MATCHED' | 'NOT_MATCHED';
    reason: string | null;
    updatedAt: string;
  }[];
}

export interface SubmissionResult {
  submissionId: string;
  workId: string;
  status: string;
  message?: string;
}
