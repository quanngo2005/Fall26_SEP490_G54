import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { SystemParameter, SystemParametersSavePayload } from './system-parameter.models';

@Injectable({ providedIn: 'root' })
export class SystemParameterService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/system-parameters`;

  list(): Observable<SystemParameter[]> {
    return this.http
      .get<ApiResponse<SystemParameter[]>>(this.url)
      .pipe(map((response) => this.unwrap(response)));
  }

  save(payload: SystemParametersSavePayload): Observable<SystemParameter[]> {
    return this.http
      .put<ApiResponse<SystemParameter[]>>(this.url, payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid system parameter response');
    }
    return response.data;
  }
}
