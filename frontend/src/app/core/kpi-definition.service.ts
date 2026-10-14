import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ApiResponse,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiFilter,
  KpiGroupOption,
  KpiStatus,
  PagedResult,
} from './kpi-definition.models';

export const INITIAL_MOCK_KPIS: KpiDefinition[] = [
  {
    id: 'f87a8f11-001a-47d3-a4c2-9a00a1200001',
    kpiGroupId: 'grp-001',
    kpiGroupName: 'Kinh doanh',
    code: 'KPI-RET-01',
    name: 'Tổng số dư huy động mới',
    description: 'Đo lường tổng số dư huy động vốn mới trong kỳ, áp dụng hệ số theo loại tiền gửi.',
    formula: 'FORMULA_SUM_DEPOSIT',
    unit: 'VND',
    aggregationType: 'Sum',
    direction: 'HigherBetter',
    maxAchievementPercent: 150,
    status: 'Active',
    updatedAt: '2026-10-14T08:30:00Z',
    depositMultipliers: [
      { id: 'm-1', label: 'Tiền gửi kỳ hạn dài', value: 1.5 },
      { id: 'm-2', label: 'Tiền gửi không kỳ hạn', value: 1.0 },
    ],
    roleWeights: [
      { id: 'rw-1', role: 'Teller', weight: 40 },
      { id: 'rw-2', role: 'RBO', weight: 60 },
    ],
  },
  {
    id: 'f87a8f11-002b-47d3-a4c2-9a00a1200002',
    kpiGroupId: 'grp-002',
    kpiGroupName: 'Vận hành',
    code: 'KPI-OP-02',
    name: 'Tỷ lệ lỗi giao dịch',
    description:
      'Đo lường tỷ lệ các giao dịch sai sót hoặc cần điều chỉnh so với tổng khối lượng giao dịch trong kỳ.',
    formula: 'RATIO(ERROR_TX, TOTAL_TX) * 100',
    unit: '%',
    aggregationType: 'Ratio',
    direction: 'LowerBetter',
    maxAchievementPercent: 100,
    status: 'Active',
    updatedAt: '2026-10-14T09:15:00Z',
    depositMultipliers: [],
    roleWeights: [
      { id: 'rw-3', role: 'Teller', weight: 70 },
      { id: 'rw-4', role: 'BackOffice', weight: 30 },
    ],
  },
  {
    id: 'f87a8f11-003c-47d3-a4c2-9a00a1200003',
    kpiGroupId: 'grp-001',
    kpiGroupName: 'Kinh doanh',
    code: 'KPI-LN-03',
    name: 'Tăng trưởng dư nợ tín dụng',
    description: 'Tỷ lệ tăng trưởng dư nợ cho vay bán lẻ so với chỉ tiêu đầu kỳ được phê duyệt.',
    formula: 'FORMULA_CREDIT_GROWTH',
    unit: 'VND',
    aggregationType: 'Sum',
    direction: 'HigherBetter',
    maxAchievementPercent: 140,
    status: 'Active',
    updatedAt: '2026-10-14T10:00:00Z',
    depositMultipliers: [
      { id: 'm-3', label: 'Vay thế chấp BĐS', value: 1.2 },
      { id: 'm-4', label: 'Vay tín chấp', value: 1.8 },
    ],
    roleWeights: [
      { id: 'rw-5', role: 'LoanOfficer', weight: 60 },
      { id: 'rw-6', role: 'RelationshipMgr', weight: 40 },
    ],
  },
];

export const INITIAL_MOCK_GROUPS: KpiGroupOption[] = [
  { id: 'grp-001', code: 'BUS', name: 'Kinh doanh' },
  { id: 'grp-002', code: 'OPS', name: 'Vận hành' },
  { id: 'grp-003', code: 'FIN', name: 'Tài chính' },
  { id: 'grp-004', code: 'SVC', name: 'Dịch vụ khách hàng' },
];

@Injectable({ providedIn: 'root' })
export class KpiDefinitionService {
  private readonly http = inject(HttpClient);
  private localMockKpis: KpiDefinition[] = [...INITIAL_MOCK_KPIS];

  getKpis(filter?: KpiFilter): Observable<ApiResponse<PagedResult<KpiDefinition>>> {
    let params = new HttpParams();
    if (filter?.keyword) {
      params = params.set('keyword', filter.keyword);
    }
    if (filter?.groupId) {
      params = params.set('kpiGroupId', filter.groupId);
    }
    if (filter?.status) {
      params = params.set('status', filter.status);
    }
    if (filter?.page) {
      params = params.set('page', filter.page.toString());
    }
    if (filter?.pageSize) {
      params = params.set('pageSize', filter.pageSize.toString());
    }

    return this.http
      .get<ApiResponse<PagedResult<KpiDefinition>>>(`${environment.apiUrl}/v1/kpi-definitions`, {
        params,
      })
      .pipe(
        map((res) => {
          if (!res.data || !res.data.items || res.data.items.length === 0) {
            return this.getLocalMockPaged(filter);
          }
          return res;
        }),
        catchError(() => of(this.getLocalMockPaged(filter))),
      );
  }

  getGroups(): Observable<ApiResponse<KpiGroupOption[]>> {
    return this.http
      .get<ApiResponse<KpiGroupOption[]>>(`${environment.apiUrl}/v1/kpi-definitions/groups`)
      .pipe(
        map((res) => {
          if (!res.data || res.data.length === 0) {
            return { success: true, status: 200, data: INITIAL_MOCK_GROUPS };
          }
          return res;
        }),
        catchError(() => of({ success: true, status: 200, data: INITIAL_MOCK_GROUPS })),
      );
  }

  getKpiById(id: string): Observable<ApiResponse<KpiDefinition>> {
    return this.http
      .get<ApiResponse<KpiDefinition>>(`${environment.apiUrl}/v1/kpi-definitions/${id}`)
      .pipe(
        catchError(() => {
          const match = this.localMockKpis.find((k) => k.id === id) ?? this.localMockKpis[0];
          return of({ success: true, status: 200, data: match });
        }),
      );
  }

  createKpi(payload: KpiDefinitionSavePayload): Observable<ApiResponse<KpiDefinition>> {
    return this.http
      .post<ApiResponse<KpiDefinition>>(`${environment.apiUrl}/v1/kpi-definitions`, payload)
      .pipe(
        map((res) => {
          this.localMockKpis = [res.data, ...this.localMockKpis];
          return res;
        }),
        catchError(() => {
          const groupName =
            INITIAL_MOCK_GROUPS.find((g) => g.id === payload.kpiGroupId)?.name ?? 'Kinh doanh';
          const newKpi: KpiDefinition = {
            id: 'mock-' + Date.now(),
            kpiGroupId: payload.kpiGroupId,
            kpiGroupName: groupName,
            code: payload.code,
            name: payload.name,
            description: payload.description ?? null,
            formula: payload.formula ?? null,
            unit: payload.unit,
            aggregationType: payload.aggregationType,
            direction: payload.direction,
            maxAchievementPercent: payload.maxAchievementPercent ?? 100,
            status: 'Active',
            updatedAt: new Date().toISOString(),
            depositMultipliers: [],
            roleWeights: [
              { id: 'rw-new-1', role: 'Teller', weight: 50 },
              { id: 'rw-new-2', role: 'Officer', weight: 50 },
            ],
          };
          this.localMockKpis = [newKpi, ...this.localMockKpis];
          return of({ success: true, status: 201, data: newKpi });
        }),
      );
  }

  updateKpi(id: string, payload: KpiDefinitionSavePayload): Observable<ApiResponse<KpiDefinition>> {
    return this.http
      .put<ApiResponse<KpiDefinition>>(`${environment.apiUrl}/v1/kpi-definitions/${id}`, payload)
      .pipe(
        map((res) => {
          this.updateLocalKpi(res.data);
          return res;
        }),
        catchError(() => {
          const existing = this.localMockKpis.find((k) => k.id === id);
          const updated: KpiDefinition = {
            ...(existing ?? this.localMockKpis[0]),
            name: payload.name,
            description: payload.description ?? null,
            formula: payload.formula ?? null,
            unit: payload.unit,
            aggregationType: payload.aggregationType,
            direction: payload.direction,
            updatedAt: new Date().toISOString(),
          };
          this.updateLocalKpi(updated);
          return of({ success: true, status: 200, data: updated });
        }),
      );
  }

  changeStatus(id: string, status: KpiStatus): Observable<ApiResponse<void>> {
    return this.http
      .patch<ApiResponse<void>>(`${environment.apiUrl}/v1/kpi-definitions/${id}/status`, {
        status,
      })
      .pipe(catchError(() => of({ success: true, status: 200, data: undefined as void })));
  }

  private updateLocalKpi(updated: KpiDefinition): void {
    const idx = this.localMockKpis.findIndex((k) => k.id === updated.id);
    if (idx !== -1) {
      this.localMockKpis[idx] = updated;
    } else {
      this.localMockKpis.push(updated);
    }
  }

  private getLocalMockPaged(filter?: KpiFilter): ApiResponse<PagedResult<KpiDefinition>> {
    let items = [...this.localMockKpis];
    if (filter?.keyword) {
      const q = filter.keyword.toLowerCase();
      items = items.filter(
        (k) =>
          k.code.toLowerCase().includes(q) ||
          k.name.toLowerCase().includes(q) ||
          (k.description && k.description.toLowerCase().includes(q)),
      );
    }
    if (filter?.groupId && filter.groupId !== 'all') {
      items = items.filter((k) => k.kpiGroupId === filter.groupId);
    }
    return {
      success: true,
      status: 200,
      data: {
        items,
        totalCount: items.length,
        page: filter?.page ?? 1,
        pageSize: filter?.pageSize ?? 20,
      },
    };
  }
}
