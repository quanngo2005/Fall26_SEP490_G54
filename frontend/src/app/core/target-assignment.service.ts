import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  TargetAllocationRequest,
  TargetAssignment,
  TargetAssignmentFeedback,
  TargetAssignmentFilter,
  TargetBreakdown,
  TargetReallocationRequest,
} from './target-assignment.models';

@Injectable({ providedIn: 'root' })
export class TargetAssignmentService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/target-assignments`;

  list(filter: TargetAssignmentFilter = {}): Observable<TargetAssignment[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http
      .get<ApiResponse<TargetAssignment[]>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  feedback(id: string): Observable<TargetAssignmentFeedback[]> {
    return this.http
      .get<ApiResponse<TargetAssignmentFeedback[]>>(
        `${this.url}/${encodeURIComponent(id)}/feedback`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Splits the target `{id}` over its children in one transaction (201). */
  allocate(id: string, request: TargetAllocationRequest): Observable<TargetAssignment[]> {
    return this.http
      .post<ApiResponse<TargetAssignment[]>>(
        `${this.url}/${encodeURIComponent(id)}/allocations`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Gives every changed owner a new version (the old one is superseded); returns the new versions. */
  reallocate(id: string, request: TargetReallocationRequest): Observable<TargetAssignment[]> {
    return this.http
      .put<ApiResponse<TargetAssignment[]>>(
        `${this.url}/${encodeURIComponent(id)}/allocations`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  breakdown(
    id: string,
    granularity: 'MONTH' | 'WEEK' | 'DAY' = 'MONTH',
  ): Observable<TargetBreakdown> {
    return this.http
      .get<ApiResponse<TargetBreakdown>>(`${this.url}/${encodeURIComponent(id)}/breakdown`, {
        params: { granularity },
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid target assignment response');
    }
    return response.data;
  }
}
