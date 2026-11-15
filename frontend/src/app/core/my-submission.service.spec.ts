import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { MySubmissionService } from './my-submission.service';

describe('MySubmissionService', () => {
  let service: MySubmissionService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/submissions`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MySubmissionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the caller submissions and sends only the filters that are set', () => {
    service
      .list({ matchStatus: 'NOT_MATCHED', periodId: 'p1', keyword: '', page: 2 })
      .subscribe((page) => expect(page.totalCount).toBe(9));
    const request = http.expectOne((r) => r.url === `${base}/mine`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('matchStatus')).toBe('NOT_MATCHED');
    expect(request.request.params.get('periodId')).toBe('p1');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('keyword')).toBe(false);
    request.flush(envelope({ items: [], totalCount: 9, page: 2, pageSize: 20 }));
  });

  it('reads the reconciliation of one submission', () => {
    service.detail('a/b').subscribe((detail) => expect(detail.submissionNo).toBe(2));
    const request = http.expectOne(`${base}/a%2Fb/reconciliation`);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ submissionNo: 2 }));
  });

  it('fetches an evidence file as a blob', () => {
    service.evidence('e1').subscribe((blob) => expect(blob.size).toBe(3));
    const request = http.expectOne(`${environment.apiUrl}/evidence/e1/preview`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['abc']));
  });

  it('turns an unsuccessful envelope into an error', () => {
    let failed = false;
    service.detail('s1').subscribe({ error: () => (failed = true) });
    http.expectOne(`${base}/s1/reconciliation`).flush({ success: false, status: 200, data: null });
    expect(failed).toBe(true);
  });
});
