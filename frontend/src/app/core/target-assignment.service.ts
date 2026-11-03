import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  KpiAllocation,
  PublishResult,
  QuarterSplit,
  SaveKpiAllocationRequest,
  SaveQuarterSplitRequest,
  TargetAssignment,
  TargetAssignmentFeedback,
  TargetAssignmentFilter,
  TargetImportCommitResult,
  TargetImportPreview,
  TargetSetup,
} from './target-assignment.models';

@Injectable({ providedIn: 'root' })
export class TargetAssignmentService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/target-assignments`;

  list(filter: TargetAssignmentFilter = {}): Observable<TargetAssignment[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http
      .get<ApiResponse<TargetAssignment[]>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  feedback(id: string): Observable<TargetAssignmentFeedback[]> {
    return this.http
      .get<ApiResponse<TargetAssignmentFeedback[]>>(
        `${this.url}/${encodeURIComponent(id)}/feedback`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /target-assignments/import/preview?branchId&year`: uploads the file and returns the preview. */
  importPreview(branchId: string, year: number, file: File): Observable<TargetImportPreview> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    const params = new HttpParams().set('branchId', branchId).set('year', year);
    return this.http
      .post<ApiResponse<TargetImportPreview>>(`${this.url}/import/preview`, formData, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /target-assignments/import/commit?branchId&year`: re-uploads the previewed file and writes its valid rows (D9 partial import). */
  importCommit(branchId: string, year: number, file: File): Observable<TargetImportCommitResult> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    const params = new HttpParams().set('branchId', branchId).set('year', year);
    return this.http
      .post<ApiResponse<TargetImportCommitResult>>(`${this.url}/import/commit`, formData, {
        params,
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `GET /target-assignments/import/template?year`: downloads the blank workbook. */
  downloadTemplate(year: number): Observable<Blob> {
    return this.http.get(`${this.url}/import/template`, {
      params: new HttpParams().set('year', year),
      responseType: 'blob',
    });
  }

  /** `GET /target-assignments/setup?branchId&year`: wizard state and completion counts (phase 2). */
  getSetup(branchId: string, year: number): Observable<TargetSetup> {
    const params = new HttpParams().set('branchId', branchId).set('year', year);
    return this.http
      .get<ApiResponse<TargetSetup>>(`${this.url}/setup`, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `GET /target-assignments/{branchYearId}/quarter-split`: year targets split by quarter. */
  getQuarterSplit(branchYearId: string): Observable<QuarterSplit> {
    return this.http
      .get<ApiResponse<QuarterSplit>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/quarter-split`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `PUT /target-assignments/{branchYearId}/quarter-split`: saves the quarter split and returns its new state. */
  saveQuarterSplit(
    branchYearId: string,
    request: SaveQuarterSplitRequest,
  ): Observable<QuarterSplit> {
    return this.http
      .put<ApiResponse<QuarterSplit>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/quarter-split`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `GET /target-assignments/{branchYearId}/kpis/{kpiId}/allocation`: one KPI's department allocation. */
  getKpiAllocation(branchYearId: string, kpiId: string): Observable<KpiAllocation> {
    return this.http
      .get<ApiResponse<KpiAllocation>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/kpis/${encodeURIComponent(kpiId)}/allocation`,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `PUT /target-assignments/{branchYearId}/kpis/{kpiId}/allocation`: saves one KPI's department allocation. */
  saveKpiAllocation(
    branchYearId: string,
    kpiId: string,
    request: SaveKpiAllocationRequest,
  ): Observable<KpiAllocation> {
    return this.http
      .put<ApiResponse<KpiAllocation>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/kpis/${encodeURIComponent(kpiId)}/allocation`,
        request,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  /** `POST /target-assignments/{branchYearId}/publish`: activates the branch-year targets. */
  publish(branchYearId: string): Observable<PublishResult> {
    return this.http
      .post<ApiResponse<PublishResult>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/publish`,
        null,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid target assignment response');
    }
    return response.data;
  }
}
