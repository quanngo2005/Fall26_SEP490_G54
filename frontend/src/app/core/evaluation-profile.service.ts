import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { EvaluationProfileFilter, EvaluationProfileListItem } from './evaluation-profile.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class EvaluationProfileService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/evaluation-profiles`;

  list(filter: EvaluationProfileFilter = {}): Observable<PagedResult<EvaluationProfileListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '')
        params = params.set(key, String(value));
    }
    return this.http
      .get<ApiResponse<PagedResult<EvaluationProfileListItem>>>(this.url, { params })
      .pipe(
        map((response) => {
          if (!response.success || response.data == null)
            throw new Error('Invalid evaluation profile response');
          return response.data;
        }),
      );
  }
}
