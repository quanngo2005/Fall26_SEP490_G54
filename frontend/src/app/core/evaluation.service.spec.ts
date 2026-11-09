import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import {
  DepartmentEmployeeEvaluation,
  EvaluationFormModel,
  EvaluationPeriodOption,
  EvaluationResult,
  SubmitEvaluationRequest,
} from './evaluation.models';
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
    quantitativeScoreChange: null,
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

  it('loads the evaluation form with the employee and period as query parameters', () => {
    const form = { employee: { id: 'e1' }, canEdit: true } as unknown as EvaluationFormModel;
    service.form('e1', period.id).subscribe((result) => expect(result.canEdit).toBeTrue());
    const request = http.expectOne((r) => r.url === `${url}/form`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('employeeId')).toBe('e1');
    expect(request.request.params.get('periodId')).toBe(period.id);
    request.flush({ success: true, status: 200, data: form });
  });

  it('treats an empty form envelope as an error instead of sample data', () => {
    let failed = false;
    service.form('e1', period.id).subscribe({ error: () => (failed = true) });
    http
      .expectOne((r) => r.url === `${url}/form`)
      .flush({ success: true, status: 200, data: null });
    expect(failed).toBeTrue();
  });

  it('propagates the HTTP status of a missing form', () => {
    let status: number | undefined;
    service.form('e1', period.id).subscribe({
      next: () => fail('A 404 must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http
      .expectOne((r) => r.url === `${url}/form`)
      .flush(
        { success: false, status: 404, errorCode: 'NOT_FOUND' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(status).toBe(404);
  });

  it('posts the evaluation to /submit with the draft flag and the qualitative scores', () => {
    const body: SubmitEvaluationRequest = {
      employeeId: 'e1',
      periodId: period.id,
      comment: 'Good',
      qualitativeScores: [{ evaluationProfileItemId: 'g1', score: 7 }],
      isDraft: true,
    };
    const result = { evaluationId: 'ev1', status: 'DRAFT' } as unknown as EvaluationResult;
    service.submit(body).subscribe((saved) => expect(saved.evaluationId).toBe('ev1'));
    const request = http.expectOne(`${url}/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush({ success: true, status: 200, data: result });
  });

  it('propagates a conflict from /submit', () => {
    let status: number | undefined;
    service
      .submit({
        employeeId: 'e1',
        periodId: period.id,
        comment: null,
        qualitativeScores: [],
        isDraft: false,
      })
      .subscribe({
        next: () => fail('A 409 must not succeed'),
        error: (error: HttpErrorResponse) => (status = error.status),
      });
    http
      .expectOne(`${url}/submit`)
      .flush(
        { success: false, status: 409, errorCode: 'CONFLICT' },
        { status: 409, statusText: 'Conflict' },
      );
    expect(status).toBe(409);
  });

  it('loads branch evaluation periods with optional query filters', () => {
    service.branchPeriods(2026, 'QUARTER', 'IN_REVIEW').subscribe((overview) => {
      expect(overview.branchName).toBe('Chi nhánh Hà Đông');
      expect(overview.periods.length).toBe(1);
      expect(overview.availableYears).toEqual([2026]);
      expect(overview.periods[0].qualityRatio).toBeNull();
    });

    const request = http.expectOne((r) => r.url === `${url}/branch-periods`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('year')).toBe('2026');
    expect(request.request.params.get('periodType')).toBe('QUARTER');
    expect(request.request.params.get('status')).toBe('IN_REVIEW');

    request.flush({
      success: true,
      status: 200,
      data: {
        branchName: 'Chi nhánh Hà Đông',
        branchCode: 'BR-HADONG',
        availableYears: [2026],
        periods: [
          {
            id: 'p1',
            code: 'Q3-2026',
            name: 'Quý 3/2026',
            startDate: '2026-07-01',
            endDate: '2026-09-30',
            periodType: 'QUARTER',
            status: 'IN_REVIEW',
            displayStatus: 'IN_REVIEW',
            qualityRatio: null,
            submittedDepartmentCount: 6,
            approvedDepartmentCount: 4,
            totalDepartmentCount: 8,
            appealWindow: null,
            canOpen: true,
            canLock: false,
            canViewHistory: false,
          },
        ],
      },
    });
  });

  it('submits period finalization to /approvals/finalize', () => {
    const finalizeBody = {
      periodId: 'p1',
      signatureToken: 'TOKEN-123',
      approvalComments: 'Approved',
    };
    service.finalize(finalizeBody).subscribe((res) => {
      expect(res.periodStatus).toBe('LOCKED');
      expect(res.totalEvaluationsFinalized).toBe(92);
    });

    const request = http.expectOne(`${url}/approvals/finalize`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(finalizeBody);

    request.flush({
      success: true,
      status: 200,
      data: {
        periodId: 'p1',
        periodStatus: 'LOCKED',
        finalizedAt: '2026-10-15T10:00:00Z',
        totalEvaluationsFinalized: 92,
        message: 'Locked',
      },
    });
  });
});
