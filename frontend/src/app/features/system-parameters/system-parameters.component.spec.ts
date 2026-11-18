import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { of, throwError } from 'rxjs';
import { EvaluationComparisonSetting, SystemParameter } from '../../core/system-parameter.models';
import { SystemParameterService } from '../../core/system-parameter.service';
import { SystemParametersComponent } from './system-parameters.component';

describe('SystemParametersComponent', () => {
  const parameters: SystemParameter[] = [
    {
      key: 'kpiLockTime',
      value: '23:59:59',
      min: null,
      max: null,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'TIME',
      group: 'DEADLINES',
    },
    {
      key: 'workOverdueEnabled',
      value: true,
      min: null,
      max: null,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'BOOLEAN',
      group: 'DEADLINES',
    },
    {
      key: 'manualApprovalHours',
      value: 24,
      min: 1,
      max: 168,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'INTEGER',
      group: 'DEADLINES',
    },
    {
      key: 'appealWindowDays',
      value: 5,
      min: 1,
      max: 30,
      locked: false,
      updatedAt: '2026-10-01T03:00:00Z',
      updatedBy: 'sa.admin',
      valueType: 'INTEGER',
      group: 'APPEALS',
    },
    {
      key: 'appealRejectedHours',
      value: 48,
      min: 1,
      max: 720,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'INTEGER',
      group: 'APPEALS',
    },
    {
      key: 'qaSampleRatePercent',
      value: 5,
      min: 0,
      max: 100,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'PERCENT',
      group: 'SCORING_QA',
    },
    {
      key: 'tierAQuotaPercent',
      value: 20,
      min: 1,
      max: 100,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'PERCENT',
      group: 'QUOTA_RANKING',
    },
    {
      key: 'importMaxFileMb',
      value: 5,
      min: 1,
      max: 50,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'INTEGER',
      group: 'IMPORT_HANDOVER',
    },
    {
      key: 'handoverThresholdVnd',
      value: 50000000,
      min: 1,
      max: null,
      locked: false,
      updatedAt: '2026-10-25T03:00:00Z',
      updatedBy: 'sa.admin',
      valueType: 'INTEGER',
      group: 'IMPORT_HANDOVER',
    },
    {
      key: 'importFormats',
      value: '.xlsx',
      min: null,
      max: null,
      locked: true,
      updatedAt: null,
      updatedBy: null,
      valueType: 'STRING',
      group: 'IMPORT_HANDOVER',
    },
    {
      key: 'perfAttentionDropPoints',
      value: 1.5,
      min: 0.1,
      max: 50,
      locked: false,
      updatedAt: null,
      updatedBy: null,
      valueType: 'DECIMAL',
      group: 'DASHBOARD_SECURITY',
    },
  ];
  let list: jasmine.Spy;
  let save: jasmine.Spy;
  let getComparison: jasmine.Spy;
  let saveComparison: jasmine.Spy;

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
    getComparison = jasmine.createSpy('getComparison').and.returnValue(
      of<EvaluationComparisonSetting>({
        baseline: 'PREVIOUS_PERIOD',
        lookbackPeriods: 3,
      }),
    );
    saveComparison = jasmine.createSpy('saveComparison').and.returnValue(
      of<EvaluationComparisonSetting>({
        baseline: 'AVERAGE_LAST_N',
        lookbackPeriods: 4,
      }),
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ id: 'a', fullName: 'Admin', roles: ['ADMIN'], permissions: [] }),
          },
        },
        {
          provide: SystemParameterService,
          useValue: { list, save, getComparison, saveComparison },
        },
      ],
    });
  });
  afterEach(() => localStorage.clear());

  it('GB910_FE06 loads all 6 parameter groups, editable time/decimal/boolean controls, and locked importFormats', () => {
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.systemParameters;
    const root: HTMLElement = fixture.nativeElement;
    expect(list).toHaveBeenCalledTimes(1);
    expect(getComparison).toHaveBeenCalledTimes(1);
    expect(root.querySelector('.breadcrumb')?.textContent).toBe(t.breadcrumb);
    expect(root.querySelector('h1')?.textContent).toBe(t.title);
    expect(root.querySelectorAll('.columns .card').length).toBe(6);

    const formatsInput: HTMLInputElement = root.querySelector('#param-importFormats')!;
    expect(formatsInput.readOnly).toBeTrue();

    const lockTimeInput: HTMLInputElement = root.querySelector('#param-kpiLockTime')!;
    expect(lockTimeInput.readOnly).toBeFalse();
    fixture.destroy();
  });

  it('GB910_FE07 validates integer, decimal, time and boolean edits and saves typed payload on confirm', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;

    type(fixture, 'param-kpiLockTime', '99:99:99');
    expect(component.canSave()).toBeFalse();
    expect(root.querySelector('#error-kpiLockTime')).not.toBeNull();

    type(fixture, 'param-kpiLockTime', '22:30:00');
    type(fixture, 'param-perfAttentionDropPoints', '2.75');

    const boolCheckbox: HTMLInputElement = root.querySelector('#param-workOverdueEnabled')!;
    boolCheckbox.checked = false;
    boolCheckbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(component.canSave()).toBeTrue();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true }));
    fixture.detectChanges();
    expect(component.confirming()).toBeTrue();

    component.confirmSave();
    expect(save).toHaveBeenCalledWith({
      changes: [
        { key: 'kpiLockTime', value: '22:30:00' },
        { key: 'workOverdueEnabled', value: false },
        { key: 'perfAttentionDropPoints', value: 2.75 },
      ],
    });
    expect(component.saved()).toBeTrue();
    fixture.destroy();
  });

  it('GB910_FE08 filters groups and searches parameters, and saves evaluation comparison baseline', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.activeGroup.set('QUOTA_RANKING');
    fixture.detectChanges();
    expect(component.visibleGroups().length).toBe(1);
    expect(component.visibleGroups()[0].id).toBe('QUOTA_RANKING');

    component.activeGroup.set('ALL');
    component.searchQuery.set('kpiLockTime');
    fixture.detectChanges();
    expect(component.visibleGroups().length).toBe(1);

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#comparison-baseline');
    select.value = 'AVERAGE_LAST_N';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    component.saveComparison();
    expect(saveComparison).toHaveBeenCalled();
    expect(component.comparisonSaved()).toBeTrue();
    fixture.destroy();
  });

  it('GB910_FE09 handles load failure with retry and 400 save failure preserving drafts', () => {
    list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const fixture = create();
    expect(fixture.componentInstance.error()).toBe('loadFailed');

    list.and.returnValue(of(parameters));
    (fixture.nativeElement.querySelector('[role="alert"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBeNull();

    save.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    type(fixture, 'param-appealWindowDays', '9');
    fixture.componentInstance.requestSave();
    fixture.componentInstance.confirmSave();
    fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBe('saveFailed');
    expect(fixture.componentInstance.dirty()).toBeTrue();
    fixture.destroy();
  });

  it('GB910_FE10 switches all labels, groups and tabs cleanly between Vietnamese and English', () => {
    const fixture = create();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    fixture.detectChanges();
    const t = i18n.localization.systemParameters;
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('h1')?.textContent).toBe(t.title);
    expect(root.querySelector('#group-DEADLINES')?.textContent).toBe(t.groups.DEADLINES);
    expect(root.textContent).not.toContain('systemParameters.');
    fixture.destroy();
  });
});
