import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  BranchEvaluationPeriodsOverview,
  DepartmentEmployeeEvaluation,
  EvaluationFormModel,
  EvaluationPeriodOption,
  EvaluationResult,
  FinalizePeriodRequest,
  FinalizePeriodResult,
  SubmitEvaluationRequest,
} from './evaluation.models';

@Injectable({ providedIn: 'root' })
export class EvaluationService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/evaluations`;

  periods(): Observable<EvaluationPeriodOption[]> {
    return this.http
      .get<ApiResponse<EvaluationPeriodOption[]>>(`${this.url}/periods`)
      .pipe(map((response) => this.unwrap(response)));
  }

  branchPeriods(
    year?: number | null,
    periodType?: string | null,
    status?: string | null,
  ): Observable<BranchEvaluationPeriodsOverview> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (periodType && periodType !== 'ALL') params = params.set('periodType', periodType);
    if (status && status !== 'ALL') params = params.set('status', status);

    return this.http
      .get<ApiResponse<BranchEvaluationPeriodsOverview>>(`${this.url}/branch-periods`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  finalize(request: FinalizePeriodRequest): Observable<FinalizePeriodResult> {
    return this.http
      .post<ApiResponse<FinalizePeriodResult>>(`${this.url}/approvals/finalize`, request)
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

  form(employeeId: string, periodId: string): Observable<EvaluationFormModel> {
    const params = new HttpParams().set('employeeId', employeeId).set('periodId', periodId);
    return this.http
      .get<ApiResponse<EvaluationFormModel>>(`${this.url}/form`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  submit(request: SubmitEvaluationRequest): Observable<EvaluationResult> {
    return this.http
      .post<ApiResponse<EvaluationResult>>(`${this.url}/submit`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid evaluation response');
    return response.data;
  }
}
