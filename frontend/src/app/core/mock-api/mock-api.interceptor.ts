import { HttpEvent, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Observable, delay, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../api-response.model';
import { MockRoute } from './mock-route';
import { ROLE_MOCK_ROUTES } from './roles.mock';

/**
 * Mock routes for endpoints the backend has not implemented yet. Remove a route
 * (and its data file) when the real endpoint ships; unregistered requests always
 * reach the API, so a missing route never hides a real failure.
 */
export const MOCK_ROUTES: readonly MockRoute[] = [...ROLE_MOCK_ROUTES];

export const isMockApiEnabled = (): boolean => environment.useMockApi === 'true';

export const mockApiInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isMockApiEnabled()) return next(request);
  const path = apiPath(request.url);
  if (path === null) return next(request);
  for (const route of MOCK_ROUTES) {
    if (route.method !== request.method) continue;
    const match = route.path.exec(path);
    if (match) return respond(request, route.handle(request, match.slice(1)));
  }
  return next(request);
};

function apiPath(url: string): string | null {
  const base = new URL(environment.apiUrl || '/', window.location.origin);
  const target = new URL(url, window.location.origin);
  if (target.origin !== base.origin) return null;
  const prefix = base.pathname.replace(/\/$/, '');
  if (!target.pathname.startsWith(prefix)) return null;
  return target.pathname.slice(prefix.length) || '/';
}

function respond<T>(
  request: HttpRequest<unknown>,
  body: ApiResponse<T>,
): Observable<HttpEvent<ApiResponse<T>>> {
  const latency = 250;
  if (body.success) {
    return of(new HttpResponse({ status: body.status, body, url: request.url })).pipe(
      delay(latency),
    );
  }
  return throwError(
    () => new HttpErrorResponse({ status: body.status, error: body, url: request.url }),
  ).pipe(delay(latency));
}
