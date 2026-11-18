import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../app.routes';
import { AuthenticatedUser } from './auth.models';
import { AuthService } from './auth.service';
import { canViewQaQueue, qaQueueGuard } from './qa-queue.guard';

describe('qaQueueGuard', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let router: Router;

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });
  const run = () => TestBed.runInInjectionContext(() => qaQueueGuard({} as never, {} as never));

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
    ['QUALITY_FLAG_READ'],
    ['quality_flag:read'],
    [' Quality-Flag-Read '],
    ['FULL_ACCESS'],
    ['WORK_READ', 'QUALITY_FLAG_READ'],
  ]) {
    it(`lets ${JSON.stringify(permissions)} through`, () => {
      as(permissions);
      expect(run()).toBeTrue();
    });
  }

  for (const permissions of [[], ['VERIFICATION_DECIDE'], ['QA_SAMPLE_READ'], ['QUALITY_FLAG']]) {
    it(`sends ${JSON.stringify(permissions)} to /home`, () => {
      as(permissions);
      expect(run().toString()).toBe(router.parseUrl('/home').toString());
    });
  }

  it('treats a missing user as unauthorised', () => {
    expect(canViewQaQueue(undefined)).toBeFalse();
    expect(run().toString()).toBe(router.parseUrl('/home').toString());
  });

  it('guards both QA queue routes', () => {
    for (const path of ['qa-queue', 'qa-queue/:kind/:id']) {
      const route = routes.find((r) => r.path === path);
      expect(route?.canActivate).toContain(qaQueueGuard);
    }
  });
});
