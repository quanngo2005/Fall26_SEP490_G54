import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login-page.component').then((module) => module.LoginPageComponent),
  },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.routes),
  },
  {
    path: 'home',
    canActivate: [authGuard],
    loadChildren: () => import('./features/home/home.routes').then((m) => m.routes),
  },
  {
    path: 'departments',
    canActivate: [authGuard],
    loadChildren: () => import('./features/departments/departments.routes').then((m) => m.routes),
  },
  {
    path: 'employee-detail',
    canActivate: [authGuard],
    data: { sensitive: true },
    loadChildren: () =>
      import('./features/employee-detail/employee-detail.routes').then((m) => m.routes),
  },
  {
    path: 'employee-profile',
    redirectTo: 'employee-detail',
    pathMatch: 'full',
  },
  {
    path: 'kpi-definitions',
    canActivate: [authGuard],
    data: { sensitive: true },
    loadChildren: () =>
      import('./features/kpi-definitions/kpi-definitions.routes').then((m) => m.routes),
  },
  {
    path: 'review-verify',
    canActivate: [authGuard],
    data: { sensitive: true },
    loadChildren: () =>
      import('./features/review-verify/review-verify.routes').then((m) => m.routes),
  },
  {
    path: 'work-type-detail',
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/work-type-detail/work-type-detail.routes').then((m) => m.routes),
  },
  {
    path: 'sidebar-settings',
    canActivate: [authGuard, adminGuard],
    data: { sensitive: true },
    loadChildren: () =>
      import('./features/sidebar-settings/sidebar-settings.routes').then((m) => m.routes),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
