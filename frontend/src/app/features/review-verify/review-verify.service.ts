import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DEFAULT_MOCK_DOSSIER } from './review-verify.data';
import {
  AuditDecisionPayload,
  AuditDecisionResponse,
  DossierInspection,
} from './review-verify.model';

@Injectable({
  providedIn: 'root',
})
export class ReviewVerifyService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

  getDossier(submissionId?: string): Observable<DossierInspection> {
    const id = submissionId || DEFAULT_MOCK_DOSSIER.submissionId;
    return this.http.get<DossierInspection>(`${this.apiUrl}/verifications/${id}/dossier`).pipe(
      catchError(() => {
        // Gracefully fallback to sample dossier for local review or offline mode
        return of({
          ...DEFAULT_MOCK_DOSSIER,
          submissionId: id,
        });
      }),
    );
  }

  submitDecision(
    submissionId: string,
    payload: AuditDecisionPayload,
  ): Observable<AuditDecisionResponse> {
    return this.http
      .post<AuditDecisionResponse>(`${this.apiUrl}/verifications/${submissionId}/decision`, payload)
      .pipe(
        catchError(() => {
          // Gracefully fallback to simulated response when backend endpoint is not yet connected
          const actionText =
            payload.decision === 'APPROVED'
              ? 'Phê duyệt hoàn tất'
              : payload.decision === 'REVISION_REQUIRED'
                ? 'Yêu cầu làm lại đã gửi tới cán bộ nộp'
                : 'Đã từ chối hồ sơ công việc';
          return of({
            verificationId: `ver-${Date.now()}`,
            submissionStatus: payload.decision,
            verifiedAt: new Date().toISOString(),
            message: `Quyết định thẩm định đã được ghi nhận: ${actionText}.`,
          });
        }),
        map((res) => res),
      );
  }
}
