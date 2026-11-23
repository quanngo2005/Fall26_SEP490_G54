import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canViewReconciliation, reconciliationGuard } from './reconciliation.guard';

describe('reconciliationGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => reconciliationGuard({} as never, {} as never));

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    });
  });

  for (const permissions of [['RECONCILIATION_READ'], ['reconciliation:read'], ['FULL_ACCESS']]) {
    it(`lets ${permissions[0]} in`, () => {
      as(permissions);

      expect(run()).toBeTrue();
      expect(canViewReconciliation(permissions)).toBeTrue();
    });
  }

  it('sends an employee, who only holds other permissions, to /home', () => {
    as(['WORK_READ', 'NOTIFICATION_READ']);

    const result = run();

    expect(result instanceof UrlTree).toBeTrue();
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/home');
  });

  it('sends a signed-out visitor away', () => {
    expect(run() instanceof UrlTree).toBeTrue();
    expect(canViewReconciliation(null)).toBeFalse();
    expect(canViewReconciliation(undefined)).toBeFalse();
  });
});
