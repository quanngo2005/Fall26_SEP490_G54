import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import {
  DepartmentDetail,
  DepartmentFilter,
  DepartmentMemberFilter,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
  PerformanceCompareMode,
} from './department.models';

@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/departments`;

  list(filter: DepartmentFilter = {}) {
    return this.http
      .get<ApiResponse<PagedResult<DepartmentSummary>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string) {
    return this.http
      .get<ApiResponse<DepartmentDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  overview(id: string, periodId?: string, compare?: PerformanceCompareMode) {
    return this.http
      .get<ApiResponse<DepartmentOverview>>(`${this.url}/${encodeURIComponent(id)}/overview`, {
        params: this.params({ periodId, compare }),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The Excel file of the overview (no envelope); failures propagate. */
  exportOverview(id: string, periodId?: string, compare?: PerformanceCompareMode) {
    return this.http.get(`${this.url}/${encodeURIComponent(id)}/overview/export`, {
      params: this.params({ periodId, compare }),
      responseType: 'blob',
    });
  }

  members(id: string, filter: DepartmentMemberFilter = {}) {
    return this.http
      .get<ApiResponse<DepartmentMembers>>(`${this.url}/${encodeURIComponent(id)}/members`, {
        params: this.params(filter),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(
    filter: DepartmentFilter | DepartmentMemberFilter | Record<string, string | undefined>,
  ) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid department response');
    return response.data;
  }
}
