import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { TargetAllocationService } from './target-allocation.service';

describe('TargetAllocationService', () => {
  let service: TargetAllocationService;
  let http: HttpTestingController;
  const assignments = `${environment.apiUrl}/target-assignments`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TargetAllocationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets the employee allocation of a department year', () => {
    let result: unknown;
    service.getEmployeeAllocation('d1').subscribe((value) => (result = value));
    const call = http.expectOne((r) => r.method === 'GET' && r.url.startsWith(assignments));
    expect(call.request.url).toContain('d1');
    call.flush({ success: true, data: { kpiId: 'k1' } });
    expect(result).toEqual({ kpiId: 'k1' });
  });

  it('publishes the employees of a department year', () => {
    service.publishEmployees('d1').subscribe();
    const call = http.expectOne((r) => r.method === 'POST' && r.url.startsWith(assignments));
    expect(call.request.url).toContain('d1');
    call.flush({ success: true, data: {} });
  });

  it('fails instead of faking success when the envelope has no data', () => {
    let failed = false;
    service.getEmployeeAllocation('d1').subscribe({ error: () => (failed = true) });
    http.expectOne((r) => r.url.startsWith(assignments)).flush({ success: true, data: null });
    expect(failed).toBeTrue();
  });
});
