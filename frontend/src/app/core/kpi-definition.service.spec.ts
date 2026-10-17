import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { INITIAL_MOCK_KPIS, KpiDefinitionService } from './kpi-definition.service';

describe('KpiDefinitionService real API behavior', () => {
  let service: KpiDefinitionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(KpiDefinitionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('propagates unauthorized reads rather than showing mock KPI records', () => {
    let status: number | undefined;
    service.getKpis().subscribe({
      next: () => fail('Unauthorized reads must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http.expectOne(`${environment.apiUrl}/kpi-definitions`).flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });
    expect(status).toBe(401);
  });

  it('preserves a genuine empty result', () => {
    service.getKpis().subscribe((response) => expect(response.data.items).toEqual([]));
    http.expectOne(`${environment.apiUrl}/kpi-definitions`).flush({
      success: true,
      data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
    });
  });

  it('does not turn failed writes into mock success', () => {
    let status: number | undefined;
    service.createKpi(INITIAL_MOCK_KPIS[0]).subscribe({
      next: () => fail('Failed writes must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    const request = http.expectOne(`${environment.apiUrl}/kpi-definitions`);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 500, statusText: 'Server Error' });
    expect(status).toBe(500);
  });
});
