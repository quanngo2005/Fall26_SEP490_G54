import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { PagedResult } from '../../core/paged-result.model';
import { KpiAllocation, TargetSetup, TargetSetupStep } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { TargetSetupApiService } from './target-setup-api.service';
import { TargetSetupWizardComponent } from './target-setup-wizard.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('TargetSetupWizardComponent', () => {
  const branch: DepartmentSummary = {
    id: 'br1',
    code: 'CN01',
    name: 'Chi nhánh 1',
    levelType: 'BRANCH',
    status: 'ACTIVE',
    parentDepartmentId: null,
    parentDepartmentName: null,
    employeeCount: 10,
    childDepartmentCount: 2,
    managerName: null,
    tellerCount: 5,
    salesCount: 3,
    lastSyncAt: '2026-11-03T08:00:00Z',
  };

  const page = (items: DepartmentSummary[]): PagedResult<DepartmentSummary> => ({
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 100,
  });

  const setupState = (step: TargetSetupStep, status = 'DRAFT'): TargetSetup => ({
    branchYearId: step === 'IMPORT' ? null : 'by-1',
    hasBranchYear: step !== 'IMPORT',
    importedAt: null,
    rowCount: 3,
    step,
    status,
    quarters: { done: 0, total: 3 },
    allocation: { done: 0, total: 3 },
    canPublish: false,
    blockers: [],
  });

  interface RenderOptions {
    step?: TargetSetupStep;
    fail?: boolean;
    canPublish?: boolean;
    publish?: 'ok' | 'blocked';
    departments?: DepartmentSummary[];
    queryParams?: Record<string, string>;
    status?: string;
    /** Spy on `Router.navigate` before the component is created, to capture a navigate call made on init. */
    captureNavigate?: boolean;
    allAllocations?: KpiAllocation[];
  }

  let api: jasmine.SpyObj<TargetAssignmentService>;
  let navigateSpy: jasmine.Spy | undefined;

  function render(options: RenderOptions = {}): ComponentFixture<TargetSetupWizardComponent> {
    const targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getSetup',
      'getQuarterSplit',
      'getKpiAllocation',
      'getAllAllocations',
      'saveKpiAllocation',
      'publish',
    ]);
    api = targetApi;
    const departmentApi = jasmine.createSpyObj<DepartmentService>('DepartmentService', ['list']);
    departmentApi.list.and.returnValue(of(page(options.departments ?? [branch])));
    targetApi.getSetup.and.returnValue(
      options.fail
        ? throwError(() => new Error('boom'))
        : of({
            ...setupState(options.step ?? 'IMPORT', options.status),
            canPublish: options.canPublish ?? false,
          }),
    );
    targetApi.getKpiAllocation.and.returnValue(throwError(() => new Error('not needed')));
    targetApi.getAllAllocations.and.returnValue(of(options.allAllocations ?? []));
    targetApi.publish.and.returnValue(
      options.publish === 'blocked'
        ? throwError(
            () =>
              new HttpErrorResponse({
                status: 422,
                error: { success: false, errorCode: 'TARGET_PUBLISH_BLOCKED' },
              }),
          )
        : of({
            publishedAt: '2026-11-03T10:00:00Z',
            branchYearCount: 3,
            quarterCount: 12,
            departmentYearCount: 6,
            departmentQuarterCount: 24,
          }),
    );
    targetApi.getQuarterSplit.and.returnValue(
      of({
        branchYearId: 'by-1',
        year: 2026,
        status: 'DRAFT',
        importedAt: null,
        fileRatios: null,
        quarters: [1, 2, 3, 4].map((quarter) => ({
          periodId: `p${quarter}`,
          quarter,
          locked: false,
        })),
        rows: [
          {
            kpiId: 'k1',
            kpiCode: 'KPI_1',
            kpiName: 'KPI 1',
            unit: 'VND',
            groupCode: 'G',
            groupName: 'Group',
            aggregationType: 'SUM',
            isRatio: false,
            yearValue: 100,
            q1: 25,
            q2: 25,
            q3: 25,
            q4: 25,
            previousYearValue: null,
            previousYear: null,
            previousYearActual: null,
            sum: 100,
            diff: 0,
            matched: true,
          },
        ],
      }),
    );

    const suggestApi = jasmine.createSpyObj<TargetSetupApiService>('TargetSetupApiService', [
      'suggest',
    ]);

    TestBed.configureTestingModule({
      providers: [
        { provide: DepartmentService, useValue: departmentApi },
        { provide: TargetAssignmentService, useValue: targetApi },
        { provide: TargetSetupApiService, useValue: suggestApi },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ year: '2026' })),
            snapshot: { queryParamMap: convertToParamMap(options.queryParams ?? {}) },
          },
        },
      ],
    }).overrideComponent(TargetSetupWizardComponent, {
      remove: { imports: [HeaderComponent, SidebarComponent] },
      add: { imports: [HeaderStub, SidebarStub] },
    });

    navigateSpy = undefined;
    if (options.captureNavigate) {
      navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    }

    const fixture = TestBed.createComponent(TargetSetupWizardComponent);
    TestBed.inject(I18nService).setLanguage('vi');
    fixture.detectChanges();
    return fixture;
  }

  const stepButtons = (fixture: ComponentFixture<TargetSetupWizardComponent>) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.wizard-step'),
    );
  const bodyTitle = (fixture: ComponentFixture<TargetSetupWizardComponent>) =>
    (fixture.nativeElement as HTMLElement).querySelector('.wizard-body h2')?.textContent?.trim();

  const cases: Array<{ step: TargetSetupStep; current: number; done: number[] }> = [
    { step: 'IMPORT', current: 1, done: [] },
    { step: 'QUARTER', current: 2, done: [1] },
    { step: 'ALLOCATE', current: 3, done: [1, 2] },
    { step: 'PUBLISH', current: 4, done: [1, 2, 3] },
    { step: 'PUBLISHED', current: 4, done: [1, 2, 3, 4] },
  ];

  for (const { step, current, done } of cases) {
    it(`maps step ${step} to the ${current}-step wizard state`, () => {
      const fixture = render({ step });
      const i18n = TestBed.inject(I18nService);
      const text = i18n.localization.targetSetup;
      const labels = [text.stepImport, text.stepQuarter, text.stepAllocate, text.stepPublish];
      const buttons = stepButtons(fixture);

      expect(buttons.length).toBe(4);
      buttons.forEach((button, index) => {
        const number = index + 1;
        const isDone = done.includes(number);
        expect(button.classList.contains('is-done')).toBe(isDone);
        expect(button.classList.contains('is-current')).toBe(!isDone && number === current);
        expect(button.disabled).toBe(!isDone && number !== current);
        expect(button.textContent?.trim()).toBe(`${isDone ? '✓ ' : ''}${labels[index]}`);
      });
      expect(bodyTitle(fixture)).toBe(labels[current - 1]);
    });
  }

  it('renders the localized breadcrumb and the year in the title', () => {
    const fixture = render();
    const i18n = TestBed.inject(I18nService);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.breadcrumb')?.textContent?.trim()).toBe(
      i18n.localization.targetSetup.breadcrumb,
    );
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Phân bổ mục tiêu năm 2026');
  });

  it('shows the error state when getSetup fails', () => {
    const fixture = render({ fail: true });
    const i18n = TestBed.inject(I18nService);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.state-error')?.textContent).toContain(
      i18n.localization.targetSetup.loadFailed,
    );
    expect(element.querySelector('.wizard-stepper')).toBeNull();
    expect(element.querySelector('.wizard-body')).toBeNull();
  });

  it('shows the error state when the branch department is missing', () => {
    const fixture = render({ departments: [] });

    expect(fixture.nativeElement.querySelector('.state-error')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.wizard-step')).toBeNull();
  });

  it('lets a done step be opened while future steps stay disabled', () => {
    const fixture = render({ step: 'ALLOCATE' });
    const i18n = TestBed.inject(I18nService);
    const buttons = stepButtons(fixture);

    buttons[0].click();
    fixture.detectChanges();
    expect(bodyTitle(fixture)).toBe(i18n.localization.targetSetup.stepImport);

    // Step 4 is still in the future: disabled, so clicking it keeps the body on step 1.
    expect(buttons[3].disabled).toBeTrue();
    buttons[3].click();
    fixture.detectChanges();
    expect(bodyTitle(fixture)).toBe(i18n.localization.targetSetup.stepImport);
  });

  it('switches the step labels with the language', () => {
    const fixture = render({ step: 'QUARTER' });
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    try {
      fixture.detectChanges();

      expect(stepButtons(fixture).map((button) => button.textContent?.trim())).toEqual([
        '✓ 1. Enter yearly targets',
        '2. Split by quarter',
        '3. Allocate to departments',
        '4. Publish',
      ]);
    } finally {
      i18n.setLanguage('vi');
      localStorage.clear();
    }
  });

  it('opens step 2 and asks it to suggest when ?step=2&suggest=1 and the year is DRAFT', () => {
    const fixture = render({ step: 'ALLOCATE', queryParams: { step: '2', suggest: '1' } });
    const element = fixture.nativeElement as HTMLElement;

    // ALLOCATE maps to step 3 by default, but the query param should still open step 2.
    expect(bodyTitle(fixture)).toBe(
      TestBed.inject(I18nService).localization.targetSetup.stepQuarter,
    );
    const suggestInput = element.querySelector('app-branch-quarter-split');
    expect(suggestInput).not.toBeNull();
  });

  it('clears ?step/&suggest from the URL after applying it once', () => {
    render({ step: 'QUARTER', queryParams: { step: '2', suggest: '1' }, captureNavigate: true });

    expect(navigateSpy).toHaveBeenCalledWith(['/target-setup', 2026], { replaceUrl: true });
  });

  it('ignores ?step=2 when the setup is no longer DRAFT (not clickable)', () => {
    const fixture = render({
      step: 'PUBLISHED',
      status: 'PUBLISHED',
      queryParams: { step: '2', suggest: '1' },
    });

    // PUBLISHED stays on its own mapped step (4), the query param is ignored.
    expect(bodyTitle(fixture)).toBe(
      TestBed.inject(I18nService).localization.targetSetup.stepPublish,
    );
  });

  it('renders step 2 with the quarter split and moves to step 3 on Phân bổ', () => {
    const fixture = render({ step: 'QUARTER' });
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-branch-quarter-split')).not.toBeNull();

    const allocate = element.querySelector<HTMLButtonElement>('.allocate-button');
    expect(allocate?.disabled).toBeFalse();
    allocate?.click();
    fixture.detectChanges();

    expect(bodyTitle(fixture)).toBe(
      TestBed.inject(I18nService).localization.targetSetup.stepAllocate,
    );
  });
  it('shows the per-KPI view by default and switches to the matrix component', () => {
    const fixture = render({ step: 'ALLOCATE' });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-department-allocation')).not.toBeNull();
    expect(element.querySelector('app-department-allocation-matrix')).toBeNull();
    const tabs = Array.from(element.querySelectorAll<HTMLButtonElement>('.view-switch-tab'));
    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual(['Theo chỉ tiêu', 'Ma trận']);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');

    tabs[1].click();
    fixture.detectChanges();

    expect(element.querySelector('app-department-allocation')).toBeNull();
    expect(element.querySelector('app-department-allocation-matrix')).not.toBeNull();
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(api.getAllAllocations).toHaveBeenCalledWith('by-1');
  });

  it('asks to confirm before switching views when the matrix has unsaved edits, and discards on confirm', () => {
    const allocation: KpiAllocation = {
      kpiId: 'k1',
      kpiName: 'KPI 1',
      unit: 'VND',
      workType: 'Teller',
      branch: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
      departments: [
        {
          departmentId: 'd1',
          departmentName: 'Phòng Giao dịch',
          headcount: 3,
          year: 100,
          q1: 25,
          q2: 25,
          q3: 25,
          q4: 25,
          previousYear: null,
          previousYearActual: null,
          matched: true,
        },
      ],
      totals: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
      remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
      hiddenDepartments: [],
    };
    const fixture = render({ step: 'ALLOCATE', allAllocations: [allocation] });
    const element = fixture.nativeElement as HTMLElement;
    const tabs = Array.from(element.querySelectorAll<HTMLButtonElement>('.view-switch-tab'));

    tabs[1].click();
    fixture.detectChanges();
    const input = element.querySelector<HTMLInputElement>('.matrix-table tbody input')!;
    input.dispatchEvent(new Event('focus'));
    input.value = '400';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    tabs[0].click();
    fixture.detectChanges();

    // Still on the matrix: the confirm dialog is up instead of switching right away.
    expect(element.querySelector('app-department-allocation-matrix')).not.toBeNull();
    expect(element.querySelector('.confirm-dialog')).not.toBeNull();

    const confirm = Array.from(
      element.querySelectorAll<HTMLButtonElement>('.confirm-actions button'),
    ).find((button) => button.textContent?.trim() === 'Chuyển cách xem')!;
    confirm.click();
    fixture.detectChanges();

    expect(element.querySelector('app-department-allocation-matrix')).toBeNull();
    expect(element.querySelector('app-department-allocation')).not.toBeNull();
  });

  function clickText(fixture: ComponentFixture<TargetSetupWizardComponent>, label: string): void {
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((item) => item.textContent?.trim() === label);
    if (!button) {
      throw new Error(`Button "${label}" not found`);
    }
    button.click();
    fixture.detectChanges();
  }

  const publishAllocation: KpiAllocation = {
    kpiId: 'k1',
    kpiName: 'KPI 1',
    unit: 'VND',
    workType: 'Teller',
    branch: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
    departments: [
      {
        departmentId: 'd1',
        departmentName: 'Phòng Giao dịch',
        headcount: 3,
        year: 100,
        q1: 25,
        q2: 25,
        q3: 25,
        q4: 25,
        previousYear: 80,
        previousYearActual: null,
        matched: true,
      },
    ],
    totals: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
    remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
    hiddenDepartments: [],
  };

  it('keeps "Phát hành cho N phòng" disabled until every target matches', () => {
    const fixture = render({
      step: 'PUBLISH',
      canPublish: false,
      allAllocations: [publishAllocation],
    });
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.publish-footer .button-primary',
    )!;
    expect(button.textContent?.trim()).toBe('Phát hành cho 1 phòng');
    expect(button.disabled).toBeTrue();
  });

  it('W7 lists each department with its KPIs received and the change against last year', () => {
    const fixture = render({
      step: 'PUBLISH',
      canPublish: true,
      allAllocations: [publishAllocation],
    });
    const cells = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.publish-table tbody td'),
    ).map((cell) => cell.textContent?.trim());
    expect(cells.slice(0, 4)).toEqual(['Phòng Giao dịch', 'Chưa xác định', '1', '▲ +25,0%']);
  });

  it('publishes after confirmation and returns to the overview', () => {
    const fixture = render({
      step: 'PUBLISH',
      canPublish: true,
      allAllocations: [publishAllocation],
    });
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    clickText(fixture, 'Phát hành cho 1 phòng');
    expect((fixture.nativeElement as HTMLElement).querySelector('.confirm-dialog')).not.toBeNull();
    const dialogButtons = (
      fixture.nativeElement as HTMLElement
    ).querySelectorAll<HTMLButtonElement>('.confirm-actions button');
    dialogButtons[1].click();
    fixture.detectChanges();

    expect(api.publish).toHaveBeenCalledOnceWith('by-1');
    expect(navigate).toHaveBeenCalledWith(['/target-overview']);
  });

  it('shows why publishing was refused', () => {
    const fixture = render({
      step: 'PUBLISH',
      canPublish: true,
      publish: 'blocked',
      allAllocations: [publishAllocation],
    });

    clickText(fixture, 'Phát hành cho 1 phòng');
    (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('.confirm-actions button')[1]
      .click();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.publish-error')?.textContent,
    ).toContain('Chưa phát hành được: còn chỉ tiêu chưa khớp.');
  });

  it('shows the published state instead of the publish button', () => {
    const fixture = render({ step: 'PUBLISHED' });
    clickText(fixture, '✓ 4. Phát hành');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.publish-done')?.textContent,
    ).toContain('Chỉ tiêu năm nay đã được phát hành cho các phòng.');
  });
});
