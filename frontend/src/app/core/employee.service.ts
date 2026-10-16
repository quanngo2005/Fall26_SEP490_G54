import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, Observable, of, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  DEFAULT_EMPLOYEE_RESPONSE,
  SAMPLE_EMPLOYEE_LIST,
} from '../features/employee-list/employee-list.data';
import {
  EmployeeListItem,
  EmployeeListQuery,
  EmployeeListResponse,
} from '../features/employee-list/employee-list.model';

@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);

  getEmployees(query: EmployeeListQuery = {}): Observable<EmployeeListResponse> {
    let params = new HttpParams();
    if (query.search) {
      params = params.set('search', query.search);
    }
    if (query.departmentId) {
      params = params.set('departmentId', query.departmentId);
    }
    if (query.positionId) {
      params = params.set('positionId', query.positionId);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.managerId) {
      params = params.set('managerId', query.managerId);
    }
    if (query.page) {
      params = params.set('page', query.page.toString());
    }
    if (query.pageSize) {
      params = params.set('pageSize', query.pageSize.toString());
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortDirection) {
      params = params.set('sortDirection', query.sortDirection);
    }

    return this.http
      .get<EmployeeListResponse>(`${environment.apiUrl}/v1/employees`, { params })
      .pipe(
        catchError((error: HttpErrorResponse) => {
          // In offline mode / local UI dev when backend endpoint is not yet connected:
          if (error.status === 0 || error.status === 404) {
            return of(this.filterLocalSample(query));
          }
          return throwError(() => error);
        }),
      );
  }

  private filterLocalSample(query: EmployeeListQuery): EmployeeListResponse {
    let items = [...SAMPLE_EMPLOYEE_LIST];

    if (query.search) {
      const term = query.search.toLowerCase().trim();
      items = items.filter(
        (e) =>
          e.fullName.toLowerCase().includes(term) ||
          e.code.toLowerCase().includes(term) ||
          e.email.toLowerCase().includes(term),
      );
    }

    if (query.departmentId) {
      items = items.filter((e) => e.departmentId === query.departmentId);
    }

    if (query.positionId && query.positionId !== 'all') {
      items = items.filter((e) => e.positionId === query.positionId);
    }

    if (query.status && query.status !== 'all') {
      items = items.filter((e) => e.status === query.status);
    }

    if (query.managerId) {
      items = items.filter((e) => e.managerId === query.managerId);
    }

    if (query.sortBy) {
      const key = query.sortBy as keyof EmployeeListItem;
      const dir = query.sortDirection === 'desc' ? -1 : 1;
      items.sort((a, b) => {
        const valA = a[key] ?? '';
        const valB = b[key] ?? '';
        return String(valA).localeCompare(String(valB), 'vi', { numeric: true }) * dir;
      });
    }

    const total = items.length;
    const page = query.page ?? 1;
    const pageSize = query.pageSize ?? 10;
    const start = (page - 1) * pageSize;
    const pagedItems = items.slice(start, start + pageSize);

    return {
      ...DEFAULT_EMPLOYEE_RESPONSE,
      items: pagedItems,
      total,
      page,
      pageSize,
    };
  }
}
