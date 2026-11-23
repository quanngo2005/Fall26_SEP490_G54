import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  EmployeeAllocation,
  MyTargetFeedbackReplyResult,
  PublishEmployeesResult,
  SaveEmployeeAllocationRequest,
  SuggestionEngineRequest,
  SuggestionEngineResponse,
} from './target-allocation.models';

/**
 * Phase 3 (tier-3 employee allocation) and suggestion-engine calls, not yet exposed by
 * `core/target-assignment.service.ts` (docs/api/TARGET_ALLOCATION_CONTRACT.md, "Phase 3" and
 * "Suggestion engine"). New endpoints for this DM feature live here instead of under `core/**`
 * (file ownership: `features/target-allocation/**` only).
 */
@Injectable({ providedIn: 'root' })
export class TargetAllocationService {
  private readonly http = inject(HttpClient);
  private readonly assignmentsUrl = `${environment.apiUrl}/target-assignments`;
  private readonly suggestionsUrl = `${environment.apiUrl}/target-suggestions`;
  private readonly myTargetsUrl = `${environment.apiUrl}/my-targets`;

  /** `GET /{deptYearId}/employee-allocation`: one KPI's employee grid. */
  getEmployeeAllocation(deptYearId: string): Observable<EmployeeAllocation> {
    return this.http
      .get<ApiResponse<EmployeeAllocation>>(
        `${this.assignmentsUrl}/${encodeURIComponent(deptYearId)}/employee-allocation`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `GET /{deptYearId}/employee-allocations`: every KPI's employee grid for that department. */
  getEmployeeAllocations(deptYearId: string): Observable<EmployeeAllocation[]> {
    return this.http
      .get<ApiResponse<EmployeeAllocation[]>>(
        `${this.assignmentsUrl}/${encodeURIComponent(deptYearId)}/employee-allocations`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `PUT /{deptYearId}/employee-allocation`: saves one KPI's employee split. */
  saveEmployeeAllocation(
    deptYearId: string,
    request: SaveEmployeeAllocationRequest,
  ): Observable<EmployeeAllocation> {
    return this.http
      .put<ApiResponse<EmployeeAllocation>>(
        `${this.assignmentsUrl}/${encodeURIComponent(deptYearId)}/employee-allocation`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /{deptYearId}/publish-employees`: publishes every KPI's employee allocations of that department. */
  publishEmployees(deptYearId: string): Observable<PublishEmployeesResult> {
    return this.http
      .post<ApiResponse<PublishEmployeesResult>>(
        `${this.assignmentsUrl}/${encodeURIComponent(deptYearId)}/publish-employees`,
        null,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /target-suggestions/{year}`: read-only tier-3 suggested split (D1/D4/D5/D6/D8/D9). */
  suggest(year: number, request: SuggestionEngineRequest): Observable<SuggestionEngineResponse> {
    return this.http
      .post<ApiResponse<SuggestionEngineResponse>>(`${this.suggestionsUrl}/${year}`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /my-targets/feedback/{feedbackId}/reply`: DM-only reply to an employee's feedback. */
  replyToFeedback(feedbackId: string, reply: string): Observable<MyTargetFeedbackReplyResult> {
    return this.http
      .post<ApiResponse<MyTargetFeedbackReplyResult>>(
        `${this.myTargetsUrl}/feedback/${encodeURIComponent(feedbackId)}/reply`,
        { reply },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid target allocation response');
    }
    return response.data;
  }
}
