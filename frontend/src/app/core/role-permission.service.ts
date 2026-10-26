import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  RoleOption,
  RolePermissionMatrix,
  RolePermissionMatrixSavePayload,
} from './role-permission.models';

@Injectable({ providedIn: 'root' })
export class RolePermissionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/roles`;

  roles(): Observable<RoleOption[]> {
    return this.http
      .get<ApiResponse<RoleOption[]>>(this.url)
      .pipe(map((response) => this.unwrap(response)));
  }

  matrix(roleId: string): Observable<RolePermissionMatrix> {
    return this.http
      .get<ApiResponse<RolePermissionMatrix>>(this.matrixUrl(roleId))
      .pipe(map((response) => this.unwrap(response)));
  }

  save(roleId: string, payload: RolePermissionMatrixSavePayload): Observable<RolePermissionMatrix> {
    return this.http
      .put<ApiResponse<RolePermissionMatrix>>(this.matrixUrl(roleId), payload)
      .pipe(map((response) => this.unwrap(response)));
  }

  private matrixUrl(roleId: string): string {
    return `${this.url}/${encodeURIComponent(roleId)}/permission-matrix`;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid role permission response');
    }
    return response.data;
  }
}
