import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { myCampaignsGuard } from './my-campaigns.guard';

describe('myCampaignsGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () => TestBed.runInInjectionContext(() => myCampaignsGuard({} as never, {} as never));

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
    ['CAMPAIGN_CONTACT'],
    ['WORK_READ', 'CAMPAIGN_CONTACT'],
    ['FULL_ACCESS'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [[], ['CAMPAIGN_READ', 'CAMPAIGN_MANAGE'], ['WORK_READ']]) {
    it(`redirects ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(String(run())).toBe(router.parseUrl('/home').toString());
    });
  }

  it('redirects a missing user to /home', () => {
    expect(String(run())).toBe(router.parseUrl('/home').toString());
  });
});
