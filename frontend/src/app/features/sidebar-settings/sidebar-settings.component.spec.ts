import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  SidebarConfiguration,
  SidebarConfigurationService,
} from '../../core/sidebar-configuration.service';
import { NavigationService } from '../../shared/layout/navigation.service';
import { SidebarSettingsComponent } from './sidebar-settings.component';

describe('SidebarSettingsComponent', () => {
  const configs: SidebarConfiguration[] = [
    { role: 'employee', itemIds: ['overview', 'myProfile'], version: 1 },
    { role: 'admin', itemIds: ['overview', 'systemConfiguration'], version: 0 },
  ];
  let save: jasmine.Spy;
  beforeEach(() => {
    localStorage.clear();
    save = jasmine
      .createSpy('save')
      .and.callFake((config: SidebarConfiguration) =>
        of({ ...config, version: config.version + 1 }),
      );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({
              id: 'admin',
              fullName: 'Admin',
              roles: ['ADMIN'],
              permissions: [],
            }),
          },
        },
        {
          provide: SidebarConfigurationService,
          useValue: { all: () => of(configs), current: () => of([configs[1]]), save },
        },
      ],
    });
  });
  afterEach(() => localStorage.clear());
  it('edits and saves a role without replacing the admin sidebar with the preview', () => {
    const fixture = TestBed.createComponent(SidebarSettingsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.toggle('organization');
    component.toggle('departments');
    component.move('myProfile', -1);
    expect(
      component
        .preview()
        .find((item) => item.id === 'organization')
        ?.children?.map((item) => item.id),
    ).toEqual(['departments']);
    expect(TestBed.inject(NavigationService).roles()).toEqual(['admin']);
    component.save();
    expect(save).toHaveBeenCalledWith({
      role: 'employee',
      itemIds: ['myProfile', 'overview', 'organization', 'departments'],
      version: 1,
      routes: {},
    });
    expect(component.saved()).toBeTrue();
    expect(component.dirty()).toBeFalse();
    fixture.destroy();
  });
  it('preserves the draft after conflict and translates the rendered error live', () => {
    save.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const fixture = TestBed.createComponent(SidebarSettingsComponent);
    const component = fixture.componentInstance;
    component.toggle('notifications');
    component.save();
    fixture.detectChanges();
    expect(component.dirty()).toBeTrue();
    expect(component.saved()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Cấu hình đã được thay đổi');
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Someone else changed this configuration');
    expect(fixture.nativeElement.textContent).toContain('Role sidebar management');
    fixture.destroy();
  });
  it('reorders through drop and keyboard, saves route overrides and resets them on cancel', () => {
    const fixture = TestBed.createComponent(SidebarSettingsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    fixture.debugElement
      .query(By.directive(CdkDropList))
      .triggerEventHandler('cdkDropListDropped', { previousIndex: 1, currentIndex: 0 });
    fixture.detectChanges();
    expect(component.draft()).toEqual(['myProfile', 'overview']);
    const handle: HTMLButtonElement = fixture.nativeElement.querySelector('.drag-handle');
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    expect(component.draft()).toEqual(['overview', 'myProfile']);
    const route: HTMLSelectElement = fixture.nativeElement.querySelector('#route-myProfile');
    route.value = '/kpi-definitions';
    route.dispatchEvent(new Event('change'));
    expect(component.preview()[1].route).toBe('/kpi-definitions');
    component.save();
    expect(save).toHaveBeenCalledWith({
      role: 'employee',
      itemIds: ['overview', 'myProfile'],
      version: 1,
      routes: { myProfile: '/kpi-definitions' },
    });
    route.value = '';
    route.dispatchEvent(new Event('change'));
    expect(component.preview()[1].route).toBe('');
    component.cancel();
    expect(component.routeFor('myProfile')).toBe('/kpi-definitions');
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(handle.getAttribute('aria-label')).toBe('Drag to reorder menu item');
    expect(route.getAttribute('aria-label')).toBe('Application URL');
    const offered = Array.from<HTMLOptionElement>(route.querySelectorAll('option')).map(
      (option) => option.value,
    );
    expect(offered).toEqual([
      '',
      ...TestBed.inject(NavigationService).routeOptions.filter(
        (route) => route !== '/sidebar-settings',
      ),
    ]);
    route.value = '';
    route.dispatchEvent(new Event('change'));
    expect(component.routeFor('myProfile')).toBe('');
    fixture.destroy();
  });

  it('removes an item with its delete button and keeps required items undeletable', () => {
    const fixture = TestBed.createComponent(SidebarSettingsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const buttons = () =>
      fixture.debugElement.queryAll(By.css('.order-list .remove-item')).map((b) => b.nativeElement);
    expect(component.draft()).toEqual(['overview', 'myProfile']);
    expect(buttons().length).toBe(1);
    buttons()[0].click();
    fixture.detectChanges();
    expect(component.draft()).toEqual(['overview']);
    expect(buttons().length).toBe(0);
    fixture.destroy();
  });
});
