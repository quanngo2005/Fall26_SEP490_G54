import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { permissionGuard } from './permission.guard';

describe('permissionGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = (...codes: string[]) =>
    TestBed.runInInjectionContext(() => permissionGuard(...codes)({} as never, {} as never));

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    });
  });

  for (const permissions of [['CAMPAIGN_READ'], ['campaign:read'], ['FULL_ACCESS']]) {
    it(`lets ${permissions[0]} in`, () => {
      as(permissions);
      expect(run('CAMPAIGN_READ')).toBeTrue();
    });
  }

  it('accepts any one of several permissions', () => {
    as(['WORK_READ']);
    expect(run('CAMPAIGN_READ', 'WORK_READ')).toBeTrue();
  });

  it('sends a user without the permission to /home', () => {
    as(['WORK_READ', 'NOTIFICATION_READ']);
    const result = run('CAMPAIGN_READ');
    expect(result instanceof UrlTree).toBeTrue();
    expect(String(result)).toBe(TestBed.inject(Router).parseUrl('/home').toString());
  });

  it('sends an anonymous user to /home', () => {
    expect(run('CAMPAIGN_READ') instanceof UrlTree).toBeTrue();
  });
});
