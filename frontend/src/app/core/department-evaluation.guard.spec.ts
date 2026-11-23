import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import {
  canViewDepartmentEvaluation,
  departmentEvaluationGuard,
} from './department-evaluation.guard';

describe('departmentEvaluationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (roles: string[], permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles, permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => departmentEvaluationGuard({} as never, {} as never));

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

  it('allows department managers', () => {
    as(['DEPT_MANAGER'], ['EVALUATION_READ', 'EVALUATION_SUBMIT']);
    expect(run()).toBeTrue();

    as(['DEPT_MANAGER'], ['EVALUATION_READ', 'EVALUATION_CALIBRATE']);
    expect(run()).toBeTrue();
  });

  it('rejects branch managers and redirects to /branch-performance/evaluations', () => {
    as(['BRANCH_MANAGER'], ['EVALUATION_READ', 'EVALUATION_FINALIZE']);
    expect(String(run())).toBe(router.parseUrl('/branch-performance/evaluations').toString());
  });

  it('rejects admins and redirects to /home', () => {
    as(['SYSTEM_ADMIN'], ['FULL_ACCESS']);
    expect(String(run())).toBe(router.parseUrl('/home').toString());
  });

  it('rejects regular employees and redirects to /home', () => {
    as(['EMPLOYEE'], ['EVALUATION_READ']);
    expect(String(run())).toBe(router.parseUrl('/home').toString());
  });

  it('evaluates helper function directly', () => {
    expect(canViewDepartmentEvaluation(['DEPT_MANAGER'], ['EVALUATION_SUBMIT'])).toBeTrue();
    expect(canViewDepartmentEvaluation(['BRANCH_MANAGER'], ['EVALUATION_FINALIZE'])).toBeFalse();
    expect(canViewDepartmentEvaluation(['SYSTEM_ADMIN'], ['FULL_ACCESS'])).toBeFalse();
    expect(canViewDepartmentEvaluation(['EMPLOYEE'], ['EVALUATION_READ'])).toBeFalse();
  });

  it('protects the /evaluations route in app.routes.ts', () => {
    const route = routes.find((r) => r.path === 'evaluations');
    expect(route?.canActivate).toContain(departmentEvaluationGuard);
  });
});
