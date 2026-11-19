import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { QaDashboard, QaDashboardFilterOptions } from './qa-dashboard.models';
import { QaDashboardService } from './qa-dashboard.service';

describe('QaDashboardService', () => {
  let service: QaDashboardService;
  let http: HttpTestingController;
  const api = environment.apiUrl;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QaDashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the dashboard with the chosen filters and drops blank ones', () => {
    const data = { asOf: '2026-10-26T08:15:02Z' } as QaDashboard;
    let result: QaDashboard | undefined;
    service
      .get({ periodId: 'p1', departmentId: null, jobPositionId: '', jobPositionCode: undefined })
      .subscribe((value) => (result = value));

    const req = http.expectOne((r) => r.url === `${api}/qa-dashboard`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual(['periodId']);
    expect(req.request.params.get('periodId')).toBe('p1');
    req.flush(envelope(data));
    expect(result).toEqual(data);
  });

  it('loads the dashboard without parameters when no filter is given', () => {
    service.get().subscribe();
    const req = http.expectOne(`${api}/qa-dashboard`);
    expect(req.request.params.keys()).toEqual([]);
    req.flush(envelope({}));
  });

  it('loads the filter options and unwraps the envelope', () => {
    const options: QaDashboardFilterOptions = {
      periods: [{ id: 'p1', code: 'PER-2026-10', name: '10/2026', isCurrent: true }],
      departments: [{ id: 'd1', code: 'D01', name: 'CSKH' }],
      jobPositions: [{ id: 'j1', code: 'TELLER', title: 'Teller' }],
    };
    let result: QaDashboardFilterOptions | undefined;
    service.filters().subscribe((value) => (result = value));
    const req = http.expectOne(`${api}/qa-dashboard/filters`);
    expect(req.request.method).toBe('GET');
    req.flush(envelope(options));
    expect(result).toEqual(options);
  });

  it('surfaces API failures instead of hiding them', () => {
    let failed = false;
    service.get().subscribe({ error: () => (failed = true) });
    http
      .expectOne(`${api}/qa-dashboard`)
      .flush({ success: false, status: 500 }, { status: 500, statusText: 'Server Error' });
    expect(failed).toBeTrue();
  });
});
