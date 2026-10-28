import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import { WorkDetail, WorkFilter, WorkListItem, WorkStatusChange } from './work.models';

@Injectable({ providedIn: 'root' })
export class WorkService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/works`;

  list(filter: WorkFilter = {}): Observable<PagedResult<WorkListItem>> {
    return this.http
      .get<ApiResponse<PagedResult<WorkListItem>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<WorkDetail> {
    return this.http
      .get<ApiResponse<WorkDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Allowed targets are `InProgress`, `Done` and `Cancelled`; any other transition is a 409 `WORK_INVALID_STATE`. */
  changeStatus(id: string, change: WorkStatusChange): Observable<WorkDetail> {
    return this.http
      .patch<ApiResponse<WorkDetail>>(`${this.url}/${encodeURIComponent(id)}/status`, change)
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: WorkFilter): HttpParams {
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
    if (!response.success || response.data == null) throw new Error('Invalid work response');
    return response.data;
  }
}
