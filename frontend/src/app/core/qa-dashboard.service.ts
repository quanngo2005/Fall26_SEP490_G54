import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { QaDashboard, QaDashboardFilter, QaDashboardFilterOptions } from './qa-dashboard.models';

/** QA dashboard (SCR-PRF-04). Contract: docs/api/QA_DASHBOARD_CONTRACT.md. Requires `quality_flag:read`. */
@Injectable({ providedIn: 'root' })
export class QaDashboardService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiUrl;

  get(filter: QaDashboardFilter = {}): Observable<QaDashboard> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      params = params.set(key, String(value));
    }
    return this.http
      .get<ApiResponse<QaDashboard>>(`${this.api}/qa-dashboard`, { params })
      .pipe(map((response) => response.data));
  }

  filters(): Observable<QaDashboardFilterOptions> {
    return this.http
      .get<ApiResponse<QaDashboardFilterOptions>>(`${this.api}/qa-dashboard/filters`)
      .pipe(map((response) => response.data));
  }
}
