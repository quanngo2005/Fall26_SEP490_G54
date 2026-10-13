import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuthResponse } from './auth.models';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;

  const refreshedSession: AuthResponse = {
    accessToken: 'rotated-access-token',
    refreshToken: 'rotated-refresh-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: {
      id: 'account-id',
      email: 'person@example.test',
      fullName: 'Example Person',
      roles: ['Manager'],
      permissions: ['work.read'],
    },
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(
      'g54.auth.session',
      JSON.stringify({
        ...refreshedSession,
        accessToken: 'expired-access-token',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    );
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('refreshes once after 401 and keeps the session when the retry fails for another reason', () => {
    client.get(`${environment.apiUrl}/resource`).subscribe({ error: () => undefined });

    const initial = http.expectOne(`${environment.apiUrl}/resource`);
    initial.flush(null, { status: 401, statusText: 'Unauthorized' });

    const refresh = http.expectOne(`${environment.apiUrl}/v1/auth/refresh`);
    expect(refresh.request.headers.has('Authorization')).toBeFalse();
    refresh.flush(refreshedSession);

    const retry = http.expectOne(`${environment.apiUrl}/resource`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer rotated-access-token');
    retry.flush(null, { status: 503, statusText: 'Service Unavailable' });

    expect(localStorage.getItem('g54.auth.session')).not.toBeNull();
  });

  it('does not attach the application token to third-party requests', () => {
    client.get('https://third-party.example.test/data').subscribe();

    const request = http.expectOne('https://third-party.example.test/data');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });
});
