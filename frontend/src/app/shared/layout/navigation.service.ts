import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import {
  SidebarConfiguration,
  SidebarConfigurationService,
} from '../../core/sidebar-configuration.service';

export type WorkspaceRole =
  'employee' | 'departmentManager' | 'branchManager' | 'hr' | 'qa' | 'admin';
export type MenuKey =
  | 'overview'
  | 'myWork'
  | 'mySubmissions'
  | 'myPerformance'
  | 'notifications'
  | 'myProfile'
  | 'employees'
  | 'organization'
  | 'departments'
  | 'jobPositions'
  | 'reportingLines'
  | 'employeeData'
  | 'reportsComplaints'
  | 'teamWork'
  | 'handoverDelegation'
  | 'reviewVerify'
  | 'teamPerformance'
  | 'targetsAndCampaigns'
  | 'branchWork'
  | 'branchPerformance'
  | 'departmentPerformance'
  | 'reportingStructure'
  | 'verificationQueue'
  | 'escalatedCases'
  | 'auditReview'
  | 'riskEvents'
  | 'reportsAnalytics'
  | 'accounts'
  | 'rolesPermissions'
  | 'accessPolicies'
  | 'systemConfiguration'
  | 'systemAudit'
  | 'tasks'
  | 'personalKpis'
  | 'review'
  | 'positions'
  | 'kpiDefinitions'
  | 'workTypes'
  | 'targets'
  | 'reports'
  | 'audit'
  | 'users'
  | 'settings';

export interface NavigationItem {
  id: MenuKey;
  icon: string;
  route?: string;
  permission?: string;
  children?: NavigationItem[];
}

const ROLE_CODES: Readonly<Record<string, WorkspaceRole>> = {
  EMPLOYEE: 'employee',
  MANAGER: 'departmentManager',
  DEPT_MANAGER: 'departmentManager',
  DEPARTMENT_MANAGER: 'departmentManager',
  BRANCH_MANAGER: 'branchManager',
  HR: 'hr',
  HR_MANAGER: 'hr',
  QA: 'qa',
  COMPLIANCE: 'qa',
  VERIFIER: 'qa',
  ADMIN: 'admin',
  SYSTEM_ADMIN: 'admin',
};

export const ROLE_MENUS: Readonly<Record<WorkspaceRole, readonly MenuKey[]>> = {
  employee: [
    'overview',
    'myWork',
    'mySubmissions',
    'myPerformance',
    'reportsComplaints',
    'notifications',
    'myProfile',
  ],
  departmentManager: [
    'overview',
    'employees',
    'teamWork',
    'handoverDelegation',
    'reviewVerify',
    'teamPerformance',
    'targetsAndCampaigns',
    'reportsComplaints',
    'notifications',
    'myProfile',
  ],
  branchManager: [
    'overview',
    'organization',
    'myWork',
    'branchWork',
    'handoverDelegation',
    'reviewVerify',
    'branchPerformance',
    'departmentPerformance',
    'targetsAndCampaigns',
    'reportsComplaints',
    'notifications',
    'myProfile',
  ],
  hr: [
    'overview',
    'employees',
    'organization',
    'employeeData',
    'reportsComplaints',
    'notifications',
    'myProfile',
  ],
  qa: [
    'overview',
    'verificationQueue',
    'reportsComplaints',
    'escalatedCases',
    'auditReview',
    'riskEvents',
    'reportsAnalytics',
    'notifications',
    'myProfile',
  ],
  admin: [
    'overview',
    'accounts',
    'rolesPermissions',
    'accessPolicies',
    'systemConfiguration',
    'systemAudit',
    'myProfile',
  ],
};

const ITEMS: readonly NavigationItem[] = [
  { id: 'overview', icon: 'dashboard', route: '/home' },
  { id: 'myWork', icon: 'myWork', permission: 'WORK_READ' },
  { id: 'mySubmissions', icon: 'mySubmissions', permission: 'WORK_READ' },
  { id: 'myPerformance', icon: 'myPerformance' },
  { id: 'reportsComplaints', icon: 'reportsComplaints' },
  { id: 'notifications', icon: 'notifications' },
  { id: 'myProfile', icon: 'myProfile', route: '/employee-detail/me' },
  { id: 'employees', icon: 'people', route: '/employees' },
  {
    id: 'organization',
    icon: 'organization',
    children: [
      { id: 'departments', icon: 'departments', route: '/departments' },
      { id: 'jobPositions', icon: 'jobPositions', route: '/job-positions' },
      { id: 'reportingLines', icon: 'reportingLines' },
    ],
  },
  { id: 'employeeData', icon: 'employeeData', permission: 'KPI_READ' },
  { id: 'teamWork', icon: 'teamWork' },
  { id: 'handoverDelegation', icon: 'handoverDelegation' },
  {
    id: 'reviewVerify',
    icon: 'reviewVerify',
    route: '/review-verify',
    permission: 'VERIFICATION_DECIDE',
  },
  { id: 'teamPerformance', icon: 'teamPerformance' },
  { id: 'targetsAndCampaigns', icon: 'targetsAndCampaigns', permission: 'TARGET_MANAGE' },
  { id: 'branchWork', icon: 'branchWork' },
  { id: 'branchPerformance', icon: 'branchPerformance' },
  { id: 'departmentPerformance', icon: 'departmentPerformance', route: '/department-performance' },
  { id: 'verificationQueue', icon: 'verificationQueue', permission: 'VERIFICATION_DECIDE' },
  { id: 'escalatedCases', icon: 'escalatedCases' },
  { id: 'auditReview', icon: 'auditReview' },
  { id: 'riskEvents', icon: 'riskEvents' },
  { id: 'reportsAnalytics', icon: 'reportsAnalytics', permission: 'EVALUATION_READ' },
  { id: 'accounts', icon: 'people' },
  { id: 'rolesPermissions', icon: 'people', route: '/roles' },
  { id: 'accessPolicies', icon: 'accessPolicies' },
  { id: 'systemConfiguration', icon: 'systemConfiguration', route: '/sidebar-settings' },
  { id: 'systemAudit', icon: 'systemAudit' },
  // Legacy items for backward compat
  { id: 'tasks', icon: 'myWork', permission: 'WORK_READ' },
  { id: 'personalKpis', icon: 'employeeData', permission: 'KPI_READ' },
  {
    id: 'review',
    icon: 'reviewVerify',
    route: '/review-verify',
    permission: 'VERIFICATION_DECIDE',
  },
  { id: 'positions', icon: 'jobPositions' },
  {
    id: 'kpiDefinitions',
    icon: 'reportsAnalytics',
    route: '/kpi-definitions',
    permission: 'KPI_MANAGE',
  },
  { id: 'workTypes', icon: 'myWork', route: '/work-type-detail' },
  { id: 'targets', icon: 'targetsAndCampaigns', permission: 'TARGET_MANAGE' },
  { id: 'reports', icon: 'reportsAnalytics', permission: 'EVALUATION_READ' },
  { id: 'audit', icon: 'auditReview' },
  { id: 'users', icon: 'people' },
  { id: 'settings', icon: 'systemConfiguration' },
];

@Injectable({ providedIn: 'root' })
export class NavigationService {
  private readonly api = inject(SidebarConfigurationService);
  readonly configurations = signal<SidebarConfiguration[]>([]);
  private readonly refreshVersion = signal(0);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly catalog = ITEMS;
  readonly workspaceRoles = Object.keys(ROLE_MENUS) as WorkspaceRole[];
  readonly routeOptions = [
    '/home',
    '/departments',
    '/department-performance',
    '/employee-detail',
    '/work-type-detail',
    '/review-verify',
    '/kpi-definitions',
    '/sidebar-settings',
    '/roles',
  ];
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly currentUrl = toSignal(
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)),
    { initialValue: null },
  );
  readonly collapsed = signal(false);
  readonly mobileOpen = signal(false);
  readonly expandedGroups = signal<Set<MenuKey>>(new Set());
  readonly roles = computed(() => [
    ...new Set(
      (this.auth.currentUser()?.roles ?? [])
        .map(
          (code) =>
            ROLE_CODES[
              code
                .trim()
                .toUpperCase()
                .replace(/[\s-]+/g, '_')
            ],
        )
        .filter((role): role is WorkspaceRole => role !== undefined),
    ),
  ]);
  readonly isAdmin = computed(() => this.roles().includes('admin'));

  constructor() {
    effect((onCleanup) => {
      const user = this.auth.currentUser();
      this.refreshVersion();
      this.configurations.set([]);
      this.loadFailed.set(false);
      this.loading.set(!!user);
      if (!user) return;
      const subscription = this.api.current().subscribe({
        next: (configs) => {
          this.configurations.set(configs);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  reload(): void {
    this.refreshVersion.update((version) => version + 1);
  }

  buildItems(
    ids: readonly MenuKey[],
    routes: Partial<Record<MenuKey, string>> = {},
  ): NavigationItem[] {
    return ids.flatMap<NavigationItem>((id) => {
      const item = ITEMS.find((candidate) => candidate.id === id);
      if (!item) return [];
      if (!item.children) return [{ ...item, route: routes[id] ?? item.route }];
      return [
        {
          ...item,
          children: ids.flatMap((childId) => {
            const child = item.children?.find((candidate) => candidate.id === childId);
            return child ? [{ ...child, route: routes[childId] ?? child.route }] : [];
          }),
        },
      ];
    });
  }

  readonly items = computed(() => {
    const user = this.auth.currentUser();
    if (!user) return [];
    const roleConfigs = this.roles().flatMap(
      (role) => this.configurations().find((config) => config.role === role) ?? [],
    );
    const ids: MenuKey[] = [];
    const routes: Partial<Record<MenuKey, string>> = {};
    for (const config of roleConfigs) {
      for (const id of config.itemIds) {
        if (ids.includes(id)) continue;
        ids.push(id);
        if (config.routes?.[id] !== undefined) routes[id] = config.routes[id];
      }
    }
    const permissions = new Set(
      user.permissions.map((p) => p.toUpperCase().replace(/[.:-]/g, '_')),
    );
    return this.buildItems(ids, routes).filter(
      (item) =>
        (item.id !== 'systemConfiguration' || this.isAdmin()) &&
        (!item.permission || permissions.has(item.permission)),
    );
  });
  readonly currentPageKey = computed((): MenuKey | null => {
    // Depend on currentUrl signal so this recomputes on navigation
    this.currentUrl();
    const url = this.router.url.split('?')[0];
    const allItems = this.items().flatMap((item) =>
      item.children ? [item, ...item.children] : [item],
    );
    // Find longest matching route (most specific match wins)
    const match = allItems
      .filter((item) => item.route && url.startsWith(item.route))
      .sort((a, b) => (b.route?.length ?? 0) - (a.route?.length ?? 0))[0];
    return match?.id ?? null;
  });
  toggleCollapsed(): void {
    this.collapsed.update((value) => !value);
  }
  toggleMobile(): void {
    this.mobileOpen.update((value) => !value);
  }
  closeMobile(): void {
    this.mobileOpen.set(false);
  }
  toggleGroup(id: MenuKey): void {
    this.expandedGroups.update((groups) => {
      const next = new Set(groups);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }
  isGroupExpanded(id: MenuKey): boolean {
    return this.expandedGroups().has(id);
  }
}
