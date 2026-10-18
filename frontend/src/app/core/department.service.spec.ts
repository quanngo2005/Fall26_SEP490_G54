import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { DepartmentService } from './department.service';

describe('DepartmentService', () => {
  let http: HttpTestingController;
  let service: DepartmentService;
  const url = `${environment.apiUrl}/departments`;
  const id = '5eed0000-0000-0000-0001-000000000002';
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(DepartmentService);
  });
  afterEach(() => http.verify());
  it('sends list filters and preserves empty data', () => {
    service.list({ keyword: 'Sales', levelType: 'DEPARTMENT', page: 2 }).subscribe((data) => {
      expect(data.totalCount).toBe(0);
      expect(data.items).toEqual([]);
    });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('Sales');
    expect(request.request.params.get('levelType')).toBe('DEPARTMENT');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({
      success: true,
      status: 200,
      data: { items: [], totalCount: 0, page: 2, pageSize: 20 },
    });
  });
  it('reads the overview period and leaves missing metrics null', () => {
    service
      .overview(id, 'period-id')
      .subscribe((data) => expect(data.performance.average).toBeNull());
    const request = http.expectOne((r) => r.url === `${url}/${id}/overview`);
    expect(request.request.params.get('periodId')).toBe('period-id');
    request.flush({ success: true, status: 200, data: { performance: { average: null } } });
  });
  it('sends member filters and reads detail using GUIDs', () => {
    service.members(id, { status: 'ON_LEAVE', pageSize: 10 }).subscribe();
    const request = http.expectOne((r) => r.url === `${url}/${id}/members`);
    expect(request.request.params.get('status')).toBe('ON_LEAVE');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 10 } });
    service.detail(id).subscribe();
    http.expectOne(`${url}/${id}`).flush({ success: true, data: { id } });
  });
  it('propagates HTTP failures and invalid envelopes instead of sample data', () => {
    service.overview(id).subscribe({
      next: () => fail('Unexpected success'),
      error: (error) => expect(error.status).toBe(403),
    });
    http.expectOne(`${url}/${id}/overview`).flush({}, { status: 403, statusText: 'Forbidden' });
    service.list().subscribe({
      next: () => fail('Unexpected fallback'),
      error: (error) => expect(error).toBeTruthy(),
    });
    http.expectOne(url).flush({ success: true, data: null });
  });
});
