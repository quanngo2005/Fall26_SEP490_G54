import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canViewPersonalEvaluation, personalEvaluationGuard } from './personal-evaluation.guard';

describe('personalEvaluationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (roles: string[], permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles, permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => personalEvaluationGuard({} as never, {} as never));

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

  it('allows employees with employee role and standard permissions', () => {
    as(['EMPLOYEE'], ['EVALUATION_READ', 'WORK_READ']);
    expect(run()).toBeTrue();
  });

  it('allows sales officers and tellers through', () => {
    as(['SALES_OFFICER'], ['WORK_READ']);
    expect(run()).toBeTrue();
    as(['TELLER'], ['WORK_READ']);
    expect(run()).toBeTrue();
  });

  it('redirects branch managers to /evaluations', () => {
    as(['BRANCH_MANAGER'], ['EVALUATION_READ', 'EVALUATION_FINALIZE']);
    expect(String(run())).toBe(router.parseUrl('/evaluations').toString());
  });

  it('redirects department managers to /evaluations', () => {
    as(['DEPARTMENT_MANAGER'], ['EVALUATION_READ', 'VERIFICATION_DECIDE']);
    expect(String(run())).toBe(router.parseUrl('/evaluations').toString());
  });

  it('redirects users holding manager finalize or verification decide permissions', () => {
    as(['EMPLOYEE'], ['EVALUATION_FINALIZE']);
    expect(String(run())).toBe(router.parseUrl('/evaluations').toString());

    as(['EMPLOYEE'], ['VERIFICATION_DECIDE']);
    expect(String(run())).toBe(router.parseUrl('/evaluations').toString());
  });

  it('redirects admins with FULL_ACCESS to /evaluations', () => {
    as(['ADMIN'], ['FULL_ACCESS']);
    expect(String(run())).toBe(router.parseUrl('/evaluations').toString());
  });

  it('evaluates helper function directly', () => {
    expect(canViewPersonalEvaluation(['EMPLOYEE'], ['EVALUATION_READ'])).toBeTrue();
    expect(canViewPersonalEvaluation(['BRANCH_MANAGER'], [])).toBeFalse();
    expect(canViewPersonalEvaluation([], ['FULL_ACCESS'])).toBeFalse();
  });

  it('protects the personal evaluation route in app.routes.ts', () => {
    const route = routes.find((r) => r.path === 'my-evaluation');
    expect(route?.canActivate).toContain(personalEvaluationGuard);
  });
});
