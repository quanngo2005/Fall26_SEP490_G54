import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../../core/api-response.model';
import {
  MyTargetConfirmResult,
  MyTargetFeedback,
  MyTargetFeedbackRequest,
  MyTargetProgress,
  MyTargetYear,
  ProgressPeriod,
} from './my-targets.models';

/**
 * HTTP client for the employee "my-targets" endpoints (docs/api/TARGET_ALLOCATION_CONTRACT.md § Employee side).
 * Owned by `features/my-targets/**`: new calls for this feature go here, never in `core/`.
 */
@Injectable({ providedIn: 'root' })
export class MyTargetsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/my-targets`;

  year(year: number): Observable<MyTargetYear> {
    return this.http
      .get<ApiResponse<MyTargetYear>>(`${this.url}/${year}`)
      .pipe(map((response) => response.data));
  }

  confirm(year: number): Observable<MyTargetConfirmResult> {
    return this.http
      .post<ApiResponse<MyTargetConfirmResult>>(`${this.url}/${year}/confirm`, {})
      .pipe(map((response) => response.data));
  }

  feedback(year: number, request: MyTargetFeedbackRequest): Observable<MyTargetFeedback> {
    return this.http
      .post<ApiResponse<MyTargetFeedback>>(`${this.url}/${year}/feedback`, request)
      .pipe(map((response) => response.data));
  }

  reply(feedbackId: string, reply: string): Observable<MyTargetFeedback> {
    return this.http
      .post<ApiResponse<MyTargetFeedback>>(`${this.url}/feedback/${feedbackId}/reply`, { reply })
      .pipe(map((response) => response.data));
  }

  progress(year: number, period: ProgressPeriod): Observable<MyTargetProgress> {
    const params = new HttpParams().set('period', period);
    return this.http
      .get<ApiResponse<MyTargetProgress>>(`${this.url}/${year}/progress`, { params })
      .pipe(map((response) => response.data));
  }
}
