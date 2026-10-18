import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { toHttpParams, unwrapResponse } from './api-utils';
import { ApiResponse, PagedResult } from './api.models';
import { parseWireEnum } from './enum-wire';
import {
  WORK_TYPE_STATUS_FIRST_ORDINAL,
  WORK_TYPE_STATUSES,
  WorkTypeDetail,
  WorkTypeFilter,
} from './work-type.models';

type RawWorkType = Omit<WorkTypeDetail, 'status'> & { status: unknown };

@Injectable({ providedIn: 'root' })
export class WorkTypeService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/work-types`;

  list(filter: WorkTypeFilter = {}): Observable<PagedResult<WorkTypeDetail>> {
    return this.http
      .get<ApiResponse<PagedResult<RawWorkType>>>(this.url, { params: toHttpParams(filter) })
      .pipe(
        map((response) => unwrapResponse(response, 'work type list')),
        map((page) => ({ ...page, items: page.items.map((item) => this.adapt(item)) })),
      );
  }

  detail(id: string): Observable<WorkTypeDetail> {
    return this.http.get<ApiResponse<RawWorkType>>(`${this.url}/${encodeURIComponent(id)}`).pipe(
      map((response) => unwrapResponse(response, 'work type')),
      map((item) => this.adapt(item)),
    );
  }

  private adapt(raw: RawWorkType): WorkTypeDetail {
    return {
      ...raw,
      status: parseWireEnum(WORK_TYPE_STATUSES, raw.status, WORK_TYPE_STATUS_FIRST_ORDINAL),
    };
  }
}
