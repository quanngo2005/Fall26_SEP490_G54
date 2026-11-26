import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  EvaluationFlowRequest,
  EvaluationFlowResult,
  FinalizeEvaluationResult,
  ReturnEvaluationRequest,
  ApproveDepartmentRankingRequest,
  BranchApprovalOverview,
  BranchEvaluationPeriodsOverview,
  CalibrationDecisionResult,
  CalibrationOverview,
  DepartmentApprovalResult,
  DepartmentEmployeeEvaluation,
  DepartmentEvaluationDashboard,
  EvaluationFormModel,
  EvaluationPeriodOption,
  EvaluationResult,
  FinalizePeriodRequest,
  FinalizePeriodResult,
  PersonalCriteriaLedgerItem,
  PersonalEvaluationView,
  PreCloseCheckOverview,
  RankingBoardOverview,
  RankingBoardSendResult,
  RejectDepartmentRequest,
  SaveRankingBoardRequest,
  AutoAssignRankingBoardRequest,
  SubmitEvaluationRequest,
  RankingSlotQuotas,
  RankingSlotQuotaItem,
  UpdateRankingSlotQuotasRequest,
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

  /** `POST /evaluation-periods/years`: creates the year, its half-years and quarters so its targets can be imported. */
  createYearPeriods(
    year: number,
  ): Observable<{ yearPeriodId: string; year: number; createdCount: number }> {
    return this.http
      .post<ApiResponse<{ yearPeriodId: string; year: number; createdCount: number }>>(
        `${environment.apiUrl}/evaluation-periods/years`,
        { year },
      )
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

  getBranchApprovals(periodId: string): Observable<BranchApprovalOverview> {
    const params = new HttpParams().set('periodId', periodId);
    return this.http
      .get<ApiResponse<BranchApprovalOverview>>(`${this.url}/approvals`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  getCalibration(
    periodId: string,
    departmentId?: string,
    positionId?: string,
    includeSubDepartments?: boolean,
  ): Observable<CalibrationOverview> {
    let params = new HttpParams().set('periodId', periodId);
    if (departmentId) params = params.set('departmentId', departmentId);
    if (positionId) params = params.set('positionId', positionId);
    if (includeSubDepartments !== undefined) {
      params = params.set('includeSubDepartments', String(includeSubDepartments));
    }
    return this.http
      .get<ApiResponse<CalibrationOverview>>(`${this.url}/calibration`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  approveDepartment(
    departmentId: string,
    request: ApproveDepartmentRankingRequest,
  ): Observable<DepartmentApprovalResult> {
    return this.http
      .post<ApiResponse<DepartmentApprovalResult>>(
        `${this.url}/approvals/${departmentId}/approve`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  rejectDepartment(
    departmentId: string,
    request: RejectDepartmentRequest,
  ): Observable<CalibrationDecisionResult> {
    return this.http
      .post<ApiResponse<CalibrationDecisionResult>>(
        `${this.url}/approvals/${departmentId}/recalibrate`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  getDepartmentSlotQuotas(
    departmentId: string,
    periodId?: string | null,
  ): Observable<RankingSlotQuotas> {
    let params = new HttpParams();
    if (periodId) {
      params = params.set('periodId', periodId);
    }
    return this.http
      .get<ApiResponse<RankingSlotQuotas>>(
        `${this.url}/approvals/departments/${departmentId}/quotas`,
        { params },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  updateDepartmentSlotQuotas(
    departmentId: string,
    request: UpdateRankingSlotQuotasRequest,
  ): Observable<RankingSlotQuotas> {
    return this.http
      .put<ApiResponse<RankingSlotQuotas>>(
        `${this.url}/approvals/departments/${departmentId}/quotas`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  getBranchPeriodQuota(periodId: string): Observable<RankingSlotQuotas> {
    const params = new HttpParams().set('periodId', periodId);
    return this.http
      .get<ApiResponse<RankingSlotQuotas>>(`${this.url}/approvals/period-quota`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  updateBranchPeriodQuota(
    periodId: string,
    slots: RankingSlotQuotaItem[],
  ): Observable<RankingSlotQuotas> {
    const params = new HttpParams().set('periodId', periodId);
    return this.http
      .put<ApiResponse<RankingSlotQuotas>>(
        `${this.url}/approvals/period-quota`,
        { departmentId: '00000000-0000-0000-0000-000000000000', periodId, slots },
        { params },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  getPreCloseCheck(periodId: string): Observable<PreCloseCheckOverview> {
    const params = new HttpParams().set('periodId', periodId);
    return this.http
      .get<ApiResponse<PreCloseCheckOverview>>(`${this.url}/pre-close-check`, { params })
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

  departmentDashboard(
    periodId?: string | null,
    comparePeriodId?: string | null,
  ): Observable<DepartmentEvaluationDashboard> {
    let params = new HttpParams();
    if (periodId) params = params.set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    return this.http
      .get<ApiResponse<DepartmentEvaluationDashboard>>(`${this.url}/department-dashboard`, {
        params,
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  exportDepartmentDashboard(
    periodId?: string | null,
    comparePeriodId?: string | null,
  ): Observable<Blob> {
    let params = new HttpParams();
    if (periodId) params = params.set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    return this.http.get(`${this.url}/department-dashboard/export`, {
      params,
      responseType: 'blob',
    });
  }

  branchDashboard(
    periodId?: string | null,
    comparePeriodId?: string | null,
    departmentId?: string | null,
  ): Observable<DepartmentEvaluationDashboard> {
    let params = new HttpParams();
    if (periodId) params = params.set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    if (departmentId) params = params.set('departmentId', departmentId);
    return this.http
      .get<ApiResponse<DepartmentEvaluationDashboard>>(`${this.url}/branch-dashboard`, {
        params,
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  exportBranchDashboard(
    periodId?: string | null,
    comparePeriodId?: string | null,
    departmentId?: string | null,
  ): Observable<Blob> {
    let params = new HttpParams();
    if (periodId) params = params.set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    if (departmentId) params = params.set('departmentId', departmentId);
    return this.http.get(`${this.url}/branch-dashboard/export`, {
      params,
      responseType: 'blob',
    });
  }

  form(
    employeeId: string,
    periodId: string,
    comparePeriodId?: string | null,
  ): Observable<EvaluationFormModel> {
    let params = new HttpParams().set('employeeId', employeeId).set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    return this.http
      .get<ApiResponse<EvaluationFormModel>>(`${this.url}/form`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  exportEvaluationForm(
    employeeId: string,
    periodId: string,
    comparePeriodId?: string | null,
  ): Observable<Blob> {
    let params = new HttpParams().set('employeeId', employeeId).set('periodId', periodId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    return this.http.get(`${this.url}/form/export`, {
      params,
      responseType: 'blob',
    });
  }

  submit(request: SubmitEvaluationRequest): Observable<EvaluationResult> {
    return this.http
      .post<ApiResponse<EvaluationResult>>(`${this.url}/submit`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  getPersonalEvaluation(periodId?: string): Observable<PersonalEvaluationView> {
    let params = new HttpParams();
    if (periodId) {
      params = params.set('periodId', periodId);
    }
    return this.http
      .get<ApiResponse<PersonalEvaluationView>>(`${this.url}/personal`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  submitToBranch(request: EvaluationFlowRequest): Observable<EvaluationFlowResult> {
    return this.http
      .post<ApiResponse<EvaluationFlowResult>>(`${this.url}/submit-to-branch`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  returnToCalculated(request: ReturnEvaluationRequest): Observable<EvaluationFlowResult> {
    return this.http
      .post<ApiResponse<EvaluationFlowResult>>(`${this.url}/return-to-calculated`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  finalizeEvaluation(request: EvaluationFlowRequest): Observable<FinalizeEvaluationResult> {
    return this.http
      .post<ApiResponse<FinalizeEvaluationResult>>(`${this.url}/finalize`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  getPersonalCriteriaLedger(
    periodId: string,
    criteriaId: string,
  ): Observable<PersonalCriteriaLedgerItem[]> {
    const params = new HttpParams().set('periodId', periodId).set('criteriaId', criteriaId);
    return this.http
      .get<ApiResponse<PersonalCriteriaLedgerItem[]>>(`${this.url}/personal/ledger`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  rankingBoard(
    periodId: string,
    departmentId?: string | null,
    comparePeriodId?: string | null,
  ): Observable<RankingBoardOverview> {
    let params = new HttpParams().set('periodId', periodId);
    if (departmentId) params = params.set('departmentId', departmentId);
    if (comparePeriodId) params = params.set('comparePeriodId', comparePeriodId);
    return this.http
      .get<ApiResponse<RankingBoardOverview>>(`${this.url}/ranking-board`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  saveRankingBoardDraft(request: SaveRankingBoardRequest): Observable<RankingBoardOverview> {
    return this.http
      .post<ApiResponse<RankingBoardOverview>>(`${this.url}/ranking-board/draft`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  autoAssignRankingBoard(request: AutoAssignRankingBoardRequest): Observable<RankingBoardOverview> {
    return this.http
      .post<ApiResponse<RankingBoardOverview>>(`${this.url}/ranking-board/auto-assign`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  sendRankingBoardToDirector(request: SaveRankingBoardRequest): Observable<RankingBoardSendResult> {
    return this.http
      .post<ApiResponse<RankingBoardSendResult>>(
        `${this.url}/ranking-board/send-to-director`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  exportRankingBoard(periodId: string, departmentId: string): Observable<Blob> {
    const params = new HttpParams().set('periodId', periodId).set('departmentId', departmentId);
    return this.http.get(`${this.url}/ranking-board/export`, {
      params,
      responseType: 'blob',
    });
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid evaluation response');
    return response.data;
  }
}
