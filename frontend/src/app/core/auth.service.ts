import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import {
  catchError,
  finalize,
  map,
  Observable,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError,
} from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, AuthenticatedUser, LoginRequest, StoredAuthSession } from './auth.models';

const SESSION_KEY = 'g54.auth.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private refreshRequest: Observable<AuthResponse> | null = null;
  private readonly currentUserState = signal<AuthenticatedUser | null>(
    this.readSession()?.user ?? null,
  );

  readonly currentUser = this.currentUserState.asReadonly();

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/v1/auth/login`, request)
      .pipe(tap((response) => this.saveSession(response, request.rememberMe)));
  }

  refresh(): Observable<AuthResponse> {
    const session = this.readSession();
    if (!session || !this.isFuture(session.refreshTokenExpiresAt)) {
      this.clearSession();
      return throwError(() => new Error('Cannot refresh without a valid refresh session.'));
    }

    if (!this.refreshRequest) {
      const rememberMe = localStorage.getItem(SESSION_KEY) !== null;
      this.refreshRequest = this.http
        .post<AuthResponse>(`${environment.apiUrl}/v1/auth/refresh`, {
          refreshToken: session.refreshToken,
        })
        .pipe(
          tap((response) => this.saveSession(response, rememberMe)),
          finalize(() => (this.refreshRequest = null)),
          shareReplay({ bufferSize: 1, refCount: false }),
        );
    }

    return this.refreshRequest;
  }

  logout(): Observable<{ message: string }> {
    const session = this.readSession();
    if (!session) {
      throw new Error('Cannot log out without an active authentication session.');
    }

    const session$ = this.isFuture(session.expiresAt) ? of(session) : this.refresh();
    return session$
      .pipe(
        switchMap((activeSession) =>
          this.http.post<{ message: string }>(`${environment.apiUrl}/v1/auth/logout`, {
            refreshToken: activeSession.refreshToken,
          }),
        ),
      )
      .pipe(tap(() => this.clearSession()));
  }

  isAuthenticated(): boolean {
    const session = this.readSession();
    if (!session || !this.isFuture(session.refreshTokenExpiresAt)) {
      this.clearSession();
      return false;
    }

    return true;
  }

  ensureAuthenticated(): Observable<boolean> {
    const session = this.readSession();
    if (!session || !this.isFuture(session.refreshTokenExpiresAt)) {
      this.clearSession();
      return of(false);
    }

    if (this.isFuture(session.expiresAt)) {
      return of(true);
    }

    return this.refresh().pipe(
      map(() => true),
      catchError(() => {
        this.clearSession();
        return of(false);
      }),
    );
  }

  accessToken(): string | null {
    const session = this.readSession();
    return session && this.isFuture(session.expiresAt) ? session.accessToken : null;
  }

  clearSession(): void {
    this.removeStoredSession();
    this.currentUserState.set(null);
  }

  private removeStoredSession(): void {
    localStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(SESSION_KEY);
  }

  private saveSession(response: AuthResponse, rememberMe: boolean): void {
    const storage = rememberMe ? localStorage : sessionStorage;
    const otherStorage = rememberMe ? sessionStorage : localStorage;
    otherStorage.removeItem(SESSION_KEY);
    storage.setItem(SESSION_KEY, JSON.stringify(response));
    this.currentUserState.set(response.user);
  }

  private isFuture(value: string): boolean {
    const timestamp = Date.parse(value);
    return Number.isFinite(timestamp) && timestamp > Date.now();
  }

  private readSession(): StoredAuthSession | null {
    const value = localStorage.getItem(SESSION_KEY) ?? sessionStorage.getItem(SESSION_KEY);
    if (!value) {
      return null;
    }

    try {
      const session: StoredAuthSession = JSON.parse(value) as StoredAuthSession;
      if (
        typeof session.accessToken !== 'string' ||
        typeof session.refreshToken !== 'string' ||
        typeof session.expiresAt !== 'string' ||
        typeof session.refreshTokenExpiresAt !== 'string' ||
        !session.user
      ) {
        this.removeStoredSession();
        return null;
      }
      return session;
    } catch {
      this.removeStoredSession();
      return null;
    }
  }
}
