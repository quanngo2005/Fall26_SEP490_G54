import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { CreateHandoverRequest, HandoverDetail, HandoverRecipient } from './handover.models';

@Injectable({ providedIn: 'root' })
export class HandoverService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

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
