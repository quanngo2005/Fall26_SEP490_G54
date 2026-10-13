import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login-page.component').then((module) => module.LoginPageComponent),
  },
  {
    path: 'home',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/home/home-page.component').then((module) => module.HomePageComponent),
  },
  {
    path: 'employee-profile',
    title: 'Hồ sơ Cán bộ',
    loadComponent: () =>
      import('./features/employee-profile/employee-profile.component').then(
        (module) => module.EmployeeProfileComponent,
      ),
  },
  {
    path: 'work-type-detail',
    title: 'Work Type Detail',
    loadComponent: () =>
      import('./features/work-type-detail/work-type-detail.component').then(
        (m) => m.WorkTypeDetailComponent,
      ),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
