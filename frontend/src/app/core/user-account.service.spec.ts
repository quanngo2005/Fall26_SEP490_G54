import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../environments/environment';
import { UserAccountService } from './user-account.service';

describe('UserAccountService', () => {
  let service: UserAccountService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/user-accounts`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(UserAccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only non-empty filters as query params', () => {
    let result: unknown;
    service
      .list({ keyword: 'nva', roleId: '', status: 'LOCKED', page: 2, pageSize: 20 })
      .subscribe((page) => (result = page));
    const req = http.expectOne((r) => r.url === url);
    expect(req.request.params.get('keyword')).toBe('nva');
    expect(req.request.params.get('status')).toBe('LOCKED');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.has('roleId')).toBe(false);
    const data = { items: [], totalCount: 0, page: 2, pageSize: 20 };
    req.flush({ success: true, status: 200, data });
    expect(result).toEqual(data);
  });

  it('POSTs the create payload and unwraps the temporary password', () => {
    const payload = {
      employeeId: 'e1',
      username: 'vtg.so',
      roleId: 'r1',
      mustChangePassword: true,
    };
    let password = '';
    service.create(payload).subscribe((result) => (password = result.temporaryPassword));
    const req = http.expectOne(url);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({ success: true, status: 201, data: { account: {}, temporaryPassword: 'Tmp#1' } });
    expect(password).toBe('Tmp#1');
  });

  it('encodes the id and PUTs the role', () => {
    service.update('a/b', { roleId: 'r2' }).subscribe();
    const req = http.expectOne(`${url}/a%2Fb`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ roleId: 'r2' });
    req.flush({ success: true, status: 200, data: { id: 'a/b' } });
  });

  it('errors when the envelope is unsuccessful', () => {
    let failed = false;
    service.summary().subscribe({ error: () => (failed = true) });
    http
      .expectOne(`${url}/summary`)
      .flush({ success: false, status: 200, data: null, message: 'no' });
    expect(failed).toBe(true);
  });
});
