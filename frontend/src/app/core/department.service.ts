import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { SAMPLE_DEPARTMENTS } from '../features/department-detail/department-detail.data';
import { DepartmentDetail as DepartmentDetailView } from '../features/department-detail/department-detail.model';
import { ApiResponse, PagedResult } from './kpi-definition.models';
import {
  DepartmentDetail,
  DepartmentFilter,
  DepartmentMemberFilter,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
} from './department.models';

@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/departments`;

  /**
   * Loads the Department Detail read model. Only the department id is sent; the backend
   * resolves authorization (organizational scope), hierarchy, manager and employees.
   */
  getDetail(departmentId: string): Observable<DepartmentDetailView> {
    return this.http
      .get<DepartmentDetailView>(
        `${environment.apiUrl}/v1/departments/${encodeURIComponent(departmentId)}/detail`,
      )
      .pipe(
        catchError((error: HttpErrorResponse) => {
          // status 0 = backend unreachable: use sample data so the screen can be reviewed locally.
          if (error.status === 0) {
            const sample = SAMPLE_DEPARTMENTS.find((department) => department.id === departmentId);
            return sample
              ? of(sample)
              : throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
          }
          return throwError(() => error);
        }),
      );
  }

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

  overview(id: string, periodId?: string) {
    return this.http
      .get<ApiResponse<DepartmentOverview>>(`${this.url}/${encodeURIComponent(id)}/overview`, {
        params: periodId ? new HttpParams().set('periodId', periodId) : new HttpParams(),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  members(id: string, filter: DepartmentMemberFilter = {}) {
    return this.http
      .get<ApiResponse<DepartmentMembers>>(`${this.url}/${encodeURIComponent(id)}/members`, {
        params: this.params(filter),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: DepartmentFilter | DepartmentMemberFilter) {
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