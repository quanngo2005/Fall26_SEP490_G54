import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { employeeReadGuard, EMPLOYEE_READ_PERMISSION } from './employee-read.guard';

describe('employeeReadGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[], roles: string[] = []) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles, permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => employeeReadGuard({} as never, {} as never));
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
    [EMPLOYEE_READ_PERMISSION],
    ['employee:read'],
    ['employee.read'],
    ['employee-read'],
    [' Employee_Read '],
    ['FULL_ACCESS'],
    ['full_access'],
    ['WORK_READ', 'KPI_READ', 'EMPLOYEE_READ'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [
    [],
    ['WORK_READ', 'KPI_READ', 'EVALUATION_READ', 'ORGANIZATION_READ'],
    ['EMPLOYEE_MANAGE'],
    ['EMPLOYEE'],
    ['READ'],
    ['OTHER_PERMISSION'],
  ]) {
    it(`redirects ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      const result = run();
      expect(result).not.toBeTrue();
      expect(result.toString()).toBe(home());
    });
  }

  it('redirects unauthenticated users to /home', () => {
    user.set(null);
    const result = run();
    expect(result).not.toBeTrue();
    expect(result.toString()).toBe(home());
  });
});
