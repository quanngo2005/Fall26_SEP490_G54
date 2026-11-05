import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { QaDecision, QaKind, QaQueue, QaQueueDetail, QaQueueFilter } from './qa-queue.models';

@Injectable({ providedIn: 'root' })
export class QaQueueService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiUrl;

  list(filter: QaQueueFilter = {}): Observable<QaQueue> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      params = params.set(key, String(value));
    }
    return this.http
      .get<ApiResponse<QaQueue>>(`${this.api}/qa-queue`, { params })
      .pipe(map((response) => response.data));
  }

  detail(kind: QaKind, id: string): Observable<QaQueueDetail> {
    return this.http
      .get<ApiResponse<QaQueueDetail>>(`${this.api}/qa-queue/${kind}/${encodeURIComponent(id)}`)
      .pipe(map((response) => response.data));
  }

  /**
   * Records QA's verdict on a queue item. A 5% sample is decided PASSED / FAILED (FAILED raises a flag and can send the
   * work back at once); a quality flag is dismissed (confirm correct) or sent back for rework (reject).
   */
  decide(kind: QaKind, id: string, decision: QaDecision): Observable<unknown> {
    const comment = decision.comment.trim();
    if (kind === 'SAMPLE') {
      const confirm = decision.verdict === 'CONFIRM';
      return this.http.post<ApiResponse<unknown>>(
        `${this.api}/qa-samples/${encodeURIComponent(id)}/decision`,
        confirm
          ? { result: 'PASSED', comment: comment || null, requestRework: false }
          : {
              result: 'FAILED',
              comment,
              severity: decision.severity,
              requestRework: decision.requestRework,
            },
      );
    }

    return decision.verdict === 'CONFIRM'
      ? this.http.post<ApiResponse<unknown>>(
          `${this.api}/quality-flags/${encodeURIComponent(id)}/dismiss`,
          {},
        )
      : this.http.post<ApiResponse<unknown>>(
          `${this.api}/quality-flags/${encodeURIComponent(id)}/request-rework`,
          { comment },
        );
  }

  /** Evidence files need the bearer token, so they are fetched as a blob instead of linked directly. */
  evidence(evidenceId: string): Observable<Blob> {
    return this.http.get(`${this.api}/evidence/${encodeURIComponent(evidenceId)}/preview`, {
      responseType: 'blob',
    });
  }
}
