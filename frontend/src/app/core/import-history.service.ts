import { HttpClient, HttpEventType, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { filter, map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { ImportBatchResult, ImportBatchSummary, ImportUploadEvent } from './import-history.models';
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

  /**
   * `POST /transaction-imports` (multipart field `file`). The server saves the valid rows and reconciles pending work
   * transaction codes in the same request, so the result carries both the row counts and `reconcile`.
   */
  upload(file: File): Observable<ImportUploadEvent> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http
      .post<ApiResponse<ImportBatchResult>>(this.url, body, {
        reportProgress: true,
        observe: 'events',
      })
      .pipe(
        filter(
          (event) =>
            event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response,
        ),
        map((event): ImportUploadEvent => {
          if (event.type === HttpEventType.UploadProgress) {
            const percent = event.total ? Math.round((100 * event.loaded) / event.total) : 0;
            return { kind: 'progress', percent };
          }
          if (event.type !== HttpEventType.Response || !event.body) {
            throw new Error('Invalid import response');
          }
          return { kind: 'done', result: this.unwrap(event.body) };
        }),
      );
  }

  /** `GET /transaction-imports/template`: the blank workbook with the template headers. */
  downloadTemplate(): Observable<Blob> {
    return this.http.get(`${this.url}/template`, { responseType: 'blob' });
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid import history response');
    }
    return response.data;
  }
}
