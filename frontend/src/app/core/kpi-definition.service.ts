import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
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

    return this.http.get<ApiResponse<PagedResult<KpiDefinition>>>(
      `${environment.apiUrl}/kpi-definitions`,
      { params },
    );
  }

  getGroups(): Observable<ApiResponse<KpiGroupOption[]>> {
    return this.http.get<ApiResponse<KpiGroupOption[]>>(
      `${environment.apiUrl}/kpi-definitions/groups`,
    );
  }

  getKpiById(id: string): Observable<ApiResponse<KpiDefinition>> {
    return this.http.get<ApiResponse<KpiDefinition>>(`${environment.apiUrl}/kpi-definitions/${id}`);
  }

  createKpi(payload: KpiDefinitionSavePayload): Observable<ApiResponse<KpiDefinition>> {
    return this.http.post<ApiResponse<KpiDefinition>>(
      `${environment.apiUrl}/kpi-definitions`,
      payload,
    );
  }

  updateKpi(id: string, payload: KpiDefinitionSavePayload): Observable<ApiResponse<KpiDefinition>> {
    return this.http.put<ApiResponse<KpiDefinition>>(
      `${environment.apiUrl}/kpi-definitions/${id}`,
      payload,
    );
  }

  changeStatus(id: string, status: KpiStatus): Observable<ApiResponse<void>> {
    return this.http.patch<ApiResponse<void>>(
      `${environment.apiUrl}/kpi-definitions/${id}/status`,
      { status },
    );
  }
}
