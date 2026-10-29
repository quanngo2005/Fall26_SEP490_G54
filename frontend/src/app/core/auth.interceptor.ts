import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = new URL(environment.apiUrl || '/', window.location.origin);
  const requestUrl = new URL(request.url, window.location.origin);
  const isApiRequest =
    requestUrl.origin === apiBase.origin &&
    /^\/(auth|me|departments|employees|works|work-types|job-positions|workflows|kpis|kpi-definitions|kpi-results|business-objectives|submissions|verifications|verification|evidence|hello|sidebar-configurations|evaluations|campaigns|roles|user-accounts|system-parameters)(\/|$)/.test(
      requestUrl.pathname,
    );
  const isLoginRequest = requestUrl.pathname === '/auth/login';
  const isRefreshRequest = requestUrl.pathname === '/auth/refresh';
  const authService = isApiRequest ? inject(AuthService) : null;
  const accessToken =
    isApiRequest && !isLoginRequest && !isRefreshRequest ? authService?.accessToken() : null;
  const authenticatedRequest = accessToken
    ? request.clone({
        setHeaders: { Authorization: `Bearer ${accessToken}` },
      })
    : request;

  return next(authenticatedRequest).pipe(
    catchError((error: unknown) => {
      if (
        !authService ||
        !(error instanceof HttpErrorResponse) ||
        error.status !== 401 ||
        isLoginRequest ||
        isRefreshRequest
      ) {
        return throwError(() => error);
      }

      return authService.refresh().pipe(
        catchError(() => {
          authService.clearSession();
          return throwError(() => error);
        }),
        switchMap(() => {
          const refreshedAccessToken = authService.accessToken();
          return refreshedAccessToken
            ? next(
                request.clone({
                  setHeaders: { Authorization: `Bearer ${refreshedAccessToken}` },
                }),
              )
            : throwError(() => error);
        }),
      );
    }),
  );
};
