import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import {
  canManageServiceRatingImport,
  canManageTransactionImport,
  transactionImportGuard,
} from './transaction-import.guard';

describe('transactionImportGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () =>
    TestBed.runInInjectionContext(() => transactionImportGuard({} as never, {} as never));

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    });
  });

  for (const permissions of [
    ['TRANSACTION_IMPORT_MANAGE'],
    ['transaction-import:manage'],
    ['FULL_ACCESS'],
  ]) {
    it(`lets ${permissions[0]} in`, () => {
      as(permissions);

      expect(run()).toBeTrue();
      expect(canManageTransactionImport(permissions)).toBeTrue();
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
    expect(canManageTransactionImport(null)).toBeFalse();
    expect(canManageTransactionImport(undefined)).toBeFalse();
  });

  it('allows the service rating import only with its own permission or full access', () => {
    expect(canManageServiceRatingImport(['SERVICE_RATING_IMPORT_MANAGE'])).toBeTrue();
    expect(canManageServiceRatingImport(['service_rating_import:manage'])).toBeTrue();
    expect(canManageServiceRatingImport(['FULL_ACCESS'])).toBeTrue();
    expect(canManageServiceRatingImport(['TRANSACTION_IMPORT_MANAGE'])).toBeFalse();
    expect(canManageServiceRatingImport(null)).toBeFalse();
  });
});
