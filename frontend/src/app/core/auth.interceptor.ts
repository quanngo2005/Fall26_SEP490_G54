import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const apiRoot = environment.apiUrl.endsWith('/') ? environment.apiUrl : `${environment.apiUrl}/`;
  const isApiRequest = request.url === environment.apiUrl || request.url.startsWith(apiRoot);
  const isLoginRequest = request.url.endsWith('/v1/auth/login');
  const isRefreshRequest = request.url.endsWith('/v1/auth/refresh');
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
