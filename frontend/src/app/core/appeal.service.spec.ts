import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AppealService } from './appeal.service';

describe('AppealService', () => {
  let service: AppealService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/appeals`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AppealService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists appeals with every filter as a query parameter and joins the statuses', () => {
    service
      .list({
        status: ['OPEN', 'UNDER_REVIEW'],
        type: 'SCORE',
        scope: 'TEAM',
        periodId: 'p1',
        jobPositionId: 'j1',
        page: 2,
        pageSize: 5,
      })
      .subscribe((page) => expect(page.totalCount).toBe(7));
    const request = http.expectOne((r) => r.url === base);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('status')).toBe('OPEN,UNDER_REVIEW');
    expect(request.request.params.get('type')).toBe('SCORE');
    expect(request.request.params.get('scope')).toBe('TEAM');
    expect(request.request.params.get('periodId')).toBe('p1');
    expect(request.request.params.get('jobPositionId')).toBe('j1');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('5');
    request.flush(envelope({ items: [], totalCount: 7, page: 2, pageSize: 5 }));
  });

  it('sends no query parameter for an empty filter, an empty status list or a blank value', () => {
    service.list({ status: [], type: undefined, periodId: '' }).subscribe();
    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.keys()).toEqual([]);
    request.flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
  });

  it('loads the detail of an encoded appeal id', () => {
    service.detail('a 1').subscribe((detail) => expect(detail.reason).toBe('r'));
    const request = http.expectOne(`${base}/a%201`);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ id: 'a 1', reason: 'r', evidences: [] }));
  });

  it('files an appeal for a submission as multipart', () => {
    const form = new FormData();
    form.append('reason', 'because');
    service.createForSubmission('s1', form).subscribe((detail) => expect(detail.id).toBe('a1'));
    const request = http.expectOne(`${environment.apiUrl}/submissions/s1/appeals`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe(form);
    request.flush(envelope({ id: 'a1' }));
  });

  it('withdraws an appeal', () => {
    service.withdraw('a1').subscribe((detail) => expect(detail.status).toBe('WITHDRAWN'));
    const request = http.expectOne(`${base}/a1/withdraw`);
    expect(request.request.method).toBe('POST');
    request.flush(envelope({ id: 'a1', status: 'WITHDRAWN' }));
  });

  it('posts a decision with its note', () => {
    service
      .decide('a1', { decision: 'UPHOLD', note: 'Giữ nguyên điểm' })
      .subscribe((result) => expect(result.decision).toBe('UPHOLD'));
    const request = http.expectOne(`${base}/a1/decision`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ decision: 'UPHOLD', note: 'Giữ nguyên điểm' });
    request.flush(envelope({ decision: 'UPHOLD', appeal: { id: 'a1' } }));
  });

  it('fetches evidence as a blob from the API origin so the bearer token is attached', () => {
    service.evidence('/evidence/e1/preview').subscribe((blob) => expect(blob.size).toBe(3));
    const request = http.expectOne(`${environment.apiUrl}/evidence/e1/preview`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['abc']));
  });

  it('throws when the envelope reports a failure', () => {
    let failure: unknown;
    service.detail('a1').subscribe({ error: (error: unknown) => (failure = error) });
    http.expectOne(`${base}/a1`).flush({ success: false, status: 200, data: null });
    expect(failure).toBeInstanceOf(Error);
  });
});
