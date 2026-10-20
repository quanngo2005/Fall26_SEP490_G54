import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import { WorkTypeDetail, WorkTypeFilter } from './work-type.models';

@Injectable({ providedIn: 'root' })
export class WorkTypeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/work-types`;

  list(filter: WorkTypeFilter = {}): Observable<PagedResult<WorkTypeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<WorkTypeDetail>>>(this.url, { params: toHttpParams(filter) })
      .pipe(map((response) => unwrapResponse(response, 'work type list')));
  }

  detail(id: string): Observable<WorkTypeDetail> {
    return this.http
      .get<ApiResponse<WorkTypeDetail>>(`${this.url}/${encodeURIComponent(id)}`)
      .pipe(map((response) => unwrapResponse(response, 'work type')));
  }
}
