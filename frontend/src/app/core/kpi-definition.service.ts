import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
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
      .get<ApiResponse<PagedResult<KpiDefinition>>>(this.url, { params: toHttpParams(filter) })
      .pipe(map((response) => unwrapResponse(response, 'KPI definition list')));
  }

  groups(): Observable<KpiGroupOption[]> {
    return this.http
      .get<ApiResponse<KpiGroupOption[]>>(`${this.url}/groups`)
      .pipe(map((response) => unwrapResponse(response, 'KPI group')));
  }

  create(payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http
      .post<ApiResponse<KpiDefinition>>(this.url, payload)
      .pipe(map((response) => unwrapResponse(response, 'KPI definition')));
  }

  update(id: string, payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http
      .put<ApiResponse<KpiDefinition>>(`${this.url}/${encodeURIComponent(id)}`, payload)
      .pipe(map((response) => unwrapResponse(response, 'KPI definition')));
  }
}
