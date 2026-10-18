import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
import { ApiResponse, PagedResult } from './api.models';
import { parseWireEnum } from './enum-wire';
import { EMPLOYEE_STATUSES, EmployeeDetail, EmployeeFilter } from './employee.models';

type RawEmployee = Omit<EmployeeDetail, 'status'> & { status: unknown };

@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/employees`;

  list(filter: EmployeeFilter = {}): Observable<PagedResult<EmployeeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<RawEmployee>>>(this.url, { params: toHttpParams(filter) })
      .pipe(
        map((response) => unwrapResponse(response, 'employee list')),
        map((page) => ({ ...page, items: page.items.map((item) => this.adapt(item)) })),
      );
  }

  detail(id: string): Observable<EmployeeDetail> {
    return this.http.get<ApiResponse<RawEmployee>>(`${this.url}/${encodeURIComponent(id)}`).pipe(
      map((response) => unwrapResponse(response, 'employee')),
      map((item) => this.adapt(item)),
    );
  }

  private adapt(raw: RawEmployee): EmployeeDetail {
    return { ...raw, status: parseWireEnum(EMPLOYEE_STATUSES, raw.status) };
  }
}
