import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
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
      .pipe(map((response) => this.unwrap(response)));
  }

  history(submissionId: string): Observable<VerificationHistoryItem[]> {
    return this.http
      .get<ApiResponse<VerificationHistoryItem[]>>(
        `${this.url}/${encodeURIComponent(submissionId)}/history`,
      )
      .pipe(map((response) => this.unwrap(response)));
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
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid verification response');
    return response.data;
  }
}
