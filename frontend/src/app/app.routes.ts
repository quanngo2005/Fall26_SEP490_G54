import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { organizationGuard } from './core/organization.guard';

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
    path: 'roles',
    canActivate: [authGuard, adminGuard],
    title: 'Vai trò & quyền',
    loadComponent: () =>
      import('./features/role-permissions/role-list.component').then((m) => m.RoleListComponent),
  },
  {
    path: 'user-accounts',
    canActivate: [authGuard, adminGuard],
    title: 'Tài khoản người dùng',
    loadComponent: () =>
      import('./features/user-accounts/user-account-list.component').then(
        (m) => m.UserAccountListComponent,
      ),
  },
  {
    path: 'role-permissions',
    canActivate: [authGuard, adminGuard],
    title: 'Thiết lập quyền',
    loadComponent: () =>
      import('./features/role-permissions/role-permission-matrix.component').then(
        (m) => m.RolePermissionMatrixComponent,
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
    canActivate: [authGuard, organizationGuard],
    title: 'Phòng ban',
    loadComponent: () =>
      import('./features/departments/department-list.component').then(
        (module) => module.DepartmentListComponent,
      ),
  },
  {
    path: 'job-positions',
    canActivate: [authGuard, organizationGuard],
    title: 'Vị trí công việc',
    loadComponent: () =>
      import('./features/job-positions/job-position-list.component').then(
        (m) => m.JobPositionListComponent,
      ),
  },
  {
    path: 'department-performance',
    canActivate: [authGuard, organizationGuard],
    title: 'Hiệu suất phòng ban',
    loadComponent: () =>
      import('./features/departments/department-view.component').then(
        (module) => module.DepartmentViewComponent,
      ),
  },
  {
    path: 'evaluations',
    canActivate: [authGuard],
    title: 'Đánh giá nhân viên',
    loadComponent: () =>
      import('./features/evaluations/evaluations.component').then((m) => m.EvaluationsComponent),
  },
  {
    path: 'employees',
    canActivate: [authGuard],
    title: 'Nhân viên',
    loadComponent: () =>
      import('./features/employees/employee-list.component').then((m) => m.EmployeeListComponent),
  },
  {
    path: 'employee-detail',
    title: 'Hồ sơ Cán bộ',
    loadComponent: () =>
      import('./features/employee-detail/employee-detail.component').then(
        (m) => m.EmployeeDetailComponent,
      ),
  },
  {
    path: 'employee-detail/:code',
    title: 'Hồ sơ Cán bộ',
    loadComponent: () =>
      import('./features/employee-detail/employee-detail.component').then(
        (m) => m.EmployeeDetailComponent,
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
    path: 'work-type-detail/:id',
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
    path: 'my-work',
    canActivate: [authGuard],
    title: 'Công việc',
    loadComponent: () =>
      import('./features/work-board/work-board.component').then((m) => m.WorkBoardComponent),
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
