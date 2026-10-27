import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { NavigationService } from '../shared/layout/navigation.service';
import { AuthService } from './auth.service';
import { AuthenticatedUser } from './auth.models';
import { SidebarConfiguration, SidebarConfigurationService } from './sidebar-configuration.service';
import { adminGuard } from './admin.guard';

describe('Configured sidebar navigation', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let current: jasmine.Spy;
  beforeEach(() => {
    user.set({
      id: 'employee',
      email: 'employee@example.test',
      fullName: 'Employee',
      roles: ['EMPLOYEE'],
      permissions: [],
    });
    current = jasmine.createSpy('current').and.returnValue(
      of([
        {
          role: 'employee',
          itemIds: [
            'myProfile',
            'overview',
            'kpiDefinitions',
            'organization',
            'jobPositions',
            'departments',
          ],
          version: 3,
        },
      ]),
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
        { provide: SidebarConfigurationService, useValue: { current } },
      ],
    });
  });
  it('uses saved order and child selection while preserving permission filtering', () => {
    const navigation = TestBed.inject(NavigationService);
    TestBed.tick();
    expect(navigation.items().map((item) => item.id)).toEqual([
      'myProfile',
      'overview',
      'organization',
    ]);
    expect(navigation.items()[2].children?.map((item) => item.id)).toEqual([
      'jobPositions',
      'departments',
    ]);
    user.update((value) => value && { ...value, permissions: ['KPI_MANAGE'] });
    TestBed.tick();
    expect(navigation.items().map((item) => item.id)).toContain('kpiDefinitions');
  });
  it('shows errors without fallback menus and clears state on logout', () => {
    current.and.returnValue(throwError(() => new Error('Unavailable')));
    const navigation = TestBed.inject(NavigationService);
    TestBed.tick();
    expect(navigation.loadFailed()).toBeTrue();
    expect(navigation.items()).toEqual([]);
    user.set(null);
    TestBed.tick();
    expect(navigation.configurations()).toEqual([]);
    expect(navigation.loading()).toBeFalse();
  });
  it('applies role URL overrides to links and children without granting menu permissions', () => {
    current.and.returnValue(
      of([
        {
          role: 'employee',
          itemIds: ['overview', 'myProfile', 'organization', 'departments', 'kpiDefinitions'],
          version: 1,
          routes: {
            myProfile: '/work-type-detail',
            departments: '/employee-detail',
            kpiDefinitions: '/home',
          },
        },
      ]),
    );
    const navigation = TestBed.inject(NavigationService);
    TestBed.tick();
    expect(navigation.items().find((item) => item.id === 'myProfile')?.route).toBe(
      '/work-type-detail',
    );
    expect(navigation.items().find((item) => item.id === 'organization')?.children?.[0].route).toBe(
      '/employee-detail',
    );
    expect(navigation.items().some((item) => item.id === 'kpiDefinitions')).toBeFalse();
  });
  it('links Department performance to its own route, separate from the department list', () => {
    current.and.returnValue(
      of([
        {
          role: 'employee',
          itemIds: ['overview', 'organization', 'departments', 'departmentPerformance'],
          version: 1,
        },
      ]),
    );
    const navigation = TestBed.inject(NavigationService);
    TestBed.tick();

    const items = navigation.items();
    expect(items.find((item) => item.id === 'departmentPerformance')?.route).toBe(
      '/department-performance',
    );
    expect(items.find((item) => item.id === 'organization')?.children?.[0].route).toBe(
      '/departments',
    );
    expect(navigation.routeOptions).toContain('/department-performance');
    expect(navigation.routeOptions).toContain('/departments');
  });
  it('cancels an in-flight reload when the account signs out', () => {
    const response = new Subject<SidebarConfiguration[]>();
    current.and.returnValue(response);
    const navigation = TestBed.inject(NavigationService);
    TestBed.tick();
    user.set(null);
    TestBed.tick();
    response.next([{ role: 'admin', itemIds: ['overview', 'systemConfiguration'], version: 1 }]);
    expect(navigation.configurations()).toEqual([]);
    expect(navigation.items()).toEqual([]);
  });
  it('allows the editor route only for admin role aliases', () => {
    const router = TestBed.inject(Router);
    const denied = TestBed.runInInjectionContext(() => adminGuard({} as never, {} as never));
    expect(denied?.toString()).toBe(router.parseUrl('/home').toString());
    user.update((value) => value && { ...value, roles: ['SYSTEM_ADMIN'] });
    expect(TestBed.runInInjectionContext(() => adminGuard({} as never, {} as never))).toBeTrue();
  });
});
