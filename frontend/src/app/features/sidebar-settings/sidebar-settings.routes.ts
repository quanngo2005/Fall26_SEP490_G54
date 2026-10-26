import { Routes } from '@angular/router';
import { adminGuard } from '../../core/admin.guard';

export const routes: Routes = [
  {
    path: '',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./sidebar-settings.component').then((m) => m.SidebarSettingsComponent),
  },
];
