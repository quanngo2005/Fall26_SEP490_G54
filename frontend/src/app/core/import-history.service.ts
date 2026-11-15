import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { ImportBatchSummary } from './import-history.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class ImportHistoryService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/transaction-imports`;

  list(page: number, pageSize: number): Observable<PagedResult<ImportBatchSummary>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.http
      .get<ApiResponse<PagedResult<ImportBatchSummary>>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid import history response');
    }
    return response.data;
  }
}
