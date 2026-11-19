import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  CreateHandoverRequest,
  HandoverDetail,
  HandoverListItem,
  HandoverListQuery,
  HandoverRecipient,
} from './handover.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class HandoverService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /** Lists handovers visible to the signed-in user (SCR-WRK-05). */
  list(query: HandoverListQuery = {}): Observable<PagedResult<HandoverListItem>> {
    let params = new HttpParams();
    if (query.direction) params = params.set('direction', query.direction);
    if (query.status) params = params.set('status', query.status);
    if (query.fromEmployeeId) params = params.set('fromEmployeeId', query.fromEmployeeId);
    if (query.kpiStatus) params = params.set('kpiStatus', query.kpiStatus);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.page) params = params.set('page', String(query.page));
    if (query.pageSize) params = params.set('pageSize', String(query.pageSize));
    return this.http
      .get<ApiResponse<PagedResult<HandoverListItem>>>(`${this.baseUrl}/handovers`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Gets full detail for one handover record (SCR-WRK-05-D1). */
  detail(id: string): Observable<HandoverListItem> {
    return this.http
      .get<ApiResponse<HandoverListItem>>(`${this.baseUrl}/handovers/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Recipient accepts a pending handover request. */
  accept(id: string): Observable<HandoverDetail> {
    return this.http
      .post<ApiResponse<HandoverDetail>>(
        `${this.baseUrl}/handovers/${encodeURIComponent(id)}/accept`,
        {},
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Recipient rejects a pending handover request. */
  reject(id: string): Observable<HandoverDetail> {
    return this.http
      .post<ApiResponse<HandoverDetail>>(
        `${this.baseUrl}/handovers/${encodeURIComponent(id)}/reject`,
        {},
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Colleagues of the caller's department a work can be handed over to. */
  recipients(): Observable<HandoverRecipient[]> {
    return this.http
      .get<ApiResponse<HandoverRecipient[]>>(`${this.baseUrl}/handovers/recipients`)
      .pipe(map((response) => this.unwrap(response)));
  }

  create(workId: string, request: CreateHandoverRequest): Observable<HandoverDetail> {
    return this.http
      .post<ApiResponse<HandoverDetail>>(
        `${this.baseUrl}/works/${encodeURIComponent(workId)}/handovers`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid handover response');
    return response.data;
  }
}
