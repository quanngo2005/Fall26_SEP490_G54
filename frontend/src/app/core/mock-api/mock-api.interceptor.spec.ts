import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { RolePermissionService } from '../role-permission.service';
import { mockApiInterceptor } from './mock-api.interceptor';

describe('mockApiInterceptor', () => {
  const original = { useMockApi: environment.useMockApi, apiUrl: environment.apiUrl };
  let http: HttpClient;
  let backend: HttpTestingController;
  let roles: RolePermissionService;

  beforeEach(() => {
    environment.apiUrl = 'http://localhost:5000';
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([mockApiInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    roles = TestBed.inject(RolePermissionService);
  });

  afterEach(() => {
    backend.verify();
    environment.useMockApi = original.useMockApi;
    environment.apiUrl = original.apiUrl;
  });

  it('serves registered routes from mock data when enabled', fakeAsync(() => {
    environment.useMockApi = 'true';
    let codes: string[] = [];
    roles.list().subscribe((list) => (codes = list.map((r) => r.code)));
    tick(500);
    expect(codes).toContain('SYSTEM_ADMIN');
    backend.expectNone(`${environment.apiUrl}/roles`);
  }));

  it('sends unregistered routes to the real API even when enabled', () => {
    environment.useMockApi = 'true';
    http.get(`${environment.apiUrl}/employees`).subscribe();
    const request = backend.expectOne(`${environment.apiUrl}/employees`);
    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('sends every request to the real API when disabled', () => {
    environment.useMockApi = 'false';
    roles.list().subscribe();
    const request = backend.expectOne(`${environment.apiUrl}/roles`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: [] });
  });

  it('rejects stale permission matrix versions with VERSION_CONFLICT', fakeAsync(() => {
    environment.useMockApi = 'true';
    let roleId = '';
    roles.list().subscribe((list) => (roleId = list[0].id));
    tick(500);
    let errorCode: string | undefined;
    roles.save(roleId, { version: 99, changes: [] }).subscribe({
      error: (error: { status: number; error: { errorCode: string } }) =>
        (errorCode = error.error.errorCode),
    });
    tick(500);
    expect(errorCode).toBe('VERSION_CONFLICT');
  }));

  it('applies changes and bumps the matrix version', fakeAsync(() => {
    environment.useMockApi = 'true';
    let roleId = '';
    roles.list().subscribe((list) => (roleId = list[1].id));
    tick(500);
    let version = 0;
    let scope: string | null | undefined;
    roles.matrix(roleId).subscribe((matrix) => (version = matrix.version));
    tick(500);
    roles
      .save(roleId, { version, changes: [{ resource: 'work', action: 'view', scope: 'Branch' }] })
      .subscribe((matrix) => {
        version = matrix.version;
        scope = matrix.modules
          .flatMap((m) => m.resources)
          .find((r) => r.resource === 'work')
          ?.cells.find((c) => c.action === 'view')?.scope;
      });
    tick(500);
    expect(version).toBe(2);
    expect(scope).toBe('Branch');
  }));
});
