import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { branchEvaluationGuard, canViewBranchEvaluations } from './branch-evaluation.guard';

describe('branchEvaluationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => branchEvaluationGuard({} as never, {} as never));

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
    ['EVALUATION_FINALIZE'],
    ['evaluation:finalize'],
    ['WORK_READ', 'EVALUATION_FINALIZE'],
    ['FULL_ACCESS'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [[], ['EVALUATION_READ', 'EVALUATION_SUBMIT'], ['EVALUATION']]) {
    it(`redirects ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(String(run())).toBe(router.parseUrl('/home').toString());
    });
  }

  it('treats a missing user as unauthorised', () => {
    expect(canViewBranchEvaluations(null)).toBeFalse();
    expect(String(run())).toBe(router.parseUrl('/home').toString());
  });

  it('protects the branch evaluation route', () => {
    const route = routes.find((r) => r.path === 'branch-performance/evaluations');
    expect(route?.canActivate).toContain(branchEvaluationGuard);
  });
});
