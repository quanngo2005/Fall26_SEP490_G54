import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canManageTargets, targetManageGuard } from './target-manage.guard';

describe('targetManageGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => targetManageGuard({} as never, {} as never));
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
    ['TARGET_MANAGE'],
    ['target:manage'],
    ['target.manage'],
    ['target-manage'],
    [' Target_Manage '],
    ['FULL_ACCESS'],
    ['WORK_READ', 'TARGET_MANAGE'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [
    [],
    ['WORK_READ', 'KPI_READ'],
    ['TARGET'],
    ['MANAGE'],
    [''],
    ['KPI_MANAGE'],
  ]) {
    it(`redirects ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(String(run())).toBe(home());
    });
  }

  it('redirects a missing user to /home', () => {
    expect(String(run())).toBe(home());
  });

  it('tolerates missing permission input', () => {
    expect(canManageTargets(undefined)).toBeFalse();
    expect(canManageTargets(null)).toBeFalse();
    expect(canManageTargets([])).toBeFalse();
  });

  it('is applied after authGuard on the target setup route', () => {
    const route = routes.find((item) => item.path === 'target-setup/:year');

    expect(route).toBeDefined();
    const guards = route?.canActivate ?? [];
    expect(guards.length).toBe(2);
    expect(guards[1]).toBe(targetManageGuard);
  });
});
