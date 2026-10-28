import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PagedResult } from './paged-result.model';
import {
  CreateUserAccountPayload,
  CreateUserAccountResult,
  EligibleEmployee,
  UpdateUserAccountPayload,
  UserAccountDetail,
  UserAccountFilter,
  UserAccountSummary,
  UserAccountSummaryCounts,
} from './user-account.models';

@Injectable({ providedIn: 'root' })
export class UserAccountService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/user-accounts`;

  list(filter: UserAccountFilter = {}): Observable<PagedResult<UserAccountSummary>> {
    return this.http
      .get<ApiResponse<PagedResult<UserAccountSummary>>>(this.url, { params: this.params(filter) })
      .pipe(map((response) => this.unwrap(response)));
  }

  summary(): Observable<UserAccountSummaryCounts> {
    return this.http
      .get<ApiResponse<UserAccountSummaryCounts>>(`${this.url}/summary`)
      .pipe(map((response) => this.unwrap(response)));
  }

  eligibleEmployees(): Observable<EligibleEmployee[]> {
    return this.http
      .get<ApiResponse<EligibleEmployee[]>>(`${this.url}/eligible-employees`)
      .pipe(map((response) => this.unwrap(response)));
  }

  detail(id: string): Observable<UserAccountDetail> {
    return this.http
      .get<ApiResponse<UserAccountDetail>>(this.itemUrl(id))
      .pipe(map((response) => this.unwrap(response)));
  }

  create(payload: CreateUserAccountPayload): Observable<CreateUserAccountResult> {
    return this.http
      .post<ApiResponse<CreateUserAccountResult>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  update(id: string, payload: UpdateUserAccountPayload): Observable<UserAccountDetail> {
    return this.http
      .put<ApiResponse<UserAccountDetail>>(this.itemUrl(id), payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  private itemUrl(id: string): string {
    return `${this.url}/${encodeURIComponent(id)}`;
  }

  private params(filter: UserAccountFilter): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid user account response');
    }
    return response.data;
  }
}
