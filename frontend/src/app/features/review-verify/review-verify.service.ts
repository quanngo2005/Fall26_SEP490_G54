import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DEFAULT_MOCK_DOSSIER } from './review-verify.data';
import {
  ApiResponse,
  AuditDecisionPayload,
  AuditDecisionResponse,
  ControlStandardDto,
  DecisionType,
  DossierInspection,
  EvidenceItemDto,
  VerificationDecisionResultDto,
  VerificationDossierDto,
} from './review-verify.model';

function formatFileSize(bytes?: number | null): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
}

function isDossierInspection(
  data: VerificationDossierDto | DossierInspection,
): data is DossierInspection {
  return (
    'officialVerifiedValue' in data &&
    typeof (data as DossierInspection).officialVerifiedValue === 'number' &&
    'attemptNumber' in data &&
    typeof (data as DossierInspection).attemptNumber === 'number'
  );
}

export function mapDtoToDossier(
  data: VerificationDossierDto | DossierInspection,
): DossierInspection {
  if (isDossierInspection(data)) {
    return data;
  }
  const dto = data;
  const submitter = dto.submitter ?? {};
  const kpiItem = dto.kpiItem ?? {};
  const evidenceFiles = (dto.evidenceFiles ?? []).map((file: EvidenceItemDto) => ({
    fileId: String(file.fileId ?? ''),
    fileName: file.fileName ?? '',
    fileSizeText: file.fileSizeText ?? formatFileSize(file.fileSize),
    previewUrl: file.previewUrl ?? '',
    downloadUrl: file.downloadUrl,
  }));
  const controlStandards = (dto.controlStandards ?? []).map((std: ControlStandardDto) => ({
    id: std.id,
    name: std.name,
    passed: !!std.passed,
  }));

  const declaredVal = Number(kpiItem.declaredValue ?? 0);
  const status = (dto.status as DecisionType) || 'SUBMITTED';

  return {
    submissionId: String(dto.submissionId ?? ''),
    submissionCode: dto.submissionCode ?? '',
    version: dto.submissionNo ? `v${dto.submissionNo}.0` : (dto.version ?? 'v1.0'),
    workCode: dto.workCode ?? '',
    workTitle: dto.workTitle ?? '',
    submitter: {
      employeeId: String(submitter.employeeId ?? ''),
      fullName: submitter.fullName ?? '',
      jobPosition: submitter.jobPosition ?? '',
      department: submitter.department ?? '',
      employeeCode: submitter.employeeCode ?? '',
    },
    submittedAtText:
      dto.submittedAtText ??
      (dto.submittedAt ? new Date(dto.submittedAt).toLocaleDateString('vi-VN') : ''),
    submittedAtIso: dto.submittedAtIso ?? dto.submittedAt ?? '',
    attemptNumber: dto.submissionNo ?? dto.attemptNumber ?? 1,
    deadlineText: dto.deadlineText ?? '--',
    kpiItem: {
      kpiCode: kpiItem.kpiCode ?? '',
      kpiName: kpiItem.kpiName ?? '',
      declaredValue: declaredVal,
      unit: kpiItem.unit ?? '',
    },
    narrative: dto.narrative ?? '',
    evidenceFiles,
    controlStandards,
    documentPreview: dto.documentPreview ?? DEFAULT_MOCK_DOSSIER.documentPreview,
    officialVerifiedValue: declaredVal,
    verificationCode: dto.submissionCode ?? '',
    status: status as 'SUBMITTED' | 'APPROVED' | 'REVISION_REQUIRED' | 'REJECTED',
  };
}

@Injectable({
  providedIn: 'root',
})
export class ReviewVerifyService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

  getDossier(submissionId?: string): Observable<DossierInspection> {
    const id = submissionId || DEFAULT_MOCK_DOSSIER.submissionId;
    return this.http
      .get<ApiResponse<VerificationDossierDto> | VerificationDossierDto>(
        `${this.apiUrl}/verifications/${id}/dossier`,
      )
      .pipe(
        map((res) => {
          const data =
            res && typeof res === 'object' && 'data' in res
              ? (res as ApiResponse<VerificationDossierDto>).data
              : (res as VerificationDossierDto);
          return mapDtoToDossier(data);
        }),
      );
  }

  submitDecision(
    submissionId: string,
    payload: AuditDecisionPayload,
  ): Observable<AuditDecisionResponse> {
    const body = {
      decision: payload.decision,
      verifiedValue: payload.verifiedValue,
      feedback: payload.feedback,
      resubmitDeadline: payload.resubmitDeadline
        ? new Date(payload.resubmitDeadline).toISOString()
        : undefined,
      checklistResults: payload.checklistResults?.map((r) => ({
        standardId: r.standardId,
        isPassed: r.isPassed,
      })),
    };

    return this.http
      .post<ApiResponse<VerificationDecisionResultDto> | VerificationDecisionResultDto>(
        `${this.apiUrl}/verifications/${submissionId}/decision`,
        body,
      )
      .pipe(
        map((res) => {
          const data =
            res && typeof res === 'object' && 'data' in res
              ? (res as ApiResponse<VerificationDecisionResultDto>).data
              : (res as VerificationDecisionResultDto);
          return {
            verificationId: String(data?.verificationId ?? ''),
            submissionStatus: (data?.submissionStatus ?? payload.decision) as DecisionType,
            verifiedAt: String(data?.verifiedAt ?? new Date().toISOString()),
            message: data?.message ?? 'Quyết định thẩm định đã được ghi nhận.',
          };
        }),
      );
  }
}
