import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import type { MenuKey, WorkspaceRole } from '../shared/layout/navigation.service';

export interface SidebarConfiguration {
  role: WorkspaceRole;
  itemIds: MenuKey[];
  version: number;
  routes?: Partial<Record<MenuKey, string>>;
}

@Injectable({ providedIn: 'root' })
export class SidebarConfigurationService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/sidebar-configurations`;

  current() {
    return this.http
      .get<ApiResponse<SidebarConfiguration[]>>(`${this.url}/current`)
      .pipe(map((response) => this.unwrap(response)));
  }

  all() {
    return this.http
      .get<ApiResponse<SidebarConfiguration[]>>(this.url)
      .pipe(map((response) => this.unwrap(response)));
  }

  save(config: SidebarConfiguration) {
    return this.http
      .put<ApiResponse<SidebarConfiguration>>(`${this.url}/${config.role}`, {
        itemIds: config.itemIds,
        version: config.version,
        ...(config.routes !== undefined ? { routes: config.routes } : {}),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) throw new Error('Invalid sidebar response');
    return response.data;
  }
}
