import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { QaDecision } from './qa-queue.models';
import { QaQueueService } from './qa-queue.service';

describe('QaQueueService', () => {
  let service: QaQueueService;
  let http: HttpTestingController;
  const api = environment.apiUrl;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });
  const decision = (over: Partial<QaDecision> = {}): QaDecision => ({
    verdict: 'CONFIRM',
    comment: '',
    severity: 'MEDIUM',
    requestRework: true,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QaQueueService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the queue with every filter as a query parameter and drops the blank ones', () => {
    service
      .list({
        tab: 'REJECTED',
        kind: 'SAMPLE',
        departmentId: 'd1',
        periodId: 'p1',
        search: 'QA-0331',
        page: 2,
        pageSize: 5,
      })
      .subscribe((queue) => expect(queue.counts.pending).toBe(3));
    const request = http.expectOne((r) => r.url === `${api}/qa-queue`);
    expect(request.request.method).toBe('GET');
    for (const [key, value] of Object.entries({
      tab: 'REJECTED',
      kind: 'SAMPLE',
      departmentId: 'd1',
      periodId: 'p1',
      search: 'QA-0331',
      page: '2',
      pageSize: '5',
    })) {
      expect(request.request.params.get(key)).toBe(value);
    }
    request.flush(
      envelope({
        items: [],
        totalCount: 0,
        page: 2,
        pageSize: 5,
        counts: { pending: 3, confirmed: 0, rejected: 0 },
        departments: [],
      }),
    );

    service.list({ search: '', kind: undefined }).subscribe();
    const blank = http.expectOne((r) => r.url === `${api}/qa-queue`);
    expect(blank.request.params.keys()).toEqual([]);
    blank.flush(envelope({}));
  });

  it('loads the detail of an encoded item', () => {
    service.detail('FLAG', 'a b').subscribe((detail) => expect(detail.reason).toBe('r'));
    const request = http.expectOne(`${api}/qa-queue/FLAG/a%20b`);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ reason: 'r' }));
  });

  it('passes a sample: confirm sends PASSED with an optional comment', () => {
    service.decide('SAMPLE', 's1', decision({ comment: '  tốt  ' })).subscribe();
    const request = http.expectOne(`${api}/qa-samples/s1/decision`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      result: 'PASSED',
      comment: 'tốt',
      requestRework: false,
    });
    request.flush(envelope({}));

    service.decide('SAMPLE', 's1', decision()).subscribe();
    const empty = http.expectOne(`${api}/qa-samples/s1/decision`);
    expect(empty.request.body.comment).toBeNull();
    empty.flush(envelope({}));
  });

  it('fails a sample: reject sends FAILED with the severity and the rework choice', () => {
    service
      .decide(
        'SAMPLE',
        's1',
        decision({ verdict: 'REJECT', comment: 'Chứng từ không hợp lệ.', severity: 'HIGH' }),
      )
      .subscribe();
    const request = http.expectOne(`${api}/qa-samples/s1/decision`);
    expect(request.request.body).toEqual({
      result: 'FAILED',
      comment: 'Chứng từ không hợp lệ.',
      severity: 'HIGH',
      requestRework: true,
    });
    request.flush(envelope({}));
  });

  it('dismisses a flag on confirm and requests rework on reject', () => {
    service.decide('FLAG', 'f1', decision()).subscribe();
    const dismiss = http.expectOne(`${api}/quality-flags/f1/dismiss`);
    expect(dismiss.request.method).toBe('POST');
    dismiss.flush(envelope({}));

    service
      .decide('FLAG', 'f1', decision({ verdict: 'REJECT', comment: ' Làm lại báo cáo. ' }))
      .subscribe();
    const rework = http.expectOne(`${api}/quality-flags/f1/request-rework`);
    expect(rework.request.body).toEqual({ comment: 'Làm lại báo cáo.' });
    rework.flush(envelope({}));
  });

  it('fetches an evidence preview as a blob', () => {
    service.evidence('e 1').subscribe((blob) => expect(blob.size).toBe(3));
    const request = http.expectOne(`${api}/evidence/e%201/preview`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['abc']));
  });
});
