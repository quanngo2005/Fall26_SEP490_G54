import { HttpRequest } from '@angular/common/http';
import { ApiResponse } from '../api-response.model';

export interface MockRoute {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  /** Matched against the path after `environment.apiUrl`; capture groups become params. */
  path: RegExp;
  handle(request: HttpRequest<unknown>, params: string[]): ApiResponse<unknown>;
}

export function ok<T>(data: T, status = 200): ApiResponse<T> {
  return { success: true, status, data };
}

export function fail(status: number, errorCode: string, message: string): ApiResponse<null> {
  return { success: false, status, data: null, errorCode, message };
}
