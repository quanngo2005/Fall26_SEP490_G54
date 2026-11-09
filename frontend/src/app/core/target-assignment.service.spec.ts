import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { TargetAssignmentService } from './target-assignment.service';

describe('TargetAssignmentService', () => {
  let service: TargetAssignmentService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/target-assignments`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TargetAssignmentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists assignments sending only the filters that are set', () => {
    let result: unknown;
    service
      .list({ evaluationPeriodId: 'p1', status: 'ACTIVE' })
      .subscribe((rows) => (result = rows));

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.get('evaluationPeriodId')).toBe('p1');
    expect(request.request.params.get('status')).toBe('ACTIVE');
    expect(request.request.params.has('departmentId')).toBeFalse();
    request.flush({ success: true, status: 200, data: [{ id: 'a1' }] });

    expect(result).toEqual([{ id: 'a1' }] as never);
  });

  it('reads the feedback history of an assignment', () => {
    let result: unknown;
    service.feedback('a 1').subscribe((rows) => (result = rows));

    http
      .expectOne(`${url}/a%201/feedback`)
      .flush({ success: true, status: 200, data: [{ id: 'f1', type: 'COMMENT' }] });

    expect(result).toEqual([{ id: 'f1', type: 'COMMENT' }] as never);
  });

  it('rejects an unsuccessful envelope', () => {
    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });

    http.expectOne(url).flush({ success: false, status: 200, data: null });

    expect(failed).toBeTrue();
  });

  it('posts the allocations of a target and returns the created rows', () => {
    let result: unknown;
    service
      .allocate('a 1', { allocations: [{ employeeId: 'e1', targetValue: 10 }] })
      .subscribe((rows) => (result = rows));

    const request = http.expectOne(`${url}/a%201/allocations`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ allocations: [{ employeeId: 'e1', targetValue: 10 }] });
    request.flush({ success: true, status: 201, data: [{ id: 'x' }] });

    expect(result).toEqual([{ id: 'x' }]);
  });

  it('loads the monthly breakdown of a target', () => {
    let totalValue = 0;
    service.breakdown('a1').subscribe((data) => (totalValue = data.totalValue));

    const request = http.expectOne((r) => r.url === `${url}/a1/breakdown`);
    expect(request.request.params.get('granularity')).toBe('MONTH');
    request.flush({ success: true, status: 200, data: { totalValue: 6400 } });

    expect(totalValue).toBe(6400);
  });

  it('puts the new values of a target and returns the new versions', () => {
    let result: unknown;
    service
      .reallocate('a1', {
        allocations: [{ employeeId: 'e1', targetValue: 20 }],
        changeReason: 'Góp ý',
      })
      .subscribe((rows) => (result = rows));

    const request = http.expectOne(`${url}/a1/allocations`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.changeReason).toBe('Góp ý');
    request.flush({ success: true, status: 200, data: [{ id: 'v2' }] });

    expect(result).toEqual([{ id: 'v2' }]);
  });
});
