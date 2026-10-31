import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuthResponse } from './auth.models';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let originalApiUrl: string;

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
    originalApiUrl = environment.apiUrl;
    environment.apiUrl = '';
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
    environment.apiUrl = originalApiUrl;
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('refreshes once after 401 and keeps the session when the retry fails for another reason', () => {
    client.get(`${environment.apiUrl}/employees`).subscribe({ error: () => undefined });

    const initial = http.expectOne(`${environment.apiUrl}/employees`);
    initial.flush(null, { status: 401, statusText: 'Unauthorized' });

    const refresh = http.expectOne(`${environment.apiUrl}/auth/refresh`);
    expect(refresh.request.headers.has('Authorization')).toBeFalse();
    refresh.flush(refreshedSession);

    const retry = http.expectOne(`${environment.apiUrl}/employees`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer rotated-access-token');
    retry.flush(null, { status: 503, statusText: 'Service Unavailable' });

    expect(localStorage.getItem('g54.auth.session')).not.toBeNull();
  });

  it('attaches the token to admin configuration and lookup endpoints', () => {
    localStorage.setItem('g54.auth.session', JSON.stringify(refreshedSession));
    for (const path of ['/sidebar-configurations', '/roles', '/job-positions']) {
      client.get(`${environment.apiUrl}${path}`).subscribe();
      const request = http.expectOne(`${environment.apiUrl}${path}`);
      expect(request.request.headers.get('Authorization')).toBe('Bearer rotated-access-token');
      request.flush({});
    }
  });

  it('does not attach the application token to third-party requests', () => {
    client.get('https://third-party.example.test/data').subscribe();

    const request = http.expectOne('https://third-party.example.test/data');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('does not attach the token to non-API paths on the API origin', () => {
    client.get(`${environment.apiUrl}/assets/config.json`).subscribe();
    const request = http.expectOne(`${environment.apiUrl}/assets/config.json`);
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('handles same-origin prefix-free API URLs without leaking tokens to assets', () => {
    localStorage.setItem('g54.auth.session', JSON.stringify(refreshedSession));
    const originalApiUrl = environment.apiUrl;
    environment.apiUrl = '';
    try {
      client.get('/employees').subscribe();
      const apiRequest = http.expectOne('/employees');
      expect(apiRequest.request.headers.get('Authorization')).toBe('Bearer rotated-access-token');
      apiRequest.flush([]);

      client.get('/assets/config.json').subscribe();
      const assetRequest = http.expectOne('/assets/config.json');
      expect(assetRequest.request.headers.has('Authorization')).toBeFalse();
      assetRequest.flush({});
    } finally {
      environment.apiUrl = originalApiUrl;
    }
  });
});
