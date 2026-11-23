import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import {
  CreateWorkRequest,
  UpdateWorkPlanRequest,
  WorkActivity,
  WorkDetail,
  WorkFilter,
  WorkListItem,
  WorkStatusChange,
} from './work.models';

@Injectable({ providedIn: 'root' })
export class WorkService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/works`;

  list(filter: WorkFilter = {}): Observable<PagedResult<WorkListItem>> {
    return this.http
      .get<ApiResponse<PagedResult<WorkListItem>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<WorkDetail> {
    return this.http
      .get<ApiResponse<WorkDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  create(request: CreateWorkRequest): Observable<WorkDetail> {
    return this.http
      .post<ApiResponse<WorkDetail>>(this.url, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The activity log of one work, newest first. */
  activities(id: string): Observable<WorkActivity[]> {
    return this.http
      .get<ApiResponse<WorkActivity[]>>(`${this.url}/${encodeURIComponent(id)}/activities`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The assignee ticks or unticks one checklist item; the response is the work with its new progress. */
  setChecklistItem(id: string, itemId: string, isDone: boolean): Observable<WorkDetail> {
    return this.http
      .patch<ApiResponse<WorkDetail>>(
        `${this.url}/${encodeURIComponent(id)}/checklist-items/${encodeURIComponent(itemId)}`,
        { isDone },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The assignee's manager changes the deadline, priority and checklist of an open work. */
  updatePlan(id: string, request: UpdateWorkPlanRequest): Observable<WorkDetail> {
    return this.http
      .put<ApiResponse<WorkDetail>>(`${this.url}/${encodeURIComponent(id)}/plan`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Allowed targets are `InProgress`, `Done` and `Cancelled`; any other transition is a 409 `WORK_INVALID_STATE`. */
  changeStatus(id: string, change: WorkStatusChange): Observable<WorkDetail> {
    return this.http
      .patch<ApiResponse<WorkDetail>>(`${this.url}/${encodeURIComponent(id)}/status`, change)
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: WorkFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      if (Array.isArray(value)) {
        if (value.length > 0) params = params.set(key, value.join(','));
        continue;
      }
      params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid work response');
    return response.data;
  }
}
