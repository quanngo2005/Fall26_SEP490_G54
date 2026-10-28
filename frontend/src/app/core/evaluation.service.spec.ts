import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { DepartmentEmployeeEvaluation, EvaluationPeriodOption } from './evaluation.models';
import { EvaluationService } from './evaluation.service';

describe('EvaluationService', () => {
  let service: EvaluationService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/evaluations`;
  const period: EvaluationPeriodOption = {
    id: '5eed0000-0000-0000-000f-000000000003',
    code: 'Q-TEST',
    name: 'Test quarter',
    startDate: '2026-10-01',
    endDate: '2026-12-31',
    periodType: 'QUARTER',
    status: 'OPEN',
    isActive: true,
  };
  const row: DepartmentEmployeeEvaluation = {
    employeeId: '5eed0000-0000-0000-0007-000000000004',
    employeeCode: 'NV004',
    fullName: 'Lê Minh Châu',
    jobPositionTitle: 'Chuyên viên Kinh doanh',
    jobPositionId: 'p1',
    departmentId: 'd1',
    departmentName: 'Nhóm Kinh doanh 1',
    evaluationId: null,
    totalScore: null,
    evaluationStatus: 'NOT_STARTED',
    proposedTier: null,
    calculatedAt: null,
    quantitativeScore: null,
    qualitativeScore: null,
    hasOpenDispute: false,
    pendingSubmissionCount: 0,
    canEvaluate: true,
    updatedAt: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EvaluationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the evaluation periods', () => {
    service.periods().subscribe((periods) => expect(periods[0].code).toBe('Q-TEST'));
    const request = http.expectOne(`${url}/periods`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: [period] });
  });

  it('sends the period as a query parameter and omits an empty department', () => {
    service.departmentEmployees(period.id).subscribe((rows) => {
      expect(rows[0].evaluationStatus).toBe('NOT_STARTED');
      expect(rows[0].totalScore).toBeNull();
    });
    const request = http.expectOne((r) => r.url === `${url}/department-employees`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('periodId')).toBe(period.id);
    expect(request.request.params.has('departmentId')).toBeFalse();
    request.flush({ success: true, status: 200, data: [row] });
  });

  it('sends the department as a query parameter when one is given', () => {
    service.departmentEmployees(period.id, 'd1').subscribe();
    const request = http.expectOne((r) => r.url === `${url}/department-employees`);
    expect(request.request.params.get('departmentId')).toBe('d1');
    request.flush({ success: true, status: 200, data: [] });
  });

  it('preserves a genuine empty list', () => {
    service.departmentEmployees(period.id).subscribe((rows) => expect(rows).toEqual([]));
    http
      .expectOne((r) => r.url === `${url}/department-employees`)
      .flush({ success: true, status: 200, data: [] });
  });

  it('treats a failed or empty envelope as an error instead of sample data', () => {
    let failures = 0;
    service.periods().subscribe({ error: () => failures++ });
    http.expectOne(`${url}/periods`).flush({ success: false, status: 500 });
    service.departmentEmployees('x').subscribe({ error: () => failures++ });
    http
      .expectOne((r) => r.url === `${url}/department-employees`)
      .flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });

  it('propagates the HTTP status of a forbidden request', () => {
    let status: number | undefined;
    service.departmentEmployees(period.id).subscribe({
      next: () => fail('A 403 must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http
      .expectOne((r) => r.url === `${url}/department-employees`)
      .flush(
        { success: false, status: 403, errorCode: 'FORBIDDEN' },
        { status: 403, statusText: 'Forbidden' },
      );
    expect(status).toBe(403);
  });
});
