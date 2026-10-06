import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuthResponse } from './auth.models';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let authService: AuthService;
  let http: HttpTestingController;

  const response: AuthResponse = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
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
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });
    authService = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('stores a remembered login in local storage', () => {
    authService
      .login({ email: 'person@example.test', password: 'password', rememberMe: true })
      .subscribe();

    const request = http.expectOne(`${environment.apiUrl}/v1/auth/login`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.rememberMe).toBeTrue();
    request.flush(response);

    expect(localStorage.getItem('g54.auth.session')).not.toBeNull();
    expect(sessionStorage.getItem('g54.auth.session')).toBeNull();
    expect(authService.currentUser()?.fullName).toBe('Example Person');
    expect(authService.isAuthenticated()).toBeTrue();
  });

  it('clears client authentication state only after logout succeeds', () => {
    localStorage.setItem('g54.auth.session', JSON.stringify(response));
    authService.logout().subscribe();

    const request = http.expectOne(`${environment.apiUrl}/v1/auth/logout`);
    expect(request.request.body.refreshToken).toBe('refresh-token');
    request.flush({ message: 'Logout successful' });

    expect(localStorage.getItem('g54.auth.session')).toBeNull();
    expect(authService.currentUser()).toBeNull();
  });

  it('rotates expired access sessions and persists the new tokens in the same storage', () => {
    const expiredResponse: AuthResponse = {
      ...response,
      expiresAt: new Date(Date.now() - 60_000).toISOString(),
    };
    localStorage.setItem('g54.auth.session', JSON.stringify(expiredResponse));

    authService.ensureAuthenticated().subscribe((authenticated) => {
      expect(authenticated).toBeTrue();
    });

    const request = http.expectOne(`${environment.apiUrl}/v1/auth/refresh`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.refreshToken).toBe('refresh-token');
    request.flush(response);

    const stored = JSON.parse(localStorage.getItem('g54.auth.session') ?? '{}') as AuthResponse;
    expect(stored.accessToken).toBe('access-token');
    expect(sessionStorage.getItem('g54.auth.session')).toBeNull();
  });
});
