/** Mirrors G54.BLL.Dtos.Verification (docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md). */
export type DecisionType = 'APPROVED' | 'REVISION_REQUIRED' | 'REJECTED';
export type SubmissionStatus =
  'DRAFT' | 'SUBMITTED' | 'UNDER_REVIEW' | 'APPROVED' | 'REJECTED' | 'WITHDRAWN';

export interface SubmitterInfo {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobPosition: string;
  department: string;
}

export interface DossierKpiItem {
  kpiDefinitionId: string | null;
  kpiCode: string;
  kpiName: string;
  declaredValue: number;
  unit: string;
}

export interface EvidenceItem {
  fileId: string;
  fileName: string;
  fileSize: number | null;
  mimeType: string | null;
  previewUrl: string;
  downloadUrl: string | null;
}

export interface ControlStandard {
  id: string;
  name: string;
  passed: boolean;
}

export interface VerificationDossier {
  submissionId: string;
  submissionCode: string;
  submissionNo: number;
  workId: string;
  workCode: string;
  workTitle: string;
  submitter: SubmitterInfo;
  submittedAt: string;
  kpiItem: DossierKpiItem | null;
  narrative: string | null;
  evidenceFiles: EvidenceItem[];
  controlStandards: ControlStandard[];
  status: SubmissionStatus;
}

export interface AuditChecklistResult {
  standardId: string;
  isPassed: boolean;
}

export interface VerificationDecisionPayload {
  decision: DecisionType;
  /** Only meaningful for APPROVED; null otherwise. */
  verifiedValue: number | null;
  feedback: string | null;
  /** ISO 8601 with offset; required for REVISION_REQUIRED and must be in the future. */
  resubmitDeadline: string | null;
  checklistResults: AuditChecklistResult[];
}

export interface VerificationDecisionResult {
  verificationId: string;
  decision: string;
  submissionStatus: string;
  workStatus: string;
  verifiedAt: string;
  message: string;
}

export interface VerificationHistoryItem {
  verificationId: string;
  resultSubmissionId: string;
  roundNo: number;
  /** APPROVED | REVISION_REQUIRED | REJECTED | PENDING */
  decision: string;
  comment: string | null;
  verifierEmployeeId: string;
  verifierFullName: string;
  verifiedAt: string | null;
}
