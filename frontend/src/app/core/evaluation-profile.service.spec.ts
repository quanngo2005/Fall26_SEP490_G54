import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { EvaluationProfileService } from './evaluation-profile.service';

describe('EvaluationProfileService', () => {
  let service: EvaluationProfileService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EvaluationProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filters that have a value and unwraps the paged data', () => {
    let items = -1;
    service
      .list({ keyword: 'teller', status: 'ACTIVE', jobPositionId: '', page: 1, pageSize: 100 })
      .subscribe((data) => (items = data.items.length));

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/evaluation-profiles`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('keyword')).toBe('teller');
    expect(req.request.params.get('status')).toBe('ACTIVE');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('100');
    expect(req.request.params.has('jobPositionId')).toBeFalse();
    req.flush({ success: true, data: { items: [{}], totalCount: 1, page: 1, pageSize: 100 } });

    expect(items).toBe(1);
  });

  it('rejects an envelope without data instead of returning an empty list', () => {
    let error: Error | undefined;
    service.list().subscribe({ error: (e: Error) => (error = e) });

    http.expectOne(`${environment.apiUrl}/evaluation-profiles`).flush({ success: true });

    expect(error?.message).toBe('Invalid evaluation profile response');
  });
});
