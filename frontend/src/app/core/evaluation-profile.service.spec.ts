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

  it('gets one profile by its encoded id and unwraps the data', () => {
    let name = '';
    service.detail('a/b').subscribe((data) => (name = data.name));

    const req = http.expectOne(`${environment.apiUrl}/evaluation-profiles/a%2Fb`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: { name: 'Teller profile' } });

    expect(name).toBe('Teller profile');
  });

  it('posts a new profile to the collection and puts a changed one to its id', () => {
    const body = {
      name: 'Teller',
      description: null,
      jobPositionId: 'j1',
      effectiveFrom: '2026-10-01',
      effectiveTo: null,
      items: [],
    };
    service.create({ ...body, code: 'EP-TELLER' }).subscribe();
    service.update('p1', body).subscribe();

    const create = http.expectOne(`${environment.apiUrl}/evaluation-profiles`);
    expect(create.request.method).toBe('POST');
    expect(create.request.body.code).toBe('EP-TELLER');
    create.flush({ success: true, data: {} });

    const update = http.expectOne(`${environment.apiUrl}/evaluation-profiles/p1`);
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual(body);
    update.flush({ success: true, data: {} });
  });

  it('approves with an empty body and rejects with the reason', () => {
    service.approve('p1').subscribe();
    service.reject('p2', 'Weights wrong').subscribe();

    const approve = http.expectOne(`${environment.apiUrl}/evaluation-profiles/p1/approve`);
    expect(approve.request.method).toBe('POST');
    approve.flush({ success: true });

    const reject = http.expectOne(`${environment.apiUrl}/evaluation-profiles/p2/reject`);
    expect(reject.request.body).toEqual({ reason: 'Weights wrong' });
    reject.flush({ success: true });
  });

  it('fails an approval whose envelope is not successful', () => {
    let error: Error | undefined;
    service.approve('p1').subscribe({ error: (e: Error) => (error = e) });

    http
      .expectOne(`${environment.apiUrl}/evaluation-profiles/p1/approve`)
      .flush({ success: false });

    expect(error?.message).toBe('Invalid evaluation profile response');
  });

  it('rejects an envelope without data instead of returning an empty list', () => {
    let error: Error | undefined;
    service.list().subscribe({ error: (e: Error) => (error = e) });

    http.expectOne(`${environment.apiUrl}/evaluation-profiles`).flush({ success: true });

    expect(error?.message).toBe('Invalid evaluation profile response');
  });
});
