import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import { JobPositionDetail, JobPositionFilter, JobPositionSummary } from './job-position.models';

@Injectable({ providedIn: 'root' })
export class JobPositionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/job-positions`;

  list(filter: JobPositionFilter = {}) {
    return this.http
      .get<ApiResponse<PagedResult<JobPositionSummary>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string) {
    return this.http
      .get<ApiResponse<JobPositionDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => this.unwrap(response)));
  }

  private params(filter: JobPositionFilter) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null)
      throw new Error('Invalid job position response');
    return response.data;
  }
}
