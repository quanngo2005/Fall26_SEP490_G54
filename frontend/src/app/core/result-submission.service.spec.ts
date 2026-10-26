import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ResultSubmissionService } from './result-submission.service';
import { SubmissionDetail } from './result-submission.models';

describe('ResultSubmissionService', () => {
  let service: ResultSubmissionService;
  let http: HttpTestingController;
  const submission: SubmissionDetail = {
    id: 's1',
    workId: 'w1',
    workCode: 'WRK-1',
    workTitle: 'Quarterly report',
    submitterEmployeeId: 'e1',
    submitterEmployeeName: 'Test User',
    submitterEmployeeCode: 'EMP-1',
    submissionNo: 1,
    status: 'SUBMITTED',
    comment: null,
    submittedAt: '2026-10-01T00:00:00+00:00',
    updatedAt: '2026-10-01T00:00:00+00:00',
    evidences: [],
    metrics: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ResultSubmissionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads one submission by an encoded id and unwraps the envelope', () => {
    service.getSubmission('a b').subscribe((item) => expect(item.status).toBe('SUBMITTED'));
    const request = http.expectOne(`${environment.apiUrl}/submissions/a%20b`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: submission });
  });

  it('lists submissions for a work item', () => {
    service.getSubmissionsByWork('w 1').subscribe((items) => expect(items.length).toBe(1));
    const request = http.expectOne(`${environment.apiUrl}/works/w%201/submissions`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: [submission] });
  });

  it('loads the submission context for a work item', () => {
    const context = {
      workId: 'w1',
      workCode: 'WRK-1',
      workTitle: 'Quarterly report',
      status: 'OPEN',
      allowResubmission: true,
      assignedEmployeeId: 'e1',
      assignedEmployeeName: 'Test User',
      kpis: [],
    };
    service
      .getSubmissionContext('w1')
      .subscribe((value) => expect(value.allowResubmission).toBeTrue());
    const request = http.expectOne(`${environment.apiUrl}/works/w1/submission-context`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: context });
  });

  it('posts the multipart form to the work submissions endpoint', () => {
    const formData = new FormData();
    formData.append('comment', 'done');
    service
      .createSubmission('w1', formData)
      .subscribe((result) => expect(result.submissionId).toBe('s1'));
    const request = http.expectOne(`${environment.apiUrl}/works/w1/submissions`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe(formData);
    request.flush({
      success: true,
      status: 200,
      data: { submissionId: 's1', workId: 'w1', status: 'SUBMITTED' },
    });
  });

  it('withdraws a submission with an empty body', () => {
    service.withdraw('s1').subscribe((result) => expect(result.status).toBe('WITHDRAWN'));
    const request = http.expectOne(`${environment.apiUrl}/submissions/s1/withdraw`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    request.flush({
      success: true,
      status: 200,
      data: { submissionId: 's1', workId: 'w1', status: 'WITHDRAWN' },
    });
  });

  it('treats a failed or empty envelope as an error', () => {
    let failures = 0;
    service.getSubmission('x').subscribe({
      error: (err: Error) => {
        expect(err.message).toBe('Server says no');
        failures++;
      },
    });
    http
      .expectOne(`${environment.apiUrl}/submissions/x`)
      .flush({ success: false, status: 404, message: 'Server says no' });
    service.getSubmissionsByWork('w1').subscribe({ error: () => failures++ });
    http
      .expectOne(`${environment.apiUrl}/works/w1/submissions`)
      .flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });
});
