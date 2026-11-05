import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  EvaluationProfileCreatePayload,
  EvaluationProfileDetail,
  EvaluationProfileFilter,
  EvaluationProfileListItem,
  EvaluationProfileSavePayload,
} from './evaluation-profile.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class EvaluationProfileService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/evaluation-profiles`;

  list(filter: EvaluationProfileFilter = {}): Observable<PagedResult<EvaluationProfileListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '')
        params = params.set(key, String(value));
    }
    return this.http
      .get<ApiResponse<PagedResult<EvaluationProfileListItem>>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<EvaluationProfileDetail> {
    return this.http
      .get<ApiResponse<EvaluationProfileDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  create(payload: EvaluationProfileCreatePayload): Observable<EvaluationProfileDetail> {
    return this.http
      .post<ApiResponse<EvaluationProfileDetail>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  update(id: string, payload: EvaluationProfileSavePayload): Observable<EvaluationProfileDetail> {
    return this.http
      .put<ApiResponse<EvaluationProfileDetail>>(`${this.url}/${encodeURIComponent(id)}`, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  approve(id: string): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/approve`, {})
      .pipe(map((response) => this.ensureSuccess(response)));
  }

  reject(id: string, reason: string): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/reject`, { reason })
      .pipe(map((response) => this.ensureSuccess(response)));
  }

  private ensureSuccess(response: ApiResponse<unknown>): void {
    if (!response.success) throw new Error('Invalid evaluation profile response');
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null)
      throw new Error('Invalid evaluation profile response');
    return response.data;
  }
}
