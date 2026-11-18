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

  it('uploads the file to the preview endpoint with branchId and year', () => {
    const file = new File(['x'], 'kpi.xlsx');
    let result: unknown;
    service.importPreview('b1', 2026, file).subscribe((preview) => (result = preview));

    const request = http.expectOne((r) => r.url === `${url}/import/preview` && r.method === 'POST');
    expect(request.request.params.get('branchId')).toBe('b1');
    expect(request.request.params.get('year')).toBe('2026');
    expect(request.request.body instanceof FormData).toBeTrue();
    request.flush({ success: true, status: 200, data: { fileName: 'kpi.xlsx' } });

    expect(result).toEqual({ fileName: 'kpi.xlsx' } as never);
  });

  it('re-uploads the file to the commit endpoint with branchId and year', () => {
    const file = new File(['x'], 'kpi.xlsx');
    let result: unknown;
    service.importCommit('b1', 2026, file).subscribe((commitResult) => (result = commitResult));

    const request = http.expectOne((r) => r.url === `${url}/import/commit` && r.method === 'POST');
    expect(request.request.params.get('branchId')).toBe('b1');
    expect(request.request.params.get('year')).toBe('2026');
    expect(request.request.body instanceof FormData).toBeTrue();
    request.flush({ success: true, status: 201, data: { committed: 5, skipped: 1 } });

    expect(result).toEqual({ committed: 5, skipped: 1 } as never);
  });

  it('downloads the template for a given year', () => {
    service.downloadTemplate(2026).subscribe();

    const request = http.expectOne(
      (r) => r.url === `${url}/import/template` && r.params.get('year') === '2026',
    );
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob());
  });

  it('reads the import setup state for a branch and year', () => {
    let result: unknown;
    service.getSetup('b1', 2026).subscribe((setup) => (result = setup));

    const request = http.expectOne((r) => r.url === `${url}/setup`);
    expect(request.request.params.get('branchId')).toBe('b1');
    expect(request.request.params.get('year')).toBe('2026');
    request.flush({ success: true, status: 200, data: { hasBranchYear: false, step: 'IMPORT' } });

    expect(result).toEqual({ hasBranchYear: false, step: 'IMPORT' } as never);
  });

  it('reads the quarter split of a branch year', () => {
    let result: unknown;
    service.getQuarterSplit('by1').subscribe((split) => (result = split));

    const request = http.expectOne(
      (r) => r.url === `${url}/by1/quarter-split` && r.method === 'GET',
    );
    request.flush({ success: true, status: 200, data: { branchYearId: 'by1', year: 2026 } });

    expect(result).toEqual({ branchYearId: 'by1', year: 2026 } as never);
  });

  it('saves the quarter split of a branch year', () => {
    const body = { rows: [{ kpiId: 'k1', q1: 1, q2: 2, q3: 3, q4: 4 }] };
    let result: unknown;
    service.saveQuarterSplit('by1', body).subscribe((split) => (result = split));

    const request = http.expectOne(
      (r) => r.url === `${url}/by1/quarter-split` && r.method === 'PUT',
    );
    expect(request.request.body).toEqual(body);
    request.flush({ success: true, status: 200, data: { branchYearId: 'by1' } });

    expect(result).toEqual({ branchYearId: 'by1' } as never);
  });

  it('reads the allocation of one KPI', () => {
    let result: unknown;
    service.getKpiAllocation('by1', 'k1').subscribe((allocation) => (result = allocation));

    const request = http.expectOne(
      (r) => r.url === `${url}/by1/kpis/k1/allocation` && r.method === 'GET',
    );
    request.flush({ success: true, status: 200, data: { kpiId: 'k1' } });

    expect(result).toEqual({ kpiId: 'k1' } as never);
  });

  it('saves the allocation of one KPI', () => {
    const body = {
      departments: [{ departmentId: 'd1', year: 10, q1: 1, q2: 2, q3: 3, q4: 4 }],
    };
    let result: unknown;
    service.saveKpiAllocation('by1', 'k1', body).subscribe((allocation) => (result = allocation));

    const request = http.expectOne(
      (r) => r.url === `${url}/by1/kpis/k1/allocation` && r.method === 'PUT',
    );
    expect(request.request.body).toEqual(body);
    request.flush({ success: true, status: 200, data: { kpiId: 'k1' } });

    expect(result).toEqual({ kpiId: 'k1' } as never);
  });

  it('publishes a branch year without a body', () => {
    let result: unknown;
    service.publish('by1').subscribe((published) => (result = published));

    const request = http.expectOne((r) => r.url === `${url}/by1/publish` && r.method === 'POST');
    expect(request.request.body).toBeNull();
    request.flush({ success: true, status: 200, data: { branchYearCount: 1 } });

    expect(result).toEqual({ branchYearCount: 1 } as never);
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
