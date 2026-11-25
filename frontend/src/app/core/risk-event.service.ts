import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  CreatedRiskEvent,
  CreateRiskEventRequest,
  RiskEvent,
  RiskEventAttachment,
  RiskEventConfirmation,
  RiskEventDetails,
  RiskEventFilter,
  RiskEventList,
} from './risk-event.models';

@Injectable({ providedIn: 'root' })
export class RiskEventService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiUrl;
  private readonly url = `${this.api}/risk-events`;

  list(filter: RiskEventFilter = {}): Observable<RiskEventList> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      params = params.set(key, String(value));
    }
    return this.http
      .get<ApiResponse<RiskEventList>>(this.url, { params })
      .pipe(map((response) => response.data));
  }

  details(id: string): Observable<RiskEventDetails> {
    return this.http
      .get<ApiResponse<RiskEventDetails>>(`${this.url}/${encodeURIComponent(id)}/details`)
      .pipe(map((response) => response.data));
  }

  create(request: CreateRiskEventRequest): Observable<CreatedRiskEvent> {
    return this.http
      .post<ApiResponse<CreatedRiskEvent>>(this.url, request)
      .pipe(map((response) => response.data));
  }

  uploadEvidence(id: string, files: readonly File[]): Observable<RiskEventAttachment[]> {
    const form = new FormData();
    for (const file of files) form.append('files', file, file.name);
    return this.http
      .post<ApiResponse<RiskEventAttachment[]>>(
        `${this.url}/${encodeURIComponent(id)}/evidence`,
        form,
      )
      .pipe(map((response) => response.data));
  }

  confirm(id: string): Observable<RiskEventConfirmation> {
    return this.http
      .post<ApiResponse<RiskEventConfirmation>>(`${this.url}/${encodeURIComponent(id)}/confirm`, {})
      .pipe(map((response) => response.data));
  }

  dismiss(id: string): Observable<RiskEvent> {
    return this.http
      .post<ApiResponse<RiskEvent>>(`${this.url}/${encodeURIComponent(id)}/dismiss`, {})
      .pipe(map((response) => response.data));
  }

  resolve(id: string): Observable<RiskEvent> {
    return this.http
      .post<ApiResponse<RiskEvent>>(`${this.url}/${encodeURIComponent(id)}/resolve`, {})
      .pipe(map((response) => response.data));
  }

  reverse(id: string, reason: string): Observable<RiskEventDetails> {
    return this.http
      .post<ApiResponse<RiskEventDetails>>(`${this.url}/${encodeURIComponent(id)}/reverse`, {
        reason: reason.trim(),
      })
      .pipe(map((response) => response.data));
  }

  /** Evidence files need the bearer token, so they are fetched as a blob instead of linked directly. */
  evidence(evidenceId: string): Observable<Blob> {
    return this.http.get(`${this.api}/evidence/${encodeURIComponent(evidenceId)}/preview`, {
      responseType: 'blob',
    });
  }
}
