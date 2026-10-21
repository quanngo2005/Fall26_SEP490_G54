import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import { EmployeeDetail, EmployeeFilter } from './employee.models';

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

  /** URLs identify an employee by their (unique) employee code, never by the GUID. */
  byCode(code: string): Observable<EmployeeDetail> {
    return this.http
      .get<ApiResponse<EmployeeDetail>>(`${this.url}/by-code/${encodeURIComponent(code)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The signed-in user's own employee record (`/employee-detail/me`). */
  me(): Observable<EmployeeDetail> {
    return this.http
      .get<ApiResponse<EmployeeDetail>>(`${this.url}/me`)
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: EmployeeFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '')
        params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid employee response');
    return response.data;
  }
}
