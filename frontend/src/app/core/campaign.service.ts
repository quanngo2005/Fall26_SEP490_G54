import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  BatchWorkPayload,
  BatchWorkResult,
  CampaignDetail,
  CampaignFilter,
  CampaignListOverview,
  CampaignObjective,
  CreateCampaignPayload,
} from './campaign.models';

@Injectable({ providedIn: 'root' })
export class CampaignService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/campaigns`;

  list(filter: CampaignFilter = {}): Observable<CampaignListOverview> {
    return this.http
      .get<ApiResponse<CampaignListOverview>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<CampaignDetail> {
    return this.http
      .get<ApiResponse<CampaignDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  objectives(): Observable<CampaignObjective[]> {
    return this.http
      .get<ApiResponse<CampaignObjective[]>>(`${environment.apiUrl}/business-objectives`)
      .pipe(map((response) => this.unwrap(response)));
  }

  create(payload: CreateCampaignPayload): Observable<CampaignDetail> {
    return this.http
      .post<ApiResponse<CampaignDetail>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  batchGenerateWork(id: string, payload: BatchWorkPayload): Observable<BatchWorkResult> {
    return this.http
      .post<ApiResponse<BatchWorkResult>>(
        `${this.url}/${encodeURIComponent(id)}/batch-generate-work`,
        payload,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: CampaignFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid campaign response');
    }
    return response.data;
  }
}
