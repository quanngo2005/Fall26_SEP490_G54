import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuditLogService } from './audit-log.service';

describe('AuditLogService', () => {
  let http: HttpTestingController;
  let service: AuditLogService;
  const url = `${environment.apiUrl}/audit-logs`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(AuditLogService);
  });
  afterEach(() => http.verify());

  it('sends only the filters that have a value and keeps an empty page', () => {
    service
      .list({ page: 2, pageSize: 20, action: 'Update', keyword: '', from: '2026-11-01T00:00:00Z' })
      .subscribe((result) => {
        expect(result.items).toEqual([]);
        expect(result.totalCount).toBe(0);
      });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('action')).toBe('Update');
    expect(request.request.params.get('from')).toBe('2026-11-01T00:00:00Z');
    expect(request.request.params.has('keyword')).toBeFalse();
    request.flush(envelope({ items: [], totalCount: 0, page: 2, pageSize: 20 }));
  });

  it('loads one record by id', () => {
    service.get('abc').subscribe((detail) => expect(detail.entityName).toBe('RiskEvent'));
    const request = http.expectOne(`${url}/abc`);
    request.flush(envelope({ id: 'abc', entityName: 'RiskEvent' }));
  });

  it('surfaces a failed request instead of faking success', () => {
    let failed = false;
    service.list({}).subscribe({ error: () => (failed = true) });
    http.expectOne((r) => r.url === url).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(failed).toBeTrue();
  });

  it('rejects an unsuccessful envelope', () => {
    let failed = false;
    service.get('x').subscribe({ error: () => (failed = true) });
    http.expectOne(`${url}/x`).flush({ success: false, status: 200, data: null });
    expect(failed).toBeTrue();
  });
});
