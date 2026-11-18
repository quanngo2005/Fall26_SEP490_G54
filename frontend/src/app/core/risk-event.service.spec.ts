import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { RiskEventService } from './risk-event.service';

describe('RiskEventService', () => {
  let service: RiskEventService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/risk-events`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RiskEventService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with every filter as a query parameter and drops the blank ones', () => {
    service
      .list({
        periodId: 'p1',
        departmentId: 'd1',
        severity: 'HIGH',
        state: 'REVERSED',
        search: 'RSK-0045',
        page: 2,
        pageSize: 5,
      })
      .subscribe((list) => expect(list.counts.recorded).toBe(3));
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    for (const [key, value] of Object.entries({
      periodId: 'p1',
      departmentId: 'd1',
      severity: 'HIGH',
      state: 'REVERSED',
      search: 'RSK-0045',
      page: '2',
      pageSize: '5',
    })) {
      expect(request.request.params.get(key)).toBe(value);
    }
    request.flush(envelope({ items: [], counts: { recorded: 3, reversed: 0 } }));

    service.list({ search: '', severity: undefined }).subscribe();
    const blank = http.expectOne((r) => r.url === url);
    expect(blank.request.params.keys()).toEqual([]);
    blank.flush(envelope({}));
  });

  it('loads the details of an encoded event', () => {
    service.details('a b').subscribe((details) => expect(details.description).toBe('d'));
    const request = http.expectOne(`${url}/a%20b/details`);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ description: 'd' }));
  });

  it('records an event with a single POST', () => {
    const body = {
      employeeId: 'e1',
      eventType: 'KYC_VIOLATION',
      description: 'Mô tả sự kiện',
      severity: 'MEDIUM' as const,
      occurredAt: '2026-10-22T00:00:00+07:00',
      penaltyPoints: 3,
      workCode: 'WK-1051',
      record: true as const,
    };
    service.create(body).subscribe((created) => expect(created.code).toBe('RSK-0045'));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush(envelope({ id: 'r1', code: 'RSK-0045' }));
  });

  it('uploads the files as multipart under "files"', () => {
    const a = new File(['a'], 'a.pdf', { type: 'application/pdf' });
    const b = new File(['b'], 'b.png', { type: 'image/png' });
    service.uploadEvidence('r1', [a, b]).subscribe((files) => expect(files.length).toBe(2));
    const request = http.expectOne(`${url}/r1/evidence`);
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.getAll('files').map((f) => (f as File).name)).toEqual(['a.pdf', 'b.png']);
    request.flush(envelope([{}, {}]));
  });

  it('reverses with the trimmed reason', () => {
    service.reverse('r1', '  Ghi nhầm nhân viên.  ').subscribe();
    const request = http.expectOne(`${url}/r1/reverse`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Ghi nhầm nhân viên.' });
    request.flush(envelope({}));
  });

  it('fetches an evidence preview as a blob', () => {
    service.evidence('e 1').subscribe((blob) => expect(blob.size).toBe(3));
    const request = http.expectOne(`${environment.apiUrl}/evidence/e%201/preview`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['abc']));
  });
});
