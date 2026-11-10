import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canCreateRiskEvents, canViewRiskEvents, riskEventGuard } from './risk-event.guard';

describe('riskEventGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () => TestBed.runInInjectionContext(() => riskEventGuard({} as never, {} as never));

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
    ['RISK_EVENT_READ'],
    ['risk_event:read'],
    [' Risk-Event-Read '],
    ['FULL_ACCESS'],
    ['WORK_READ', 'RISK_EVENT_READ'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [[], ['RISK_EVENT_CREATE'], ['QUALITY_FLAG_READ'], ['RISK_EVENT']]) {
    it(`sends ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(run().toString()).toBe(router.parseUrl('/home').toString());
    });
  }

  it('treats a missing user as unauthorised', () => {
    expect(canViewRiskEvents(undefined)).toBeFalse();
    expect(run().toString()).toBe(router.parseUrl('/home').toString());
  });

  it('tells who may record an event', () => {
    expect(canCreateRiskEvents(['risk_event:create'])).toBeTrue();
    expect(canCreateRiskEvents(['FULL_ACCESS'])).toBeTrue();
    expect(canCreateRiskEvents(['RISK_EVENT_READ'])).toBeFalse();
    expect(canCreateRiskEvents(null)).toBeFalse();
  });

  it('guards the risk event route', () => {
    const route = routes.find((r) => r.path === 'risk-events');
    expect(route?.canActivate).toContain(riskEventGuard);
  });
});
