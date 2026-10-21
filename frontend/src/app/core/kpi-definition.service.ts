import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import {
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiFilter,
  KpiGroupOption,
} from './kpi-definition.models';

@Injectable({ providedIn: 'root' })
export class KpiDefinitionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/kpi-definitions`;

  list(filter: KpiFilter = {}): Observable<PagedResult<KpiDefinition>> {
    return this.http
      .get<ApiResponse<PagedResult<KpiDefinition>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  groups(): Observable<KpiGroupOption[]> {
    return this.http
      .get<ApiResponse<KpiGroupOption[]>>(`${this.url}/groups`)
      .pipe(map((response) => this.unwrap(response)));
  }

  create(payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http
      .post<ApiResponse<KpiDefinition>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  update(id: string, payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http
      .put<ApiResponse<KpiDefinition>>(`${this.url}/${encodeURIComponent(id)}`, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: KpiFilter): HttpParams {
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
      throw new Error('Invalid KPI definition response');
    }
    return response.data;
  }
}
