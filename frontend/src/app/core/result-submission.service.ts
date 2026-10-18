import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './kpi-definition.models';
import { SubmissionContext, SubmissionDetail, SubmissionResult } from './result-submission.models';

@Injectable({ providedIn: 'root' })
export class ResultSubmissionService {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiUrl;

  getSubmission(submissionId: string): Observable<SubmissionDetail> {
    return this.http
      .get<ApiResponse<SubmissionDetail>>(
        `${this.url}/submissions/${encodeURIComponent(submissionId)}`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  getSubmissionsByWork(workId: string): Observable<SubmissionDetail[]> {
    return this.http
      .get<ApiResponse<SubmissionDetail[]>>(
        `${this.url}/works/${encodeURIComponent(workId)}/submissions`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  getSubmissionContext(workId: string): Observable<SubmissionContext> {
    return this.http
      .get<ApiResponse<SubmissionContext>>(
        `${this.url}/works/${encodeURIComponent(workId)}/submission-context`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  createSubmission(workId: string, formData: FormData): Observable<SubmissionResult> {
    return this.http
      .post<ApiResponse<SubmissionResult>>(
        `${this.url}/works/${encodeURIComponent(workId)}/submissions`,
        formData,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  withdraw(submissionId: string): Observable<SubmissionResult> {
    return this.http
      .post<ApiResponse<SubmissionResult>>(
        `${this.url}/submissions/${encodeURIComponent(submissionId)}/withdraw`,
        {},
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message || 'Invalid result submission response');
    }
    return response.data;
  }
}
