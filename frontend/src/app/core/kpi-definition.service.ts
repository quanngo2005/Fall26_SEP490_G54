import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
import { ApiResponse, PagedResult } from './api.models';
import { parseWireEnum, toWireOrdinal } from './enum-wire';
import {
  KPI_AGGREGATIONS,
  KPI_DIRECTIONS,
  KPI_STATUSES,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiFilter,
  KpiGroupOption,
} from './kpi-definition.models';

type RawKpiDefinition = Omit<KpiDefinition, 'status' | 'direction' | 'aggregationType'> & {
  status: unknown;
  direction: unknown;
  aggregationType: unknown;
};

@Injectable({ providedIn: 'root' })
export class KpiDefinitionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/kpi-definitions`;

  list(filter: KpiFilter = {}): Observable<PagedResult<KpiDefinition>> {
    return this.http
      .get<ApiResponse<PagedResult<RawKpiDefinition>>>(this.url, {
        params: toHttpParams(filter),
      })
      .pipe(
        map((response) => unwrapResponse(response, 'KPI definition list')),
        map((page) => ({ ...page, items: page.items.map((item) => this.adapt(item)) })),
      );
  }

  groups(): Observable<KpiGroupOption[]> {
    return this.http
      .get<ApiResponse<KpiGroupOption[]>>(`${this.url}/groups`)
      .pipe(map((response) => unwrapResponse(response, 'KPI group')));
  }

  create(payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http.post<ApiResponse<RawKpiDefinition>>(this.url, this.toWire(payload)).pipe(
      map((response) => unwrapResponse(response, 'KPI definition')),
      map((item) => this.adapt(item)),
    );
  }

  update(id: string, payload: KpiDefinitionSavePayload): Observable<KpiDefinition> {
    return this.http
      .put<ApiResponse<RawKpiDefinition>>(
        `${this.url}/${encodeURIComponent(id)}`,
        this.toWire(payload),
      )
      .pipe(
        map((response) => unwrapResponse(response, 'KPI definition')),
        map((item) => this.adapt(item)),
      );
  }

  private toWire(payload: KpiDefinitionSavePayload) {
    return {
      ...payload,
      direction: toWireOrdinal(KPI_DIRECTIONS, payload.direction),
      aggregationType: toWireOrdinal(KPI_AGGREGATIONS, payload.aggregationType),
    };
  }

  private adapt(raw: RawKpiDefinition): KpiDefinition {
    const direction = parseWireEnum(KPI_DIRECTIONS, raw.direction);
    const aggregationType = parseWireEnum(KPI_AGGREGATIONS, raw.aggregationType);
    if (!direction || !aggregationType) throw new Error('Invalid KPI definition response');
    return {
      ...raw,
      direction,
      aggregationType,
      status: parseWireEnum(KPI_STATUSES, raw.status),
    };
  }
}
