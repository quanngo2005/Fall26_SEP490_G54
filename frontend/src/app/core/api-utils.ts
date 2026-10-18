import { HttpErrorResponse, HttpParams } from '@angular/common/http';
import { ApiResponse } from './api.models';

/** Builds query parameters, skipping undefined, null and empty values. */
export function toHttpParams(filter: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

/** Returns `data` of a successful envelope; a failed or empty envelope is an error, never sample data. */
export function unwrapResponse<T>(response: ApiResponse<T>, resource: string): T {
  if (!response.success || response.data == null) {
    throw new Error(`Invalid ${resource} response`);
  }
  return response.data;
}

export function apiErrorStatus(error: unknown): number | null {
  return error instanceof HttpErrorResponse ? error.status : null;
}

/** Stable `errorCode` of an `ApiResponse.Fail` body. Clients branch on this, never on the message. */
export function apiErrorCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body: unknown = error.error;
  if (body && typeof body === 'object' && 'errorCode' in body) {
    const code = (body as { errorCode?: unknown }).errorCode;
    return typeof code === 'string' ? code : null;
  }
  return null;
}
