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

export interface SubmissionContext {
  workId: string;
  workCode: string;
  workTitle: string;
  status: string;
  allowResubmission: boolean;
  assignedEmployeeId: string;
  assignedEmployeeName: string;
  kpis: {
    kpiDefinitionId: string;
    kpiCode: string;
    kpiName: string;
    unit: string;
  }[];
}

export interface SubmissionResult {
  submissionId: string;
  workId: string;
  status: string;
  message?: string;
}
