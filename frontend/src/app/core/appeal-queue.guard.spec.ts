import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import {
  appealQueueGuard,
  appealsHomeRedirect,
  canDecideAppeals,
  ownAppealsGuard,
} from './appeal-queue.guard';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';

describe('appeal queue guard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const guard = () =>
    TestBed.runInInjectionContext(() => appealQueueGuard({} as never, {} as never));
  const home = () => TestBed.runInInjectionContext(() => appealsHomeRedirect({} as never));
  const own = () => TestBed.runInInjectionContext(() => ownAppealsGuard({} as never, {} as never));

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
    ['VERIFICATION_DECIDE'],
    ['verification:decide'],
    ['WORK_READ', 'VERIFICATION_DECIDE'],
    ['FULL_ACCESS'],
  ]) {
    it(`sends ${JSON.stringify(permissions)} to the department queue`, () => {
      as(permissions);
      expect(guard()).toBeTrue();
      expect(String(home())).toBe(router.parseUrl('/appeals/department').toString());
      expect(String(own())).toBe(router.parseUrl('/appeals/department').toString());
    });
  }

  for (const permissions of [[], ['SUBMISSION_READ', 'WORK_READ'], ['VERIFICATION']]) {
    it(`sends ${JSON.stringify(permissions)} to their own appeals`, () => {
      as(permissions);
      expect(String(guard())).toBe(router.parseUrl('/appeals/mine').toString());
      expect(String(home())).toBe(router.parseUrl('/appeals/mine').toString());
      expect(own()).toBeTrue();
    });
  }

  it('treats a missing user as unable to decide', () => {
    expect(canDecideAppeals(null)).toBeFalse();
    expect(String(home())).toBe(router.parseUrl('/appeals/mine').toString());
  });

  it('protects both department routes and redirects /appeals', () => {
    for (const path of ['appeals/department', 'appeals/department/:id']) {
      expect(routes.find((r) => r.path === path)?.canActivate).toContain(appealQueueGuard);
    }
    expect(routes.find((r) => r.path === 'appeals')?.redirectTo).toBe(appealsHomeRedirect);
  });

  it('keeps the personal appeals for employees only', () => {
    expect(routes.find((r) => r.path === 'appeals/mine')?.canActivate).toContain(ownAppealsGuard);
  });
});
