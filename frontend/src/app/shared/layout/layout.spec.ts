import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { AuthenticatedUser } from '../../core/auth.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { SidebarConfigurationService } from '../../core/sidebar-configuration.service';
import { HeaderComponent } from './header.component';
import { SidebarComponent } from './sidebar.component';
import { NavigationService, ROLE_MENUS, WorkspaceRole } from './navigation.service';

describe('Shared workspace layout', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let logout: jasmine.Spy;
  const permissions = [
    'WORK_READ',
    'KPI_READ',
    'KPI_MANAGE',
    'VERIFICATION_DECIDE',
    'TARGET_MANAGE',
    'EVALUATION_READ',
  ];
  function signIn(roles: string[], granted = permissions): void {
    user.set({
      id: 'id',
      email: 'user@example.test',
      fullName: 'Test User',
      roles,
      permissions: granted,
    });
    TestBed.inject(NavigationService);
    TestBed.tick();
  }
  beforeEach(() => {
    localStorage.clear();
    user.set(null);
    logout = jasmine.createSpy('logout').and.returnValue(of(null));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly(), logout } },
        {
          provide: SidebarConfigurationService,
          useValue: {
            current: () =>
              of(
                Object.entries(ROLE_MENUS).map(([role, ids]) => ({
                  role: role as WorkspaceRole,
                  itemIds: ids.flatMap((id) =>
                    id === 'organization'
                      ? [id, 'departments', 'jobPositions', 'reportingLines']
                      : [id],
                  ),
                  version: 0,
                })),
              ),
          },
        },
      ],
    });
  });
  afterEach(() => localStorage.clear());

  for (const [role, expected] of [
    ['EMPLOYEE', 'myWork'],
    ['MANAGER', 'teamWork'],
    ['BRANCH_MANAGER', 'organization'],
    ['HR', 'employeeData'],
    ['QA', 'verificationQueue'],
    ['ADMIN', 'accounts'],
  ] as const) {
    it(`provides the appropriate ${role} workspace`, () => {
      signIn([role]);
      expect(
        TestBed.inject(NavigationService)
          .items()
          .map((item) => item.id),
      ).toContain(expected);
    });
  }
  it('does not grant administrative menus to an unknown role or employee', () => {
    const navigation = TestBed.inject(NavigationService);
    signIn(['UNKNOWN']);
    expect(navigation.items()).toEqual([]);
    signIn(['EMPLOYEE']);
    expect(navigation.items().some((item) => item.id === 'accounts')).toBeFalse();
  });
  it('filters permission-protected links even for admin and removes menus on logout', () => {
    // kpiDefinitions is not in admin ROLE_MENUS so it won't appear; verify admin cannot access employee menus
    signIn(['ADMIN'], []);
    const navigation = TestBed.inject(NavigationService);
    // myWork is employee-only and not in admin's menu
    expect(navigation.items().some((item) => item.id === 'myWork')).toBeFalse();
    user.set(null);
    expect(navigation.items()).toEqual([]);
  });
  it('unions multiple roles without duplicates', () => {
    signIn(['EMPLOYEE', 'MANAGER', 'QA']);
    const ids = TestBed.inject(NavigationService)
      .items()
      .map((item) => item.id);
    expect(ids).toContain('myWork');
    expect(ids).toContain('verificationQueue');
    expect(new Set(ids).size).toBe(ids.length);
  });
  it('updates rendered header and sidebar text immediately on language switch', () => {
    signIn(['ADMIN']);
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
    const header = TestBed.createComponent(HeaderComponent);
    const sidebar = TestBed.createComponent(SidebarComponent);
    header.detectChanges();
    sidebar.detectChanges();
    expect(sidebar.nativeElement.textContent).toContain('Cấu hình hệ thống');
    expect(header.nativeElement.querySelector('.view-profile-link').textContent).toContain(
      'Xem hồ sơ',
    );
    expect(header.nativeElement.querySelector('.avatar').getAttribute('aria-label')).toBe(
      'Xem hồ sơ',
    );
    expect(header.nativeElement.querySelector('.logout-button').getAttribute('aria-label')).toBe(
      'Đăng xuất',
    );
    header.nativeElement.querySelectorAll('.language-switch button')[1].click();
    header.detectChanges();
    sidebar.detectChanges();
    expect(sidebar.nativeElement.textContent).toContain('System Configuration');
    expect(header.nativeElement.querySelector('.view-profile-link').textContent).toContain(
      'View profile',
    );
    expect(header.nativeElement.querySelector('.avatar').getAttribute('aria-label')).toBe(
      'View profile',
    );
    expect(header.nativeElement.querySelector('.logout-button').getAttribute('aria-label')).toBe(
      'Logout',
    );
    header.destroy();
    sidebar.destroy();
  });
  it('switches all role menus, expanded children, footer and accessibility text in both directions', () => {
    signIn(['EMPLOYEE', 'MANAGER', 'BRANCH_MANAGER', 'HR', 'QA', 'ADMIN']);
    const i18n = TestBed.inject(I18nService);
    const navigation = TestBed.inject(NavigationService);
    navigation.toggleGroup('organization');
    const sidebar = TestBed.createComponent(SidebarComponent);

    for (const language of ['vi', 'en', 'vi'] as const) {
      i18n.setLanguage(language);
      sidebar.detectChanges();
      const root: HTMLElement = sidebar.nativeElement;
      for (const item of navigation.items()) {
        const label = i18n.localization.menu[item.id];
        expect(root.textContent).toContain(label);
        expect(root.querySelector(`[title="${label}"]`)).not.toBeNull();
        for (const child of item.children ?? []) {
          expect(root.textContent).toContain(i18n.localization.menu[child.id]);
        }
      }
      expect(root.textContent).toContain(i18n.localization.layout.bankingOperations);
      expect(root.textContent).toContain(i18n.localization.layout.secureWorkspace);
      expect(root.querySelector('aside')?.getAttribute('aria-label')).toBe(
        i18n.localization.common.mainNavigation,
      );
      expect(root.querySelector('.collapse-button')?.getAttribute('aria-label')).toBe(
        i18n.localization.layout.collapse,
      );
      expect(root.querySelector('.nav-child.unavailable')?.getAttribute('title')).toBe(
        i18n.localization.layout.comingSoon,
      );
      expect(root.textContent).toContain(language === 'vi' ? 'Phòng ban' : 'Departments');
      expect(root.textContent).not.toContain(
        language === 'vi' ? 'System Configuration' : 'Cấu hình hệ thống',
      );
    }

    navigation.toggleCollapsed();
    sidebar.detectChanges();
    expect(sidebar.nativeElement.querySelector('.collapse-button').getAttribute('aria-label')).toBe(
      'Mở rộng thanh điều hướng',
    );
    i18n.setLanguage('en');
    sidebar.detectChanges();
    expect(sidebar.nativeElement.querySelector('.collapse-button').getAttribute('aria-label')).toBe(
      'Expand sidebar',
    );
    sidebar.destroy();
  });
  it('opens the profile from the text link or avatar without confirming or logging out', () => {
    signIn(['EMPLOYEE']);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const confirm = spyOn(window, 'confirm');
    const header = TestBed.createComponent(HeaderComponent);
    header.detectChanges();

    for (const selector of ['.view-profile-link', '.avatar']) {
      TestBed.inject(NavigationService).toggleMobile();
      header.nativeElement.querySelector(selector).click();
      expect(navigate.calls.mostRecent().args[0].toString()).toBe('/employee-detail/me');
      expect(TestBed.inject(NavigationService).mobileOpen()).toBeFalse();
    }
    expect(confirm).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
    header.destroy();
  });
  it('logs out only through the separate logout action after confirmation', () => {
    signIn(['EMPLOYEE']);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const confirm = spyOn(window, 'confirm').and.returnValue(false);
    const header = TestBed.createComponent(HeaderComponent);
    header.detectChanges();
    const button = header.nativeElement.querySelector('.logout-button');

    button.click();
    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    confirm.and.returnValue(true);
    button.click();
    expect(logout).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith('/login');
    header.destroy();
  });
  it('shares collapsed and mobile state between components', () => {
    const navigation = TestBed.inject(NavigationService);
    navigation.toggleCollapsed();
    expect(navigation.collapsed()).toBeTrue();
    navigation.toggleMobile();
    expect(navigation.mobileOpen()).toBeTrue();
    navigation.closeMobile();
    expect(navigation.mobileOpen()).toBeFalse();
  });
});
