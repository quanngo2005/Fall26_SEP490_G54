import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { KpiDefinitionSavePayload } from './kpi-definition.models';
import { KpiDefinitionService } from './kpi-definition.service';

describe('KpiDefinitionService real API behavior', () => {
  let service: KpiDefinitionService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/kpi-definitions`;
  const raw = {
    id: 'k1',
    kpiGroupId: 'g1',
    kpiGroupName: 'Kinh doanh',
    code: 'HDV',
    name: 'Huy động vốn',
    description: null,
    formula: null,
    unit: 'VND',
    aggregationType: 'NO_AGGREGATION',
    direction: 'LOWER_BETTER',
    status: 'ACTIVE',
    maxAchievementPercent: null,
    updatedAt: '2026-10-14T08:30:00+00:00',
  };
  const payload: KpiDefinitionSavePayload = {
    kpiGroupId: 'g1',
    code: 'HDV',
    name: 'Huy động vốn',
    description: null,
    formula: null,
    unit: 'VND',
    aggregationType: 'RATIO',
    direction: 'LOWER_BETTER',
    maxAchievementPercent: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(KpiDefinitionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters as query parameters and reads UPPER_SNAKE enums as-is', () => {
    service.list({ keyword: 'hdv', kpiGroupId: 'g1', page: 2, pageSize: 20 }).subscribe((page) => {
      expect(page.items[0].direction).toBe('LOWER_BETTER');
      expect(page.items[0].aggregationType).toBe('NO_AGGREGATION');
      expect(page.items[0].status).toBe('ACTIVE');
    });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('hdv');
    expect(request.request.params.get('kpiGroupId')).toBe('g1');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({
      success: true,
      status: 200,
      data: { items: [raw], totalCount: 1, page: 2, pageSize: 20 },
    });
  });

  it('preserves a genuine empty result', () => {
    service.list().subscribe((page) => expect(page.items).toEqual([]));
    http
      .expectOne(url)
      .flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });

  it('propagates unauthorized reads rather than showing mock KPI records', () => {
    let status: number | undefined;
    service.list().subscribe({
      next: () => fail('Unauthorized reads must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http.expectOne(url).flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
  });

  it('treats a failed envelope as an error instead of returning its data', () => {
    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });
    http.expectOne(url).flush({ success: false, status: 400, errorCode: 'VALIDATION_ERROR' });
    expect(failed).toBeTrue();
  });

  it('writes UPPER_SNAKE enum strings on create and update, never ordinals', () => {
    service.create(payload).subscribe((created) => expect(created.direction).toBe('LOWER_BETTER'));
    const create = http.expectOne(url);
    expect(create.request.method).toBe('POST');
    expect(create.request.body.direction).toBe('LOWER_BETTER');
    expect(create.request.body.aggregationType).toBe('RATIO');
    create.flush({ success: true, data: raw });

    service.update('k1', payload).subscribe();
    const update = http.expectOne(`${url}/k1`);
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.direction).toBe('LOWER_BETTER');
    expect(update.request.body.aggregationType).toBe('RATIO');
    update.flush({ success: true, data: raw });
  });

  it('does not turn failed writes into mock success', () => {
    let status: number | undefined;
    service.create(payload).subscribe({
      next: () => fail('Failed writes must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http.expectOne(url).flush(null, { status: 500, statusText: 'Server Error' });
    expect(status).toBe(500);
  });

  it('reads the group lookup', () => {
    service.groups().subscribe((groups) => expect(groups.length).toBe(1));
    http
      .expectOne(`${url}/groups`)
      .flush({ success: true, data: [{ id: 'g1', code: 'BUS', name: 'Kinh doanh' }] });
  });
});
