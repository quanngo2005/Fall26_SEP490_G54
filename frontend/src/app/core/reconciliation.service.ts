import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  QualityFlagSeverity,
  ReconciliationDetail,
  ReconciliationFilter,
  ReconciliationList,
} from './reconciliation.models';

/** The reconciliation screens: transaction codes against the imported Core Banking file (no live Core Banking call). */
@Injectable({ providedIn: 'root' })
export class ReconciliationService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/reconciliations`;

  list(filter: ReconciliationFilter = {}): Observable<ReconciliationList> {
    return this.http
      .get<ApiResponse<ReconciliationList>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => response.data));
  }

  detail(id: string): Observable<ReconciliationDetail> {
    return this.http
      .get<ApiResponse<ReconciliationDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => response.data));
  }

  /** The same rows as {@link list} (every page) as an Excel file. */
  export(filter: ReconciliationFilter = {}): Observable<Blob> {
    const rest = { ...filter };
    delete rest.page;
    delete rest.pageSize;
    return this.http.get(`${this.url}/export`, {
      params: this.params(rest),
      responseType: 'blob',
    });
  }

  /** QA flags the approved result of a work (`POST /submissions/{id}/quality-flags`). */
  flagQuality(
    submissionId: string,
    request: { reason: string; severity: QualityFlagSeverity },
  ): Observable<unknown> {
    return this.http.post<ApiResponse<unknown>>(
      `${environment.apiUrl}/submissions/${encodeURIComponent(submissionId)}/quality-flags`,
      request,
    );
  }

  private params(filter: ReconciliationFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      params = params.set(key, String(value));
    }
    return params;
  }
}
