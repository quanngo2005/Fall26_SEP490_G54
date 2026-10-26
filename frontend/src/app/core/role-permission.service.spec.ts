import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../environments/environment';
import { RolePermissionService } from './role-permission.service';

describe('RolePermissionService', () => {
  let service: RolePermissionService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/roles`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RolePermissionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('unwraps the role list', () => {
    let result: unknown;
    service.roles().subscribe((roles) => (result = roles));
    http.expectOne(url).flush({ success: true, status: 200, data: [{ id: 'r1' }] });
    expect(result).toEqual([{ id: 'r1' }]);
  });

  it('encodes the role id and PUTs changes', () => {
    const payload = { version: 1, changes: [] };
    service.save('a/b', payload).subscribe();
    const req = http.expectOne(`${url}/a%2Fb/permission-matrix`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush({ success: true, status: 200, data: { roleId: 'a/b', version: 2, modules: [] } });
  });

  it('errors when the envelope is unsuccessful', () => {
    let failed = false;
    service.matrix('r1').subscribe({ error: () => (failed = true) });
    http
      .expectOne(`${url}/r1/permission-matrix`)
      .flush({ success: false, status: 200, data: null, message: 'x' });
    expect(failed).toBeTrue();
  });
});
