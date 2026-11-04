import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AppealDecisionResult,
  AppealDetail,
  AppealFilter,
  AppealListItem,
  DecideAppealRequest,
} from './appeal.models';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class AppealService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/appeals`;

  list(filter: AppealFilter = {}): Observable<PagedResult<AppealListItem>> {
    return this.http
      .get<ApiResponse<PagedResult<AppealListItem>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<AppealDetail> {
    return this.http
      .get<ApiResponse<AppealDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Files an appeal against a rejected result submission (multipart: `reason`, `files[]`). */
  createForSubmission(submissionId: string, form: FormData): Observable<AppealDetail> {
    return this.http
      .post<ApiResponse<AppealDetail>>(
        `${environment.apiUrl}/submissions/${encodeURIComponent(submissionId)}/appeals`,
        form,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  withdraw(id: string): Observable<AppealDetail> {
    return this.http
      .post<ApiResponse<AppealDetail>>(`${this.url}/${encodeURIComponent(id)}/withdraw`, {})
      .pipe(map((response) => this.unwrap(response)));
  }

  decide(id: string, request: DecideAppealRequest): Observable<AppealDecisionResult> {
    return this.http
      .post<ApiResponse<AppealDecisionResult>>(
        `${this.url}/${encodeURIComponent(id)}/decision`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Evidence files need the bearer token, so they are fetched as a blob instead of linked directly. */
  evidence(path: string): Observable<Blob> {
    return this.http.get(`${environment.apiUrl}${path}`, { responseType: 'blob' });
  }

  private params(filter: AppealFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      if (Array.isArray(value)) {
        if (value.length > 0) params = params.set(key, value.join(','));
        continue;
      }
      params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid appeal response');
    return response.data;
  }
}
