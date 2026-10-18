import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { unwrapResponse } from './api-utils';
import { ApiResponse } from './api.models';
import {
  VerificationDecisionPayload,
  VerificationDecisionResult,
  VerificationDossier,
  VerificationHistoryItem,
} from './verification.models';

@Injectable({ providedIn: 'root' })
export class VerificationService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/verifications`;

  dossier(submissionId: string): Observable<VerificationDossier> {
    return this.http
      .get<ApiResponse<VerificationDossier>>(
        `${this.url}/${encodeURIComponent(submissionId)}/dossier`,
      )
      .pipe(map((response) => unwrapResponse(response, 'verification dossier')));
  }

  history(submissionId: string): Observable<VerificationHistoryItem[]> {
    return this.http
      .get<ApiResponse<VerificationHistoryItem[]>>(
        `${this.url}/${encodeURIComponent(submissionId)}/history`,
      )
      .pipe(map((response) => unwrapResponse(response, 'verification history')));
  }

  submitDecision(
    submissionId: string,
    payload: VerificationDecisionPayload,
  ): Observable<VerificationDecisionResult> {
    return this.http
      .post<ApiResponse<VerificationDecisionResult>>(
        `${this.url}/${encodeURIComponent(submissionId)}/decision`,
        payload,
      )
      .pipe(map((response) => unwrapResponse(response, 'verification decision')));
  }
}
