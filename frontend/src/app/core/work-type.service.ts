import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, PagedResult } from './kpi-definition.models';
import { KpiOption, WorkflowOption, WorkTypeDetail, WorkTypeFilter } from './work-type.models';

@Injectable({ providedIn: 'root' })
export class WorkTypeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/work-types`;

  list(filter: WorkTypeFilter = {}): Observable<PagedResult<WorkTypeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<WorkTypeDetail>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<WorkTypeDetail> {
    return this.http
      .get<ApiResponse<WorkTypeDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  activeKpis(): Observable<KpiOption[]> {
    return this.http
      .get<ApiResponse<KpiOption[]>>(`${this.url}/kpis`)
      .pipe(map((response) => this.unwrap(response)));
  }

  workflows(): Observable<WorkflowOption[]> {
    return this.http
      .get<ApiResponse<WorkflowOption[]>>(`${this.url}/workflows`)
      .pipe(map((response) => this.unwrap(response)));
  }

  changeStatus(id: string, status: string): Observable<string> {
    return this.http
      .patch<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/status`, { status })
      .pipe(map((response) => response.message || 'Status updated'));
  }

  delete(id: string): Observable<string> {
    return this.http
      .delete<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => response.message || 'WorkType deleted'));
  }

  private params(filter: WorkTypeFilter) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message || 'Invalid work type response');
    }
    return response.data;
  }
}
