import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canViewOrganization, normalizePermission, organizationGuard } from './organization.guard';

describe('organizationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[], roles: string[] = []) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles, permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => organizationGuard({} as never, {} as never));
  const home = () => router.parseUrl('/home').toString();

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

  for (const permissions of [
    ['ORGANIZATION_READ'],
    ['organization:read'],
    ['organization.read'],
    ['organization-read'],
    [' Organization_Read '],
    ['FULL_ACCESS'],
    ['WORK_READ', 'KPI_READ', 'ORGANIZATION_READ'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [
    [],
    ['WORK_READ', 'KPI_READ', 'EVALUATION_READ', 'EMPLOYEE_READ'],
    ['ORGANIZATION_MANAGE'],
    ['ORGANIZATION'],
    ['READ'],
    [''],
  ]) {
    it(`redirects ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(String(run())).toBe(home());
    });
  }

  it('decides on the permission, not on the role name', () => {
    as(['WORK_READ'], ['SYSTEM_ADMIN', 'BRANCH_MANAGER', 'DEPT_MANAGER']);
    expect(String(run())).toBe(home());

    as(['ORGANIZATION_READ'], ['EMPLOYEE']);
    expect(run()).toBeTrue();
  });

  it('no longer opens for the old evaluation read permission', () => {
    as(['EVALUATION_READ']);
    expect(String(run())).toBe(home());
  });

  it('redirects a missing user to /home', () => {
    expect(String(run())).toBe(home());
  });

  it('exposes pure helpers that tolerate missing input', () => {
    expect(canViewOrganization(undefined)).toBeFalse();
    expect(canViewOrganization(null)).toBeFalse();
    expect(canViewOrganization([])).toBeFalse();
    expect(normalizePermission(undefined)).toBe('');
    expect(normalizePermission(' organization:read ')).toBe('ORGANIZATION_READ');
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
});
