import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';

export const routes: Routes = [
  {
    path: 'sidebar-settings',
    canActivate: [authGuard, adminGuard],
    loadComponent: () =>
      import('./features/sidebar-settings/sidebar-settings.component').then(
        (m) => m.SidebarSettingsComponent,
      ),
  },
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
    path: 'departments',
    canActivate: [authGuard],
    title: 'Department View',
    loadComponent: () =>
      import('./features/departments/department-view.component').then(
        (module) => module.DepartmentViewComponent,
      ),
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
  {
    path: 'review-verify',
    title: 'Thẩm định kết quả công việc',
    loadComponent: () =>
      import('./features/review-verify/review-verify.component').then(
        (m) => m.ReviewVerifyComponent,
      ),
  },
  {
    path: 'review-verify/:submissionId',
    title: 'Thẩm định kết quả công việc',
    loadComponent: () =>
      import('./features/review-verify/review-verify.component').then(
        (m) => m.ReviewVerifyComponent,
      ),
  },
  {
    path: 'kpi-definitions',
    canActivate: [authGuard],
    title: 'KPI Definitions - Cấu hình đo lường hiệu suất',
    loadComponent: () =>
      import('./features/kpi-definitions/kpi-definitions.component').then(
        (m) => m.KpiDefinitionsComponent,
      ),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
