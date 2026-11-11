import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { organizationGuard } from './core/organization.guard';
import { branchEvaluationGuard } from './core/branch-evaluation.guard';
import { targetManageGuard } from './core/target-manage.guard';
import { qaQueueGuard } from './core/qa-queue.guard';
import { riskEventGuard } from './core/risk-event.guard';
import { appealQueueGuard, appealsHomeRedirect, ownAppealsGuard } from './core/appeal-queue.guard';
import { personalEvaluationGuard } from './core/personal-evaluation.guard';

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
    path: 'target-overview',
    canActivate: [authGuard],
    title: 'Tổng quan mục tiêu',
    loadComponent: () =>
      import('./features/target-overview/target-overview.component').then(
        (m) => m.TargetOverviewComponent,
      ),
  },
  {
    path: 'target-setup/:year',
    canActivate: [authGuard, targetManageGuard],
    title: 'Phân bổ mục tiêu',
    loadComponent: () =>
      import('./features/target-setup/target-setup-wizard.component').then(
        (m) => m.TargetSetupWizardComponent,
      ),
  },
  {
    path: 'target-allocation',
    canActivate: [authGuard],
    title: 'Mục tiêu nhân viên',
    loadComponent: () =>
      import('./features/target-allocation/target-allocation.component').then(
        (m) => m.TargetAllocationComponent,
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
    path: 'evaluations/form',
    canActivate: [authGuard],
    title: 'Đánh giá nhân viên',
    loadComponent: () =>
      import('./features/evaluations/evaluation-form.component').then(
        (m) => m.EvaluationFormComponent,
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
    path: 'branch-performance',
    canActivate: [authGuard, organizationGuard],
    title: 'Hiệu suất chi nhánh',
    loadComponent: () =>
      import('./features/branch-performance/branch-performance.component').then(
        (module) => module.BranchPerformanceComponent,
      ),
  },
  {
    path: 'branch-performance/evaluations',
    canActivate: [authGuard, branchEvaluationGuard],
    title: 'Kỳ đánh giá chi nhánh',
    loadComponent: () =>
      import('./features/evaluations/branch-evaluation-list.component').then(
        (m) => m.BranchEvaluationListComponent,
      ),
  },
  {
    path: 'branch-performance/evaluations/:periodId/approval',
    canActivate: [authGuard, branchEvaluationGuard],
    title: 'Phê duyệt xếp hạng phòng ban',
    loadComponent: () =>
      import('./features/evaluations/department-ranking-approval/department-ranking-approval.component').then(
        (m) => m.DepartmentRankingApprovalComponent,
      ),
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
    path: 'work-types',
    canActivate: [authGuard, adminGuard],
    title: 'Work Types',
    loadComponent: () =>
      import('./features/work-types/work-type-list.component').then((m) => m.WorkTypeListComponent),
  },
  {
    path: 'evaluation-profiles',
    canActivate: [authGuard, organizationGuard],
    title: 'Evaluation Profile',
    loadComponent: () =>
      import('./features/evaluation-profiles/evaluation-profile-list.component').then(
        (m) => m.EvaluationProfileListComponent,
      ),
  },
  {
    path: 'evaluation-profiles/:id',
    canActivate: [authGuard, organizationGuard],
    title: 'Evaluation Profile',
    loadComponent: () =>
      import('./features/evaluation-profiles/evaluation-profile-detail.component').then(
        (m) => m.EvaluationProfileDetailComponent,
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
    path: 'risk-events',
    canActivate: [authGuard, riskEventGuard],
    title: 'Sự kiện rủi ro',
    loadComponent: () =>
      import('./features/risk-events/risk-events.component').then((m) => m.RiskEventsComponent),
  },
  {
    path: 'qa-queue',
    canActivate: [authGuard, qaQueueGuard],
    title: 'Hàng chờ QA',
    loadComponent: () =>
      import('./features/qa-queue/qa-queue.component').then((m) => m.QaQueueComponent),
  },
  {
    path: 'qa-queue/:kind/:id',
    canActivate: [authGuard, qaQueueGuard],
    title: 'Quyết định QA',
    loadComponent: () =>
      import('./features/qa-queue/qa-queue-detail.component').then((m) => m.QaQueueDetailComponent),
  },
  {
    path: 'appeals',
    pathMatch: 'full',
    redirectTo: appealsHomeRedirect,
  },
  {
    path: 'appeals/mine',
    canActivate: [authGuard, ownAppealsGuard],
    title: 'Kháng nghị của tôi',
    loadComponent: () =>
      import('./features/appeals/my-appeals.component').then((m) => m.MyAppealsComponent),
  },
  {
    path: 'appeals/department',
    canActivate: [authGuard, appealQueueGuard],
    title: 'Kháng nghị phòng ban',
    loadComponent: () =>
      import('./features/appeals/department-appeals.component').then(
        (m) => m.DepartmentAppealsComponent,
      ),
  },
  {
    path: 'appeals/department/:id',
    canActivate: [authGuard, appealQueueGuard],
    title: 'Chi tiết khiếu nại',
    loadComponent: () =>
      import('./features/appeals/department-appeal-detail.component').then(
        (m) => m.DepartmentAppealDetailComponent,
      ),
  },
  {
    path: 'my-evaluation',
    canActivate: [authGuard, personalEvaluationGuard],
    title: 'Kết quả đánh giá của tôi',
    loadComponent: () =>
      import('./features/my-evaluation/my-evaluation.component').then(
        (m) => m.MyEvaluationComponent,
      ),
  },
  {
    path: 'my-targets',
    canActivate: [authGuard],
    title: 'Mục tiêu của tôi',
    loadComponent: () =>
      import('./features/my-targets/my-targets.component').then((m) => m.MyTargetsComponent),
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
  {
    path: 'campaigns',
    canActivate: [authGuard],
    title: 'Chiến dịch',
    loadComponent: () =>
      import('./features/campaigns/campaign-list.component').then((m) => m.CampaignListComponent),
  },
  {
    path: 'campaigns/approval',
    canActivate: [authGuard],
    title: 'Duyệt kế hoạch chiến dịch',
    loadComponent: () =>
      import('./features/campaigns/campaign-approval.component').then(
        (m) => m.CampaignApprovalComponent,
      ),
  },
  {
    path: 'campaigns/:id',
    canActivate: [authGuard],
    title: 'Chi tiết chiến dịch',
    loadComponent: () =>
      import('./features/campaigns/campaign-detail.component').then(
        (m) => m.CampaignDetailComponent,
      ),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
