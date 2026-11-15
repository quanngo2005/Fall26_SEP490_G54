import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  MySubmissionDetail,
  MySubmissionFilter,
  MySubmissionListItem,
} from './my-submission.models';
import { PagedResult } from './paged-result.model';

@Injectable({ providedIn: 'root' })
export class MySubmissionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/submissions`;

  /** The latest version of every work the caller submitted, newest first. */
  list(filter: MySubmissionFilter = {}): Observable<PagedResult<MySubmissionListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http
      .get<ApiResponse<PagedResult<MySubmissionListItem>>>(`${this.url}/mine`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(submissionId: string): Observable<MySubmissionDetail> {
    return this.http
      .get<ApiResponse<MySubmissionDetail>>(
        `${this.url}/${encodeURIComponent(submissionId)}/reconciliation`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** Evidence files need the bearer token, so they are fetched as a blob instead of linked directly. */
  evidence(evidenceId: string): Observable<Blob> {
    return this.http.get(
      `${environment.apiUrl}/evidence/${encodeURIComponent(evidenceId)}/preview`,
      {
        responseType: 'blob',
      },
    );
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message || 'Invalid submission response');
    }
    return response.data;
  }
}
