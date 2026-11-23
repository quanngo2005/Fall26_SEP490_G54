import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  EvaluationComparisonSetting,
  RankingPolicy,
  SimulateRankingPolicyPayload,
  SimulateRankingPolicyResult,
  SystemParameter,
  SystemParametersSavePayload,
  TierCriteriaMatrix,
  TierCriterionItem,
  UpdateRankingPolicyPayload,
} from './system-parameter.models';

@Injectable({ providedIn: 'root' })
export class SystemParameterService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/system-parameters`;

  list(): Observable<SystemParameter[]> {
    return this.http
      .get<ApiResponse<SystemParameter[]>>(this.url)
      .pipe(map((response) => this.unwrap(response)));
  }

  save(payload: SystemParametersSavePayload): Observable<SystemParameter[]> {
    return this.http
      .put<ApiResponse<SystemParameter[]>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  getComparison(): Observable<EvaluationComparisonSetting> {
    return this.http
      .get<ApiResponse<EvaluationComparisonSetting>>(`${this.url}/evaluation-comparison`)
      .pipe(map((response) => this.unwrap(response)));
  }

  saveComparison(payload: EvaluationComparisonSetting): Observable<EvaluationComparisonSetting> {
    return this.http
      .put<ApiResponse<EvaluationComparisonSetting>>(`${this.url}/evaluation-comparison`, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  getTierCriteria(): Observable<TierCriteriaMatrix> {
    return this.http
      .get<ApiResponse<TierCriteriaMatrix>>(`${this.url}/tier-criteria`)
      .pipe(map((response) => this.unwrap(response)));
  }

  saveTierCriteria(criteria: TierCriterionItem[]): Observable<TierCriteriaMatrix> {
    return this.http
      .put<ApiResponse<TierCriteriaMatrix>>(`${this.url}/tier-criteria`, { criteria })
      .pipe(map((response) => this.unwrap(response)));
  }

  getRankingPolicy(periodId?: string | null): Observable<RankingPolicy> {
    let params = new HttpParams();
    if (periodId) {
      params = params.set('periodId', periodId);
    }
    return this.http
      .get<ApiResponse<RankingPolicy>>(`${this.url}/ranking-policy`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  saveRankingPolicy(payload: UpdateRankingPolicyPayload): Observable<RankingPolicy> {
    return this.http
      .put<ApiResponse<RankingPolicy>>(`${this.url}/ranking-policy`, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  simulateRankingPolicy(
    payload: SimulateRankingPolicyPayload,
  ): Observable<SimulateRankingPolicyResult> {
    return this.http
      .post<ApiResponse<SimulateRankingPolicyResult>>(
        `${this.url}/ranking-policy/simulate`,
        payload,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid system parameter response');
    }
    return response.data;
  }
}
