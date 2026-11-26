import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ImportBatchSummary,
  ImportedRating,
  ImportRowError,
  ImportUploadEvent,
} from './import-history.models';
import {
  getImportBatch,
  getImportErrors,
  getPaged,
  uploadImportFile,
} from './import-history.service';
import { PagedResult } from './paged-result.model';

/** Customer service rating import (UC29, UC32). Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md. */
@Injectable({ providedIn: 'root' })
export class ServiceRatingImportService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/service-rating-imports`;

  /** `POST /service-rating-imports` (multipart field `file`). Ratings are not reconciled: `reconcile` is always `null`. */
  upload(file: File): Observable<ImportUploadEvent> {
    return uploadImportFile(this.http, this.url, file);
  }

  /** `GET /service-rating-imports`: the rating batches, newest first. */
  list(page: number, pageSize: number): Observable<PagedResult<ImportBatchSummary>> {
    return getPaged<ImportBatchSummary>(this.http, this.url, page, pageSize);
  }

  /** `GET /service-rating-imports/{id}`: one rating batch with its counts. */
  detail(id: string): Observable<ImportBatchSummary> {
    return getImportBatch(this.http, this.url, id);
  }

  /** `GET /service-rating-imports/{id}/errors`: the rejected rows of a rating batch, ordered by row. */
  errors(id: string, page: number, pageSize: number): Observable<PagedResult<ImportRowError>> {
    return getImportErrors(this.http, this.url, id, page, pageSize);
  }

  /** `GET /service-rating-imports/{id}/ratings`: the ratings of a batch that were saved. */
  ratings(id: string, page: number, pageSize: number): Observable<PagedResult<ImportedRating>> {
    return getPaged<ImportedRating>(
      this.http,
      `${this.url}/${encodeURIComponent(id)}/ratings`,
      page,
      pageSize,
    );
  }

  /** `GET /service-rating-imports/template`: the workbook with the rating headers and one sample row. */
  downloadTemplate(): Observable<Blob> {
    return this.http.get(`${this.url}/template`, { responseType: 'blob' });
  }
}
