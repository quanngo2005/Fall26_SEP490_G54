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
  KpiHistoryEntry,
  KpiRequestPayload,
  KpiStatus,
  KpiSummary,
  KpiUsage,
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

  /** The code a new KPI with this name would get (its name without accents, numbered when taken). */
  codeSuggestion(name: string): Observable<string> {
    return this.http
      .get<ApiResponse<{ code: string }>>(`${this.url}/code-suggestion`, { params: { name } })
      .pipe(map((response) => this.unwrap(response).code));
  }

  summary(): Observable<KpiSummary> {
    return this.http
      .get<ApiResponse<KpiSummary>>(`${this.url}/summary`)
      .pipe(map((response) => this.unwrap(response)));
  }

  usage(id: string): Observable<KpiUsage> {
    return this.http
      .get<ApiResponse<KpiUsage>>(`${this.url}/${encodeURIComponent(id)}/usage`)
      .pipe(map((response) => this.unwrap(response)));
  }

  history(id: string): Observable<KpiHistoryEntry[]> {
    return this.http
      .get<ApiResponse<KpiHistoryEntry[]>>(`${this.url}/${encodeURIComponent(id)}/history`)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Branch Manager: asks the System Admin for a KPI that is missing. */
  request(payload: KpiRequestPayload): Observable<KpiDefinition> {
    return this.http
      .post<ApiResponse<KpiDefinition>>(`${this.url}/requests`, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  /** The allowed moves are in docs/api/KPI_DEFINITION_CONTRACT.md; sending for approval is `PENDING_APPROVAL`. */
  changeStatus(id: string, status: KpiStatus): Observable<void> {
    return this.http
      .patch<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/status`, { status })
      .pipe(map(() => undefined));
  }

  approve(id: string): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/approve`, null)
      .pipe(map(() => undefined));
  }

  reject(id: string, reason: string): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(`${this.url}/${encodeURIComponent(id)}/reject`, { reason })
      .pipe(map(() => undefined));
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
