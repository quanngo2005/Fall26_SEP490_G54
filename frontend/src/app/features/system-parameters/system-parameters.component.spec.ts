import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { of, throwError } from 'rxjs';
import { SystemParameter } from '../../core/system-parameter.models';
import { SystemParameterService } from '../../core/system-parameter.service';
import { SystemParametersComponent } from './system-parameters.component';

describe('SystemParametersComponent', () => {
  const parameters: SystemParameter[] = [
    {
      key: 'manualApprovalHours',
      value: 24,
      min: 1,
      max: 168,
      locked: false,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'importMaxFileMb',
      value: 5,
      min: 1,
      max: 10,
      locked: false,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'importFormats',
      value: 'csv,xlsx',
      min: null,
      max: null,
      locked: true,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'kpiLockTime',
      value: '23:59:59',
      min: null,
      max: null,
      locked: true,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'appealWindowDays',
      value: 5,
      min: 1,
      max: 30,
      locked: false,
      updatedAt: '2026-10-01T03:00:00Z',
      updatedBy: 'sa.admin',
    },
    {
      key: 'preCloseCheckDays',
      value: 3,
      min: 1,
      max: 30,
      locked: false,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'appealRejectedHours',
      value: 48,
      min: null,
      max: null,
      locked: true,
      updatedAt: null,
      updatedBy: null,
    },
    {
      key: 'handoverThresholdVnd',
      value: 50000000,
      min: 1,
      max: null,
      locked: false,
      updatedAt: '2026-10-25T03:00:00Z',
      updatedBy: 'sa.admin',
    },
  ];
  let list: jasmine.Spy;
  let save: jasmine.Spy;

  function create() {
    const fixture = TestBed.createComponent(SystemParametersComponent);
    fixture.detectChanges();
    return fixture;
  }
  function type(fixture: ReturnType<typeof create>, id: string, value: string) {
    const input: HTMLInputElement = fixture.nativeElement.querySelector(`#${id}`);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    return input;
  }

  beforeEach(() => {
    localStorage.clear();
    list = jasmine.createSpy('list').and.returnValue(of(parameters));
    save = jasmine.createSpy('save').and.callFake(() => of(parameters));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ id: 'a', fullName: 'Admin', roles: ['ADMIN'], permissions: [] }),
          },
        },
        { provide: SystemParameterService, useValue: { list, save } },
      ],
    });
  });
  afterEach(() => localStorage.clear());

  it('GB654_14 loads parameters on init and renders every group, every parameter and the locked fields as read-only', () => {
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.systemParameters;
    const root: HTMLElement = fixture.nativeElement;
    expect(list).toHaveBeenCalledTimes(1);
    expect(root.querySelector('.breadcrumb')?.textContent).toBe(t.breadcrumb);
    expect(root.querySelector('h1')?.textContent).toBe(t.title);
    expect(root.querySelectorAll('.card').length).toBe(4);
    expect(root.querySelectorAll('.prow').length).toBe(8);
    for (const key of ['importFormats', 'kpiLockTime', 'appealRejectedHours']) {
      const input: HTMLInputElement = root.querySelector(`#param-${key}`)!;
      expect(input.readOnly).withContext(key).toBeTrue();
    }
    fixture.destroy();
  });

  it('GB654_15 disables Save and Discard with no changes, enables them when editing, and blocks saving an out-of-range value', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const i18n = TestBed.inject(I18nService);
    const root: HTMLElement = fixture.nativeElement;
    expect(component.dirty()).toBeFalse();
    expect(root.querySelector<HTMLButtonElement>('.btn.primary')?.disabled).toBeTrue();
    expect(
      root.querySelector<HTMLButtonElement>('.title-actions .btn:not(.primary)')?.disabled,
    ).toBeTrue();

    type(fixture, 'param-appealWindowDays', '999');
    expect(component.dirty()).toBeTrue();
    expect(component.canSave()).toBeFalse();
    expect(root.querySelector('#error-appealWindowDays')).not.toBeNull();
    expect(root.querySelector('#error-appealWindowDays')?.textContent).toContain(
      i18n.translate('systemParameters.invalidRange', { min: 1, max: 30 }),
    );
    expect(root.querySelector<HTMLButtonElement>('.btn.primary')?.disabled).toBeTrue();
    component.requestSave();
    expect(component.confirming()).toBeFalse();

    type(fixture, 'param-appealWindowDays', '7');
    expect(component.canSave()).toBeTrue();
    expect(root.querySelector('.dot')).not.toBeNull();
    expect(root.querySelector<HTMLButtonElement>('.btn.primary')?.disabled).toBeFalse();
    expect(
      root.querySelector<HTMLButtonElement>('.title-actions .btn:not(.primary)')?.disabled,
    ).toBeFalse();
    expect(root.querySelector('.banner')?.textContent).toContain(
      i18n.translate('systemParameters.unsavedTitle', { count: 1 }),
    );
    fixture.destroy();
  });

  it('GB654_15 opens the confirm dialog on Ctrl+S with the old-to-new diff', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const t = TestBed.inject(I18nService).localization.systemParameters;
    type(fixture, 'param-appealWindowDays', '7');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true }));
    fixture.detectChanges();
    expect(component.confirming()).toBeTrue();
    const dialog: HTMLElement = fixture.nativeElement.querySelector('[role="dialog"]');
    expect(dialog.textContent).toContain(t.confirmTitle);
    expect(dialog.textContent).toContain(component.valueText(5, 'appealWindowDays'));
    expect(dialog.textContent).toContain(component.valueText(7, 'appealWindowDays'));
    fixture.destroy();
  });

  it('GB654_15 closes the confirm dialog without saving when Cancel is clicked', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const i18n = TestBed.inject(I18nService);
    type(fixture, 'param-appealWindowDays', '7');
    component.requestSave();
    fixture.detectChanges();
    const dialog: HTMLElement = fixture.nativeElement.querySelector('[role="dialog"]');
    const cancelButton = dialog.querySelector<HTMLButtonElement>('.buttons .secondary')!;
    expect(cancelButton.textContent?.trim()).toBe(i18n.localization.sidebarSettings.confirmBack);
    cancelButton.click();
    fixture.detectChanges();
    expect(component.confirming()).toBeFalse();
    expect(save).not.toHaveBeenCalled();
    expect(component.dirty()).toBeTrue();
    fixture.destroy();
  });

  it('GB654_15 saves only the changed keys after confirmation', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    type(fixture, 'param-appealWindowDays', '7');
    component.requestSave();
    fixture.detectChanges();
    expect(save).not.toHaveBeenCalled();
    component.confirmSave();
    expect(save).toHaveBeenCalledWith({ changes: [{ key: 'appealWindowDays', value: 7 }] });
    expect(component.saved()).toBeTrue();
    expect(component.dirty()).toBeFalse();
    fixture.destroy();
  });

  it('GB654_15 restores the original values on Discard', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const locked: HTMLInputElement = fixture.nativeElement.querySelector('#param-kpiLockTime');
    expect(locked.readOnly).toBeTrue();
    const input = type(fixture, 'param-handoverThresholdVnd', '80000000');
    expect(input.value).toMatch(/80[.,]000[.,]000/);
    expect(component.diffs()).toEqual([
      { key: 'handoverThresholdVnd', oldValue: 50000000, newValue: 80000000 },
    ]);
    component.discard();
    fixture.detectChanges();
    expect(component.dirty()).toBeFalse();
    expect(
      (fixture.nativeElement.querySelector('#param-handoverThresholdVnd') as HTMLInputElement)
        .value,
    ).toMatch(/50[.,]000[.,]000/);
    fixture.destroy();
  });

  it('GB654_16 shows an error state with a retry button that calls list again', () => {
    list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.systemParameters;
    expect(fixture.componentInstance.error()).toBe('loadFailed');
    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert.textContent).toContain(t.loadFailed);
    list.and.returnValue(of(parameters));
    (alert.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(list).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.error()).toBeNull();
    fixture.destroy();
  });

  it('GB654_16 keeps the drafts and shows the save-failed message on a 400 VALIDATION_ERROR response', () => {
    save.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              success: false,
              errorCode: 'VALIDATION_ERROR',
              message: 'Invalid value',
            },
          }),
      ),
    );
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.systemParameters;
    const component = fixture.componentInstance;
    type(fixture, 'param-appealWindowDays', '7');
    component.requestSave();
    component.confirmSave();
    fixture.detectChanges();
    expect(component.error()).toBe('saveFailed');
    expect(component.dirty()).toBeTrue();
    expect(component.drafts()['appealWindowDays']).toBe('7');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      t.saveFailed,
    );
    fixture.destroy();
  });

  it('GB654_16 links to the audit log filtered by SystemParameter', () => {
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.systemParameters;
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('.btn-link');
    expect(link.textContent).toContain(t.auditLogLink);
    expect(link.getAttribute('href')).toContain('/audit-logs');
    expect(link.getAttribute('href')).toContain('entityName=SystemParameter');
    fixture.destroy();
  });

  it('GB654_17 switches the title, buttons and group titles to English when the language changes', () => {
    const fixture = create();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    fixture.detectChanges();
    const t = i18n.localization.systemParameters;
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('h1')?.textContent).toBe(t.title);
    expect(root.querySelector('.breadcrumb')?.textContent).toBe(t.breadcrumb);
    expect(root.querySelector<HTMLButtonElement>('.btn.primary')?.textContent?.trim()).toBe(t.save);
    expect(
      root
        .querySelector<HTMLButtonElement>('.title-actions .btn:not(.primary)')
        ?.textContent?.trim(),
    ).toBe(t.discard);
    expect(root.querySelector('#group-manualApproval')?.textContent).toBe(t.groups.manualApproval);
    expect(root.textContent).not.toContain('systemParameters.');
    fixture.destroy();
  });
});
