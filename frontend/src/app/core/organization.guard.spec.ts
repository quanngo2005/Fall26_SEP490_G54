import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import {
  canViewOrganization,
  normalizeRole,
  ORGANIZATION_VIEWER_ROLES,
  organizationGuard,
} from './organization.guard';

describe('organizationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (roles: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles, permissions: [] });
  const run = () =>
    TestBed.runInInjectionContext(() => organizationGuard({} as never, {} as never));

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    });
    router = TestBed.inject(Router);
  });

  for (const role of [
    'SYSTEM_ADMIN',
    'ADMIN',
    'BRANCH_MANAGER',
    'DEPT_MANAGER',
    'DEPARTMENT_MANAGER',
    'MANAGER',
    'branch-manager',
    ' Dept Manager ',
    'system admin',
  ]) {
    it(`lets "${role}" through`, () => {
      as([role]);
      expect(run()).toBeTrue();
    });
  }

  for (const role of [
    'EMPLOYEE',
    'HR',
    'HR_MANAGER',
    'QA',
    'VERIFIER',
    'COMPLIANCE',
    'AUDITOR',
    'MANAGER_ASSISTANT',
    '',
  ]) {
    it(`redirects "${role}" to /home`, () => {
      as([role]);
      expect(String(run())).toBe(router.parseUrl('/home').toString());
    });
  }

  it('lets a user with several roles through when any one is allowed', () => {
    as(['EMPLOYEE', 'VERIFIER', 'BRANCH_MANAGER']);
    expect(run()).toBeTrue();
  });

  it('redirects a signed-in user with no roles, and a missing user, to /home', () => {
    as([]);
    expect(String(run())).toBe(router.parseUrl('/home').toString());
    user.set(null);
    expect(String(run())).toBe(router.parseUrl('/home').toString());
  });

  it('exposes pure helpers that tolerate missing input', () => {
    expect(canViewOrganization(undefined)).toBeFalse();
    expect(canViewOrganization(null)).toBeFalse();
    expect(canViewOrganization([])).toBeFalse();
    expect(normalizeRole(undefined)).toBe('');
    expect(normalizeRole('  dept-manager ')).toBe('DEPT_MANAGER');
  });

  it('is applied after authGuard on every organization route', () => {
    const guarded = routes.filter((route) =>
      ['departments', 'job-positions', 'department-performance'].includes(route.path ?? ''),
    );

    expect(guarded.map((route) => route.path)).toEqual([
      'departments',
      'job-positions',
      'department-performance',
    ]);
    for (const route of guarded) {
      const guards = route.canActivate ?? [];
      expect(guards.length).toBe(2);
      expect(guards[1]).toBe(organizationGuard);
    }
  });

  it('does not grant the screens to any role the sidebar maps to employee, hr or qa', () => {
    const blocked = ['EMPLOYEE', 'HR', 'HR_MANAGER', 'QA', 'COMPLIANCE', 'VERIFIER'];

    expect(blocked.filter((role) => ORGANIZATION_VIEWER_ROLES.has(role))).toEqual([]);
  });
});
