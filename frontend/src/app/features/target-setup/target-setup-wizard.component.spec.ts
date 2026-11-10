import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { PagedResult } from '../../core/paged-result.model';
import { TargetSetup, TargetSetupStep } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
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

  const setupState = (step: TargetSetupStep): TargetSetup => ({
    branchYearId: step === 'IMPORT' ? null : 'by-1',
    hasBranchYear: step !== 'IMPORT',
    importedAt: null,
    rowCount: 3,
    step,
    status: 'DRAFT',
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
  }

  let api: jasmine.SpyObj<TargetAssignmentService>;

  function render(options: RenderOptions = {}): ComponentFixture<TargetSetupWizardComponent> {
    const targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getSetup',
      'getQuarterSplit',
      'getKpiAllocation',
      'publish',
    ]);
    api = targetApi;
    const departmentApi = jasmine.createSpyObj<DepartmentService>('DepartmentService', ['list']);
    departmentApi.list.and.returnValue(of(page(options.departments ?? [branch])));
    targetApi.getSetup.and.returnValue(
      options.fail
        ? throwError(() => new Error('boom'))
        : of({ ...setupState(options.step ?? 'IMPORT'), canPublish: options.canPublish ?? false }),
    );
    targetApi.getKpiAllocation.and.returnValue(throwError(() => new Error('not needed')));
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
            isRatio: false,
            yearValue: 100,
            q1: 25,
            q2: 25,
            q3: 25,
            q4: 25,
            previousYear: null,
            sum: 100,
            diff: 0,
            matched: true,
          },
        ],
      }),
    );

    TestBed.configureTestingModule({
      providers: [
        { provide: DepartmentService, useValue: departmentApi },
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ year: '2026' })) },
        },
      ],
    }).overrideComponent(TargetSetupWizardComponent, {
      remove: { imports: [HeaderComponent, SidebarComponent] },
      add: { imports: [HeaderStub, SidebarStub] },
    });

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

  it('keeps Phát hành disabled until every target matches', () => {
    const fixture = render({ step: 'PUBLISH', canPublish: false });
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        '.publish-panel button',
      ),
    )[0];
    expect(button.disabled).toBeTrue();
  });

  it('publishes after confirmation and returns to the overview', () => {
    const fixture = render({ step: 'PUBLISH', canPublish: true });
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    clickText(fixture, 'Phát hành');
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
    const fixture = render({ step: 'PUBLISH', canPublish: true, publish: 'blocked' });

    clickText(fixture, 'Phát hành');
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
