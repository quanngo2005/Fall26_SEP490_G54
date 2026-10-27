import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { JobPositionService } from './job-position.service';

describe('JobPositionService', () => {
  let http: HttpTestingController;
  let service: JobPositionService;
  const url = `${environment.apiUrl}/job-positions`;
  const id = '5eed0000-0000-0000-0006-000000000002';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(JobPositionService);
  });

  afterEach(() => http.verify());

  it('sends every list filter as a query parameter and unwraps the paged data', () => {
    service
      .list({ keyword: 'TELLER', status: 'ACTIVE', page: 2, pageSize: 10 })
      .subscribe((data) => {
        expect(data.totalCount).toBe(1);
        expect(data.items[0].code).toBe('TELLER');
        expect(data.items[0].evaluationProfileName).toBeNull();
      });

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('TELLER');
    expect(request.request.params.get('status')).toBe('ACTIVE');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({
      success: true,
      status: 200,
      data: {
        items: [
          {
            id,
            code: 'TELLER',
            title: 'Teller',
            description: null,
            employeeCount: 3,
            evaluationProfileId: null,
            evaluationProfileName: null,
            workTypeCount: 0,
            kpiItems: [],
            lastSyncAt: '2026-10-24T08:15:00+07:00',
            status: 'ACTIVE',
          },
        ],
        totalCount: 1,
        page: 2,
        pageSize: 10,
      },
    });
  });

  it('omits empty and undefined filters instead of sending blank values', () => {
    service.list({ keyword: '', status: undefined }).subscribe();

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 50 } });
  });

  it('keeps a genuinely empty list empty rather than inventing rows', () => {
    let received: unknown;
    service.list().subscribe((data) => (received = data));

    http
      .expectOne((r) => r.url === url)
      .flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 50 } });

    expect(received).toEqual({ items: [], totalCount: 0, page: 1, pageSize: 50 });
  });

  it('reads one position by its GUID and keeps a missing profile null', () => {
    service.detail(id).subscribe((data) => {
      expect(data.code).toBe('TELLER');
      expect(data.evaluationProfile).toBeNull();
      expect(data.workTypes).toEqual([]);
    });

    const request = http.expectOne(`${url}/${id}`);
    expect(request.request.method).toBe('GET');
    request.flush({
      success: true,
      status: 200,
      data: {
        id,
        code: 'TELLER',
        title: 'Teller',
        description: null,
        employeeCount: 3,
        lastSyncAt: '2026-10-24T08:15:00+07:00',
        status: 'ACTIVE',
        evaluationProfile: null,
        workTypes: [],
      },
    });
  });

  it('encodes the identifier it puts in the path', () => {
    service.detail('a/b c').subscribe();

    http.expectOne(`${url}/a%2Fb%20c`).flush({ success: true, data: { id: 'x' } });
  });

  it('rejects an envelope that says it failed or carries no data', () => {
    const errors: string[] = [];
    service.detail(id).subscribe({ error: (e: Error) => errors.push(e.message) });
    http.expectOne(`${url}/${id}`).flush({ success: false, data: null, message: 'Not found' });
    service.list().subscribe({ error: (e: Error) => errors.push(e.message) });
    http.expectOne((r) => r.url === url).flush({ success: true, data: null });

    expect(errors).toEqual(['Invalid job position response', 'Invalid job position response']);
  });

  it('surfaces HTTP errors (403 for a forbidden role, 404, 500) without turning them into data', () => {
    const statuses: number[] = [];
    for (const status of [403, 404, 500]) {
      service.list().subscribe({ error: (e: { status: number }) => statuses.push(e.status) });
      http.expectOne((r) => r.url === url).flush({ success: false }, { status, statusText: 'x' });
    }

    expect(statuses).toEqual([403, 404, 500]);
  });
});
