import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import { EmployeeDetail, EmployeeFilter } from './employee.models';

@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/employees`;

  list(filter: EmployeeFilter = {}): Observable<PagedResult<EmployeeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<EmployeeDetail>>>(this.url, { params: toHttpParams(filter) })
      .pipe(map((response) => unwrapResponse(response, 'employee list')));
  }

  detail(id: string): Observable<EmployeeDetail> {
    return this.http
      .get<ApiResponse<EmployeeDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => unwrapResponse(response, 'employee')));
  }
}
