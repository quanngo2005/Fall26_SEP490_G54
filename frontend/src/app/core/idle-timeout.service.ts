import { inject, Injectable, NgZone, signal } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter, map, Observable, tap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const STANDARD_IDLE_TIMEOUT_MS = 15 * 60 * 1000; // 15 minutes (CR-14)
export const SENSITIVE_IDLE_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes (NFR-06)
export const DEFAULT_WARNING_DURATION_MS = 60 * 1000; // 60 seconds

const STORAGE_LAST_ACTIVE_KEY = 'g54.auth.last_active_at';
const STORAGE_LOCKED_KEY = 'g54.auth.is_locked';

@Injectable({ providedIn: 'root' })
export class IdleTimeoutService {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly ngZone = inject(NgZone);

  private standardTimeoutMs = STANDARD_IDLE_TIMEOUT_MS;
  private sensitiveTimeoutMs = SENSITIVE_IDLE_TIMEOUT_MS;
  private warningDurationMs = DEFAULT_WARNING_DURATION_MS;

  private lastActivity = Date.now();
  private checkIntervalId: ReturnType<typeof setInterval> | null = null;
  private isListening = false;
  private lastThrottleRecord = 0;

  private readonly isLockedState = signal<boolean>(this.checkInitialLockState());
  private readonly isWarningState = signal<boolean>(false);
  private readonly remainingSecondsState = signal<number>(0);
  private readonly isSensitiveState = signal<boolean>(false);

  readonly isLocked = this.isLockedState.asReadonly();
  readonly isWarning = this.isWarningState.asReadonly();
  readonly remainingSeconds = this.remainingSecondsState.asReadonly();
  readonly isSensitive = this.isSensitiveState.asReadonly();
  readonly currentUser = this.authService.currentUser;

  private readonly activityListener = (): void => {
    this.recordActivity();
  };

  private readonly storageListener = (event: StorageEvent): void => {
    if (event.key === STORAGE_LAST_ACTIVE_KEY && event.newValue) {
      const timestamp = Number(event.newValue);
      if (!Number.isNaN(timestamp) && timestamp > this.lastActivity) {
        this.lastActivity = timestamp;
        if (this.isWarningState()) {
          this.isWarningState.set(false);
        }
      }
    } else if (event.key === STORAGE_LOCKED_KEY) {
      if (event.newValue === 'true' && !this.isLockedState()) {
        this.isLockedState.set(true);
        this.isWarningState.set(false);
      } else if (event.newValue === null && this.isLockedState()) {
        this.isLockedState.set(false);
        this.lastActivity = Date.now();
      }
    }
  };

  private readonly visibilityListener = (): void => {
    if (document.visibilityState === 'visible') {
      this.checkStatus();
    }
  };

  init(): void {
    if (this.isListening) {
      return;
    }

    this.isListening = true;
    this.isSensitiveState.set(this.checkRouteSensitivity());

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => {
        const isSensitive = this.checkRouteSensitivity();
        this.isSensitiveState.set(isSensitive);
        this.checkStatus();
      });

    this.ngZone.runOutsideAngular(() => {
      window.addEventListener('mousemove', this.activityListener, { passive: true });
      window.addEventListener('mousedown', this.activityListener, { passive: true });
      window.addEventListener('keydown', this.activityListener, { passive: true });
      window.addEventListener('touchstart', this.activityListener, { passive: true });
      window.addEventListener('scroll', this.activityListener, { passive: true });
      window.addEventListener('click', this.activityListener, { passive: true });

      window.addEventListener('storage', this.storageListener);
      document.addEventListener('visibilitychange', this.visibilityListener);

      this.checkIntervalId = setInterval(() => {
        this.checkStatus();
      }, 1000);
    });
  }

  destroy(): void {
    if (!this.isListening) {
      return;
    }

    this.isListening = false;
    window.removeEventListener('mousemove', this.activityListener);
    window.removeEventListener('mousedown', this.activityListener);
    window.removeEventListener('keydown', this.activityListener);
    window.removeEventListener('touchstart', this.activityListener);
    window.removeEventListener('scroll', this.activityListener);
    window.removeEventListener('click', this.activityListener);
    window.removeEventListener('storage', this.storageListener);
    document.removeEventListener('visibilitychange', this.visibilityListener);

    if (this.checkIntervalId !== null) {
      clearInterval(this.checkIntervalId);
      this.checkIntervalId = null;
    }
  }

  recordActivity(): void {
    const now = Date.now();
    if (now - this.lastThrottleRecord < 1000) {
      return;
    }
    this.lastThrottleRecord = now;
    this.lastActivity = now;

    try {
      localStorage.setItem(STORAGE_LAST_ACTIVE_KEY, now.toString());
    } catch {
      // Storage unavailable or disabled
    }

    if (this.isWarningState()) {
      this.ngZone.run(() => {
        this.isWarningState.set(false);
      });
    }
  }

  stayLoggedIn(): void {
    this.recordActivity();
    this.isWarningState.set(false);
  }

  lock(): void {
    this.ngZone.run(() => {
      this.isLockedState.set(true);
      this.isWarningState.set(false);
      try {
        localStorage.setItem(STORAGE_LOCKED_KEY, 'true');
      } catch {
        // Storage unavailable
      }
    });
  }

  unlock(password: string): Observable<boolean> {
    const user = this.authService.currentUser();
    if (!user || !user.email) {
      return throwError(() => new Error('No active user session to unlock.'));
    }

    const rememberMe = localStorage.getItem('g54.auth.session') !== null;
    return this.authService.login({ email: user.email, password, rememberMe }).pipe(
      tap(() => {
        this.ngZone.run(() => {
          this.isLockedState.set(false);
          this.isWarningState.set(false);
          try {
            localStorage.removeItem(STORAGE_LOCKED_KEY);
          } catch {
            // Storage unavailable
          }
          this.recordActivity();
        });
      }),
      map(() => true),
    );
  }

  logout(): void {
    this.isLockedState.set(false);
    this.isWarningState.set(false);
    try {
      localStorage.removeItem(STORAGE_LOCKED_KEY);
      localStorage.removeItem(STORAGE_LAST_ACTIVE_KEY);
    } catch {
      // Storage unavailable
    }
    this.authService.logout().subscribe({
      next: () => {
        void this.router.navigate(['/login']);
      },
      error: () => {
        this.authService.clearSession();
        void this.router.navigate(['/login']);
      },
    });
  }

  configureTimeouts(standardMs?: number, sensitiveMs?: number, warningMs?: number): void {
    if (typeof standardMs === 'number') {
      this.standardTimeoutMs = standardMs;
    }
    if (typeof sensitiveMs === 'number') {
      this.sensitiveTimeoutMs = sensitiveMs;
    }
    if (typeof warningMs === 'number') {
      this.warningDurationMs = warningMs;
    }
  }

  checkStatus(): void {
    if (!this.authService.isAuthenticated()) {
      if (this.isWarningState()) {
        this.ngZone.run(() => this.isWarningState.set(false));
      }
      return;
    }

    if (this.isLockedState()) {
      return;
    }

    const effectiveTimeout = this.isSensitiveState()
      ? this.sensitiveTimeoutMs
      : this.standardTimeoutMs;
    const elapsed = Date.now() - this.lastActivity;
    const remaining = effectiveTimeout - elapsed;

    if (remaining <= 0) {
      this.lock();
      return;
    }

    if (remaining <= this.warningDurationMs) {
      const seconds = Math.ceil(remaining / 1000);
      this.ngZone.run(() => {
        this.remainingSecondsState.set(seconds);
        if (!this.isWarningState()) {
          this.isWarningState.set(true);
        }
      });
    } else if (this.isWarningState()) {
      this.ngZone.run(() => {
        this.isWarningState.set(false);
      });
    }
  }

  private checkInitialLockState(): boolean {
    try {
      return (
        localStorage.getItem(STORAGE_LOCKED_KEY) === 'true' && this.authService.isAuthenticated()
      );
    } catch {
      return false;
    }
  }

  private checkRouteSensitivity(): boolean {
    let current: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    while (current) {
      if (current.data && current.data['sensitive'] === true) {
        return true;
      }
      current = current.firstChild;
    }
    return false;
  }
}
