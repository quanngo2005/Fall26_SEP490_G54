import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, Observable, of, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { SAMPLE_DEPARTMENTS } from '../features/department-detail/department-detail.data';
import { DepartmentDetail } from '../features/department-detail/department-detail.model';

@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private readonly http = inject(HttpClient);

  /**
   * Loads the Department Detail read model. Only the department id is sent; the backend
   * resolves authorization (organizational scope), hierarchy, manager and employees.
   */
  getDetail(departmentId: string): Observable<DepartmentDetail> {
    return this.http
      .get<DepartmentDetail>(
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
}
