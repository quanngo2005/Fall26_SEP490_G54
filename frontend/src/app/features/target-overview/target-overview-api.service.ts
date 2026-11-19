import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../../core/api-response.model';

/**
 * Calls for the BM overview (W1/W2/W8) that are not yet part of `docs/api/TARGET_ALLOCATION_CONTRACT.md` /
 * `TargetAssignmentService` (core). Everything already in the contract (setup, quarter-split, allocations,
 * publish) is called through `TargetAssignmentService` instead - this service only holds net-new calls.
 */
export interface RemindDepartmentManagerResult {
  readonly departmentId: string;
  readonly sentAt: string;
}

@Injectable({ providedIn: 'root' })
export class TargetOverviewApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/target-assignments`;

  /**
   * W8 "Nhắc Trưởng phòng". Not in `TARGET_ALLOCATION_CONTRACT.md` yet - the BE agent is adding a
   * "remind manager" endpoint in parallel. Assumed shape until the contract is updated:
   * `POST /target-assignments/{branchYearId}/departments/{departmentId}/remind` (no body, 200/201).
   * If the route differs once merged, only this method needs to change.
   */
  remindDepartmentManager(
    branchYearId: string,
    departmentId: string,
  ): Observable<RemindDepartmentManagerResult> {
    return this.http
      .post<ApiResponse<RemindDepartmentManagerResult>>(
        `${this.url}/${encodeURIComponent(branchYearId)}/departments/${encodeURIComponent(departmentId)}/remind`,
        null,
      )
      .pipe(map((response) => response.data));
  }
}
