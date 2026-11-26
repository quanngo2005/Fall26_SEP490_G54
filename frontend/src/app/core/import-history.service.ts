import { HttpClient, HttpEventType, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { filter, map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  ImportBatchResult,
  ImportBatchSummary,
  ImportedTransaction,
  ImportRowError,
  ImportUploadEvent,
} from './import-history.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class ImportHistoryService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/transaction-imports`;

  list(page: number, pageSize: number): Observable<PagedResult<ImportBatchSummary>> {
    return getPaged<ImportBatchSummary>(this.http, this.url, page, pageSize);
  }

  /** `GET /transaction-imports/{id}`: one batch with its counts (`errors` stays `null`). */
  detail(id: string): Observable<ImportBatchSummary> {
    return getImportBatch(this.http, this.url, id);
  }

  /** `GET /transaction-imports/{id}/errors`: the rejected rows of a batch, ordered by row (UC33). */
  errors(id: string, page: number, pageSize: number): Observable<PagedResult<ImportRowError>> {
    return getImportErrors(this.http, this.url, id, page, pageSize);
  }

  /** `GET /transaction-imports/{id}/transactions`: the rows of a batch that were saved. */
  transactions(
    id: string,
    page: number,
    pageSize: number,
  ): Observable<PagedResult<ImportedTransaction>> {
    return getPaged<ImportedTransaction>(
      this.http,
      `${this.url}/${encodeURIComponent(id)}/transactions`,
      page,
      pageSize,
    );
  }

  /**
   * `POST /transaction-imports` (multipart field `file`). The server saves the valid rows and reconciles pending work
   * transaction codes in the same request, so the result carries both the row counts and `reconcile`.
   */
  upload(file: File): Observable<ImportUploadEvent> {
    return uploadImportFile(this.http, this.url, file);
  }

  /** `GET /transaction-imports/template`: the blank workbook with the template headers. */
  downloadTemplate(): Observable<Blob> {
    return this.http.get(`${this.url}/template`, { responseType: 'blob' });
  }
}

/** `GET {url}/{id}`: one import batch with its counts. Shared by every import file type. */
export function getImportBatch(
  http: HttpClient,
  url: string,
  id: string,
): Observable<ImportBatchSummary> {
  return http
    .get<ApiResponse<ImportBatchSummary>>(`${url}/${encodeURIComponent(id)}`)
    .pipe(map((response) => unwrap(response)));
}

/** `GET {url}/{id}/errors`: the rejected rows of a batch, ordered by row. Shared by every import file type. */
export function getImportErrors(
  http: HttpClient,
  url: string,
  id: string,
  page: number,
  pageSize: number,
): Observable<PagedResult<ImportRowError>> {
  return getPaged<ImportRowError>(http, `${url}/${encodeURIComponent(id)}/errors`, page, pageSize);
}

/** A paged `GET` with the `page`/`pageSize` query of the import contracts. */
export function getPaged<T>(
  http: HttpClient,
  url: string,
  page: number,
  pageSize: number,
): Observable<PagedResult<T>> {
  const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
  return http
    .get<ApiResponse<PagedResult<T>>>(url, { params })
    .pipe(map((response) => unwrap(response)));
}

function unwrap<T>(response: ApiResponse<T>): T {
  if (!response.success || response.data == null) {
    throw new Error('Invalid import history response');
  }
  return response.data;
}

/** Posts `file` as multipart field `file` to an import endpoint: upload progress (0-100), then the saved batch. */
export function uploadImportFile(
  http: HttpClient,
  url: string,
  file: File,
): Observable<ImportUploadEvent> {
  const body = new FormData();
  body.append('file', file, file.name);
  return http
    .post<ApiResponse<ImportBatchResult>>(url, body, {
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
        return { kind: 'done', result: unwrap(event.body) };
      }),
    );
}
