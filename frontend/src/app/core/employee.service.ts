import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, PagedResult } from './kpi-definition.models';
import {
  CreateEmployeeRequest,
  DepartmentOption,
  EmployeeDetail,
  EmployeeFilter,
  JobPositionOption,
  ManagerOption,
  UpdateEmployeeRequest,
} from './employee.models';

@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/employees`;

  list(filter: EmployeeFilter = {}): Observable<PagedResult<EmployeeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<EmployeeDetail>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<EmployeeDetail> {
    return this.http
      .get<ApiResponse<EmployeeDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  departments(): Observable<DepartmentOption[]> {
    return this.http
      .get<ApiResponse<DepartmentOption[]>>(`${this.url}/departments`)
      .pipe(map((response) => this.unwrap(response)));
  }

  jobPositions(): Observable<JobPositionOption[]> {
    return this.http
      .get<ApiResponse<JobPositionOption[]>>(`${this.url}/job-positions`)
      .pipe(map((response) => this.unwrap(response)));
  }

  managers(excludeId?: string): Observable<ManagerOption[]> {
    const params = excludeId ? new HttpParams().set('excludeId', excludeId) : new HttpParams();
    return this.http
      .get<ApiResponse<ManagerOption[]>>(`${this.url}/managers`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  create(dto: CreateEmployeeRequest): Observable<EmployeeDetail> {
    return this.http
      .post<ApiResponse<EmployeeDetail>>(this.url, dto)
      .pipe(map((response) => this.unwrap(response)));
  }

  update(id: string, dto: UpdateEmployeeRequest): Observable<EmployeeDetail> {
    return this.http
      .put<ApiResponse<EmployeeDetail>>(`${this.url}/${encodeURIComponent(id)}`, dto)
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
      .pipe(map((response) => response.message || 'Employee deleted'));
  }

  private params(filter: EmployeeFilter) {
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
      throw new Error(response.message || 'Invalid employee response');
    }
    return response.data;
  }
}
