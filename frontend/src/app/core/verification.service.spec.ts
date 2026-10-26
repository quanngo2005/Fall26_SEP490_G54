import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { VerificationDecisionPayload } from './verification.models';
import { VerificationService } from './verification.service';

describe('VerificationService', () => {
  let service: VerificationService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/verifications`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(VerificationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the dossier for an encoded submission id', () => {
    service.dossier('s 1').subscribe((dossier) => expect(dossier.submissionCode).toBe('SUB-1'));
    const request = http.expectOne(`${base}/s%201/dossier`);
    expect(request.request.method).toBe('GET');
    request.flush({
      success: true,
      status: 200,
      data: { submissionId: 's 1', submissionCode: 'SUB-1', evidenceFiles: [] },
    });
  });

  it('loads the verification history', () => {
    service.history('s1').subscribe((items) => expect(items[0].roundNo).toBe(1));
    const request = http.expectOne(`${base}/s1/history`);
    expect(request.request.method).toBe('GET');
    request.flush({
      success: true,
      status: 200,
      data: [
        {
          verificationId: 'v1',
          resultSubmissionId: 's1',
          roundNo: 1,
          decision: 'PENDING',
          comment: null,
          verifierEmployeeId: 'e1',
          verifierFullName: 'Verifier',
          verifiedAt: null,
        },
      ],
    });
  });

  it('posts the decision payload unchanged', () => {
    const payload: VerificationDecisionPayload = {
      decision: 'APPROVED',
      verifiedValue: 42,
      feedback: null,
      resubmitDeadline: null,
      checklistResults: [{ standardId: 'c1', isPassed: true }],
    };
    service
      .submitDecision('s1', payload)
      .subscribe((result) => expect(result.submissionStatus).toBe('APPROVED'));
    const request = http.expectOne(`${base}/s1/decision`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({
      success: true,
      status: 200,
      data: {
        verificationId: 'v1',
        decision: 'APPROVED',
        submissionStatus: 'APPROVED',
        workStatus: 'COMPLETED',
        verifiedAt: '2026-10-02T00:00:00+00:00',
        message: 'ok',
      },
    });
  });

  it('treats a failed or empty envelope as an error', () => {
    let failures = 0;
    service.dossier('x').subscribe({ error: () => failures++ });
    http.expectOne(`${base}/x/dossier`).flush({ success: false, status: 404 });
    service.history('x').subscribe({ error: () => failures++ });
    http.expectOne(`${base}/x/history`).flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });
});
