import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import {
  DEFAULT_WARNING_DURATION_MS,
  IdleTimeoutService,
  SENSITIVE_IDLE_TIMEOUT_MS,
  STANDARD_IDLE_TIMEOUT_MS,
} from './idle-timeout.service';

describe('IdleTimeoutService', () => {
  let service: IdleTimeoutService;
  let authService: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;

  const mockUser = {
    id: 'user-001',
    email: 'officer@bank.vn',
    fullName: 'Nguyen Van Officer',
    roles: ['officer'],
    permissions: ['view'],
  };

  const mockAuthResponse = {
    accessToken: 'test-access-token',
    refreshToken: 'test-refresh-token',
    expiresAt: new Date(Date.now() + 3600 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7200 * 1000).toISOString(),
    user: mockUser,
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    sessionStorage.setItem('g54.auth.session', JSON.stringify(mockAuthResponse));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'home', children: [] },
          { path: 'review-verify', data: { sensitive: true }, children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(IdleTimeoutService);
    authService = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    service.destroy();
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('initializes with default standard 15m and sensitive 5m timeout constants', () => {
    expect(STANDARD_IDLE_TIMEOUT_MS).toBe(15 * 60 * 1000);
    expect(SENSITIVE_IDLE_TIMEOUT_MS).toBe(5 * 60 * 1000);
    expect(DEFAULT_WARNING_DURATION_MS).toBe(60 * 1000);
    expect(service.isLocked()).toBeFalse();
    expect(service.isWarning()).toBeFalse();
  });

  it('detects route sensitivity dynamically based on route metadata', async () => {
    service.init();
    expect(service.isSensitive()).toBeFalse();

    await router.navigate(['/review-verify']);
    expect(service.isSensitive()).toBeTrue();

    await router.navigate(['/home']);
    expect(service.isSensitive()).toBeFalse();
  });

  it('does not lock if user is not authenticated', () => {
    authService.clearSession();
    service.init();
    service.configureTimeouts(100, 50, 20);

    const originalNow = Date.now;
    spyOn(Date, 'now').and.returnValue(originalNow() + 10000);

    service.checkStatus();
    expect(service.isLocked()).toBeFalse();
    expect(service.isWarning()).toBeFalse();
  });

  it('triggers warning when remaining time is below warning threshold', () => {
    service.init();
    service.configureTimeouts(10000, 5000, 3000); // 10s timeout, 3s warning

    const baseTime = 1000000;
    const nowSpy = spyOn(Date, 'now').and.returnValue(baseTime);

    service.recordActivity();

    // Advance 8s (remaining 2s <= 3s warning threshold)
    nowSpy.and.returnValue(baseTime + 8000);
    service.checkStatus();

    expect(service.isWarning()).toBeTrue();
    expect(service.isLocked()).toBeFalse();
    expect(service.remainingSeconds()).toBe(2);
  });

  it('locks the session when idle timeout expires', () => {
    service.init();
    service.configureTimeouts(10000, 5000, 3000);

    const baseTime = 1000000;
    const nowSpy = spyOn(Date, 'now').and.returnValue(baseTime);

    service.recordActivity();

    // Advance 11s (timeout exceeded)
    nowSpy.and.returnValue(baseTime + 11000);
    service.checkStatus();

    expect(service.isLocked()).toBeTrue();
    expect(service.isWarning()).toBeFalse();
    expect(localStorage.getItem('g54.auth.is_locked')).toBe('true');
  });

  it('enforces 5-minute timeout on sensitive routes', async () => {
    service.init();
    service.configureTimeouts(15000, 5000, 1000); // 15s standard, 5s sensitive

    await router.navigate(['/review-verify']);
    expect(service.isSensitive()).toBeTrue();

    const baseTime = 1000000;
    const nowSpy = spyOn(Date, 'now').and.returnValue(baseTime);

    service.recordActivity();

    // Advance 6s (exceeds sensitive 5s, but less than standard 15s)
    nowSpy.and.returnValue(baseTime + 6000);
    service.checkStatus();

    expect(service.isLocked()).toBeTrue();
  });

  it('resets warning when user records activity or clicks stayLoggedIn', () => {
    service.init();
    service.configureTimeouts(10000, 5000, 3000);

    const baseTime = 1000000;
    const nowSpy = spyOn(Date, 'now').and.returnValue(baseTime);

    service.recordActivity();
    nowSpy.and.returnValue(baseTime + 8000);
    service.checkStatus();
    expect(service.isWarning()).toBeTrue();

    // Stay logged in resets warning
    nowSpy.and.returnValue(baseTime + 8500);
    service.stayLoggedIn();
    expect(service.isWarning()).toBeFalse();
  });

  it('unlocks session when correct credentials are provided', () => {
    service.init();
    service.lock();
    expect(service.isLocked()).toBeTrue();

    let unlocked = false;
    service.unlock('P@ssword123').subscribe((result) => {
      unlocked = result;
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      email: mockUser.email,
      password: 'P@ssword123',
      rememberMe: false,
    });
    req.flush(mockAuthResponse);

    expect(unlocked).toBeTrue();
    expect(service.isLocked()).toBeFalse();
    expect(localStorage.getItem('g54.auth.is_locked')).toBeNull();
  });

  it('fails to unlock and remains locked when incorrect credentials are provided', () => {
    service.init();
    service.lock();
    expect(service.isLocked()).toBeTrue();

    let errorThrown = false;
    service.unlock('WrongPassword').subscribe({
      next: () => fail('Should have failed'),
      error: () => {
        errorThrown = true;
      },
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
    req.flush({ title: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(errorThrown).toBeTrue();
    expect(service.isLocked()).toBeTrue();
  });

  it('synchronizes lock state across browser tabs via storage events', () => {
    service.init();

    // Another tab locked the session
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'g54.auth.is_locked',
        newValue: 'true',
      }),
    );
    expect(service.isLocked()).toBeTrue();

    // Another tab unlocked the session
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'g54.auth.is_locked',
        newValue: null,
      }),
    );
    expect(service.isLocked()).toBeFalse();
  });
});
