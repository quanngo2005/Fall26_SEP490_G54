import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { AuditLogDetail, AuditLogListItem, AuditLogQuery } from './audit-log.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/audit-logs`;

  list(query: AuditLogQuery): Observable<PagedResult<AuditLogListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http
      .get<ApiResponse<PagedResult<AuditLogListItem>>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  get(id: string): Observable<AuditLogDetail> {
    return this.http
      .get<ApiResponse<AuditLogDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid audit log response');
    }
    return response.data;
  }
}
