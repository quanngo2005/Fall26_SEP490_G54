import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { DepartmentEmployeeEvaluation, EvaluationPeriodOption } from './evaluation.models';

@Injectable({ providedIn: 'root' })
export class EvaluationService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/evaluations`;

  periods(): Observable<EvaluationPeriodOption[]> {
    return this.http
      .get<ApiResponse<EvaluationPeriodOption[]>>(`${this.url}/periods`)
      .pipe(map((response) => this.unwrap(response)));
  }

  departmentEmployees(
    periodId: string,
    departmentId?: string,
  ): Observable<DepartmentEmployeeEvaluation[]> {
    let params = new HttpParams().set('periodId', periodId);
    if (departmentId) params = params.set('departmentId', departmentId);
    return this.http
      .get<ApiResponse<DepartmentEmployeeEvaluation[]>>(`${this.url}/department-employees`, {
        params,
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid evaluation response');
    return response.data;
  }
}
