export type DecisionType = 'APPROVED' | 'REVISION_REQUIRED' | 'REJECTED';

export type DossierViewState = 'loading' | 'success' | 'empty' | 'error';

export interface SubmitterInfo {
  employeeId: string;
  fullName: string;
  jobPosition: string;
  department: string;
  employeeCode: string;
}

export interface KpiItem {
  kpiCode: string;
  kpiName: string;
  declaredValue: number;
  unit: string;
}

export interface EvidenceFile {
  fileId: string;
  fileName: string;
  fileSizeText: string;
  previewUrl: string;
  downloadUrl?: string;
}

export interface ControlStandard {
  id: string;
  name: string;
  passed: boolean;
}

export interface DocumentInspectionData {
  fileName: string;
  docTitle: string;
  bankParty: string;
  customerParty: string;
  cif: string;
  product: string;
  creditLimit: string;
  officer: string;
  totalPages: number;
  currentPage: number;
  zoomPercent: number;
  auditPinAnnotation?: {
    page: number;
    message: string;
  };
}

export interface DossierInspection {
  submissionId: string;
  submissionCode: string;
  version: string;
  workCode: string;
  workTitle: string;
  submitter: SubmitterInfo;
  submittedAtText: string;
  submittedAtIso: string;
  attemptNumber: number;
  deadlineText: string;
  kpiItem: KpiItem;
  narrative: string;
  evidenceFiles: EvidenceFile[];
  controlStandards: ControlStandard[];
  documentPreview: DocumentInspectionData;
  officialVerifiedValue: number;
  verificationCode: string;
  status: 'SUBMITTED' | 'APPROVED' | 'REVISION_REQUIRED' | 'REJECTED';
}

export interface AuditChecklistResult {
  standardId: string;
  isPassed: boolean;
}

export interface AuditDecisionPayload {
  decision: DecisionType;
  verifiedValue: number;
  checklistResults: AuditChecklistResult[];
  feedback: string;
  resubmitDeadline?: string;
}

export interface AuditDecisionResponse {
  verificationId: string;
  submissionStatus: DecisionType;
  verifiedAt: string;
  message: string;
}

// Centralized response envelope from core/api-response.model
export type { ApiResponse } from '../../core/api-response.model';

export interface SubmitterInfoDto {
  employeeId?: string;
  employeeCode?: string;
  fullName?: string;
  jobPosition?: string;
  department?: string;
}

export interface KpiItemDto {
  kpiDefinitionId?: string | null;
  kpiCode?: string;
  kpiName?: string;
  declaredValue?: number;
  unit?: string;
}

export interface EvidenceItemDto {
  fileId?: string;
  fileName?: string;
  fileSize?: number | null;
  fileSizeText?: string;
  mimeType?: string | null;
  previewUrl?: string;
  downloadUrl?: string;
}

export interface ControlStandardDto {
  id: string;
  name: string;
  passed?: boolean;
}

export interface VerificationDossierDto {
  submissionId?: string;
  submissionCode?: string;
  submissionNo?: number;
  version?: string;
  workId?: string;
  workCode?: string;
  workTitle?: string;
  submitter?: SubmitterInfoDto;
  submittedAt?: string;
  submittedAtIso?: string;
  submittedAtText?: string;
  attemptNumber?: number;
  deadlineText?: string;
  kpiItem?: KpiItemDto;
  narrative?: string;
  evidenceFiles?: EvidenceItemDto[];
  controlStandards?: ControlStandardDto[];
  documentPreview?: DocumentInspectionData;
  officialVerifiedValue?: number;
  verificationCode?: string;
  status?: string;
}

export interface VerificationDecisionResultDto {
  verificationId?: string;
  decision?: string;
  submissionStatus?: string;
  workStatus?: string;
  verifiedAt?: string;
  message?: string;
}
