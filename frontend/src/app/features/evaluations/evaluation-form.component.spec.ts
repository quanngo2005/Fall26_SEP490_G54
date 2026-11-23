import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { EvaluationFormComponent } from './evaluation-form.component';
import { EvaluationFormTargetService } from '../../core/evaluation-form-target.service';
import { EvaluationService } from '../../core/evaluation.service';
import {
  DepartmentEmployeeEvaluation,
  EvaluationCriterionGroup,
  EvaluationFormModel,
  EvaluationResult,
} from '../../core/evaluation.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

@Component({ selector: 'app-header', standalone: true, template: '' })
class StubHeaderComponent {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class StubSidebarComponent {}

const quantitativeGroup: EvaluationCriterionGroup = {
  evaluationProfileItemId: 'g-kd',
  groupCode: 'KD',
  groupName: 'Business',
  weightPercent: 70,
  groupScore: 50,
  kpiItems: [
    {
      kpiDefinitionId: 'k1',
      kpiCode: 'HDV',
      kpiName: 'Deposits',
      unit: 'bn',
      weightPercent: 60,
      targetValue: 225,
      actualValue: 100,
      achievementPercent: 44.44,
      componentScore: 44.44,
    },
    {
      kpiDefinitionId: 'k2',
      kpiCode: 'KH_MOI',
      kpiName: 'New customers',
      unit: 'clients',
      weightPercent: 40,
      targetValue: null,
      actualValue: 0,
      achievementPercent: 0,
      componentScore: 0,
    },
  ],
};

const serviceGroup: EvaluationCriterionGroup = {
  evaluationProfileItemId: 'g-sv',
  groupCode: 'SV',
  groupName: 'Service',
  weightPercent: 30,
  groupScore: 100,
  kpiItems: [],
};

function buildForm(overrides: Partial<EvaluationFormModel> = {}): EvaluationFormModel {
  return {
    employee: {
      id: 'e2',
      employeeCode: 'NV004',
      fullName: 'Le Minh Chau',
      departmentName: 'Team 1',
      departmentId: 'd1',
      jobPositionTitle: 'Sales',
      jobPositionId: 'p1',
    },
    period: {
      id: 'period-1',
      code: '2026-Q4',
      name: 'Q4',
      startDate: '2026-10-01',
      endDate: '2026-12-31',
      periodType: 'QUARTER',
      status: 'OPEN',
    },
    profile: { id: 'pr1', code: 'EP-SALES', name: 'Sales profile', version: 1 },
    criteriaGroups: [quantitativeGroup, serviceGroup],
    riskEvents: [
      {
        id: 'r1',
        code: 'RSK-1',
        eventType: 'QA',
        description: 'Late handover',
        severity: 'MEDIUM',
        penaltyPoints: 2,
        occurredAt: '2026-10-14T00:00:00+07:00',
      },
    ],
    summary: {
      businessScore: 50,
      serviceQualityScore: 0,
      operationsScore: 0,
      totalRiskPenalty: 2,
      totalScore: 63,
      weightedScore: 65,
    },
    currentEvaluation: {
      id: null,
      totalScore: null,
      status: 'DRAFT',
      reviewerEmployeeId: null,
      reviewerName: null,
      calculatedAt: null,
      approvedAt: null,
      comment: 'Saved comment',
      coachingPlan: 'plan',
      coachingRequired: true,
      finalTierCode: null,
    },
    canEdit: true,
    canComplete: true,
    pendingSubmissionCount: 2,
    openAppealCount: 1,
    hasOpenDispute: false,
    previousPeriod: { periodCode: '2026-Q3', totalScore: 60, tier: 'B', scoreDifference: 3 },
    comparePeriod: null,
    compareOptions: [],
    criteriaSummary: {
      achievedCount: 3,
      nearCount: 2,
      belowCount: 1,
      totalCount: 6,
      achievedRatePercent: 50,
    },
    slaSummary: {
      onTimeCompletedCount: 27,
      overdueCount: 1,
      inProgressCount: 2,
      onTimeRatePercent: 96.4,
    },
    formSettings: {
      maxCommentLength: 1000,
      kpiMetThresholdPercent: 100,
      kpiNearThresholdPercent: 85,
    },
    ...overrides,
  };
}

function rosterRow(employeeId: string): DepartmentEmployeeEvaluation {
  return {
    employeeId,
    employeeCode: employeeId.toUpperCase(),
    fullName: employeeId,
    jobPositionTitle: 'Sales',
    jobPositionId: 'p1',
    departmentId: 'd1',
    departmentName: 'Team 1',
    evaluationId: null,
    totalScore: null,
    evaluationStatus: 'DRAFT',
    proposedTier: null,
    calculatedAt: null,
    quantitativeScore: null,
    qualitativeScore: null,
    hasOpenDispute: false,
    pendingSubmissionCount: 0,
    canEvaluate: true,
    updatedAt: null,
    quantitativeScoreChange: null,
  };
}

const savedResult: EvaluationResult = {
  evaluationId: 'ev1',
  employeeId: 'e2',
  periodId: 'period-1',
  totalScore: 70,
  status: 'CALCULATED',
  proposedTier: 'B',
  calculatedAt: '2026-10-28T00:00:00+07:00',
  message: 'ok',
};

describe('EvaluationFormComponent', () => {
  let fixture: ComponentFixture<EvaluationFormComponent>;
  let component: EvaluationFormComponent;
  let api: jasmine.SpyObj<EvaluationService>;
  let router: Router;
  let target: EvaluationFormTargetService;

  const text = () => TestBed.inject(I18nService).localization.evaluationForm;
  const emptyValue = () => TestBed.inject(I18nService).localization.evaluations.emptyValue;

  async function create(
    form: EvaluationFormModel | HttpErrorResponse = buildForm(),
    roster = ['e1', 'e2', 'e3'],
  ) {
    api.form.and.returnValue(form instanceof HttpErrorResponse ? throwError(() => form) : of(form));
    api.departmentEmployees.and.returnValue(of(roster.map(rosterRow)));
    fixture = TestBed.createComponent(EvaluationFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    sessionStorage.clear();
    api = jasmine.createSpyObj<EvaluationService>('EvaluationService', [
      'form',
      'submit',
      'departmentEmployees',
      'periods',
      'exportEvaluationForm',
      'submitToBranch',
      'finalizeEvaluation',
    ]);
    api.periods.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [EvaluationFormComponent],
      providers: [provideRouter([]), { provide: EvaluationService, useValue: api }, I18nService],
    })
      .overrideComponent(EvaluationFormComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [StubHeaderComponent, StubSidebarComponent] },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    target = TestBed.inject(EvaluationFormTargetService);
    target.save({ employeeId: 'e2', periodId: 'period-1' });
  });

  afterEach(() => sessionStorage.clear());

  it('loads the form of the employee and period saved for this tab', async () => {
    await create();
    expect(api.form).toHaveBeenCalledOnceWith('e2', 'period-1');
    expect(component.form()?.employee.fullName).toBe('Le Minh Chau');
    expect(fixture.nativeElement.textContent).toContain('Le Minh Chau');
  });

  it('renders the groups, the KPI rows and the risk events from the API', async () => {
    await create();
    const body = fixture.nativeElement.textContent as string;
    expect(body).toContain('Business');
    expect(body).toContain('Service');
    expect(body).toContain('Deposits');
    expect(body).toContain('New customers');
    expect(body).toContain('RSK-1');
  });

  it('does not show any qualitative input', async () => {
    await create();
    expect(fixture.nativeElement.querySelector('input')).toBeNull();
    expect(fixture.nativeElement.querySelector('select.score-input')).toBeNull();
  });

  it('shows the weighted KPI score from the API in the total row', async () => {
    await create();
    const total = fixture.nativeElement.querySelector('tr.total-row') as HTMLElement;
    expect(total.textContent).toContain('65');
  });

  it('shows the total risk deduction above the KPI total', async () => {
    await create();
    const deduction = fixture.nativeElement.querySelector('.metric-deduction') as HTMLElement;
    expect(deduction.textContent).toContain('2');
    expect(fixture.nativeElement.querySelector('.risk-total')).not.toBeNull();
  });

  it('does not show a final score or a tier calculated by the form', async () => {
    await create();
    const titles = Array.from(
      fixture.nativeElement.querySelectorAll('.metric-title') as NodeListOf<HTMLElement>,
    ).map((title) => title.textContent?.trim());
    expect(titles).not.toContain(text().totalScoreTitle);
    expect(component.tierText()).toBe(emptyValue());
  });

  it('shows the final tier only when the record is FINAL', async () => {
    await create(
      buildForm({
        currentEvaluation: {
          ...buildForm().currentEvaluation,
          status: 'FINAL',
          finalTierCode: 'B',
        },
      }),
    );
    expect(component.tierText()).toBe(
      TestBed.inject(I18nService).translate('evaluationForm.proposedTier', { tier: 'B' }),
    );
  });

  it('shows N/A for a KPI that has no target instead of zero', async () => {
    await create();
    const rows = fixture.nativeElement.querySelectorAll('tr.kpi-row') as NodeListOf<HTMLElement>;
    const noTarget = Array.from(rows).find((row) => row.textContent?.includes('New customers'));
    expect(noTarget?.textContent).toContain(emptyValue());
  });

  it('shows N/A for a KPI without progress and keeps a real zero', async () => {
    const noProgress = {
      ...quantitativeGroup,
      kpiItems: [
        {
          ...quantitativeGroup.kpiItems[1],
          targetValue: 10,
          actualValue: null,
          achievementPercent: null,
          componentScore: null,
        },
      ],
    };
    await create(buildForm({ criteriaGroups: [noProgress, serviceGroup] }));
    const row = Array.from(
      fixture.nativeElement.querySelectorAll('tr.kpi-row') as NodeListOf<HTMLElement>,
    ).find((r) => r.textContent?.includes('New customers'));
    expect(row?.textContent).toContain(emptyValue());
  });

  it('shows N/A for the total when nothing can be scored', async () => {
    await create(
      buildForm({
        summary: {
          ...buildForm().summary,
          totalScore: null,
          weightedScore: null,
        },
      }),
    );
    const total = fixture.nativeElement.querySelector('tr.total-row') as HTMLElement;
    expect(total.textContent).toContain(emptyValue());
    expect(component.tierText()).toBe(emptyValue());
  });

  it('shows N/A for a group score that cannot be determined', async () => {
    await create(
      buildForm({ criteriaGroups: [{ ...quantitativeGroup, groupScore: null }, serviceGroup] }),
    );
    const group = fixture.nativeElement.querySelector('tr.group-row') as HTMLElement;
    expect(group.querySelector('.score-cell')?.textContent).toContain(emptyValue());
  });

  it('shows the group rows and the total row with the dictionary colours', async () => {
    await create();
    expect(fixture.nativeElement.querySelectorAll('tr.group-row').length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('tr.total-row').length).toBe(1);
  });

  it('keeps the table header in the order of the SVG', async () => {
    await create();
    const labels = text();
    const header = Array.from(
      fixture.nativeElement.querySelectorAll('thead th') as NodeListOf<HTMLElement>,
    ).map((cell) => cell.textContent?.trim());
    expect(header).toEqual([
      labels.colKpiItem,
      labels.colWeight,
      labels.colTarget,
      labels.colActual,
      labels.colAchievement,
      labels.colScore,
    ]);
  });

  it('does not call the form API when no employee and period were saved for this tab', async () => {
    sessionStorage.clear();
    await create();
    expect(api.form).not.toHaveBeenCalled();
    expect(component.failure()).toBe('notFound');
    expect(component.stateText()).toBe(text().notFound);
  });

  it('does not navigate when it opens, so the address is left as it is', async () => {
    await create();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('shows the loading text while the form is pending', () => {
    api.form.and.returnValue(new Subject<EvaluationFormModel>().asObservable());
    fixture = TestBed.createComponent(EvaluationFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect(component.loading()).toBeTrue();
    expect(component.stateText()).toBe(text().loading);
  });

  const failures: {
    status: number;
    failure: 'offline' | 'forbidden' | 'notFound' | 'error';
    key: 'offline' | 'forbidden' | 'notFound' | 'loadError';
  }[] = [
    { status: 0, failure: 'offline', key: 'offline' },
    { status: 403, failure: 'forbidden', key: 'forbidden' },
    { status: 404, failure: 'notFound', key: 'notFound' },
    { status: 500, failure: 'error', key: 'loadError' },
  ];

  for (const { status, failure, key } of failures) {
    it(`maps an HTTP ${status} to the ${failure} state without any rows`, async () => {
      await create(new HttpErrorResponse({ status }));
      expect(component.failure()).toBe(failure);
      expect(component.form()).toBeNull();
      expect(component.stateText()).toBe(text()[key]);
      expect(fixture.nativeElement.querySelector('tr.kpi-row')).toBeNull();
    });
  }

  it('fills the comment from the stored evaluation', async () => {
    await create();
    expect(component.commentInput()).toBe('Saved comment');
  });

  describe('edit gate', () => {
    it('allows saving when the API says the form is editable', async () => {
      await create();
      expect(component.canSubmit()).toBeTrue();
    });

    it('is read only when the API says the form is not editable', async () => {
      await create(buildForm({ canEdit: false }));
      expect(component.canSubmit()).toBeFalse();
      expect(fixture.nativeElement.querySelector('.readonly-note')).not.toBeNull();
      const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
      expect(textarea.disabled).toBeTrue();
      component.onSaveDraft();
      expect(api.submit).not.toHaveBeenCalled();
    });
  });

  describe('saving', () => {
    it('saves a draft with the entered comment and no qualitative scores', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.commentInput.set('  New comment  ');
      component.onSaveDraft();
      expect(api.submit).toHaveBeenCalledOnceWith({
        employeeId: 'e2',
        periodId: 'period-1',
        comment: 'New comment',
        coachingPlan: 'plan',
        coachingRequired: true,
        isDraft: true,
      });
      expect(component.saveMessage()?.kind).toBe('success');
      expect(component.saveMessage()?.text).toBe(text().saveDraftSuccess);
    });

    it('reloads the form after a successful save', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.onSaveDraft();
      expect(api.form).toHaveBeenCalledTimes(2);
      expect(component.saveMessage()?.kind).toBe('success');
    });

    it('ignores a second save while one is in flight', async () => {
      const pending = new Subject<EvaluationResult>();
      api.submit.and.returnValue(pending.asObservable());
      await create();
      component.onSaveDraft();
      expect(component.saving()).toBeTrue();
      component.onSaveDraft();
      expect(api.submit).toHaveBeenCalledTimes(1);
      pending.next(savedResult);
      pending.complete();
      expect(component.saving()).toBeFalse();
    });

    const saveErrors: {
      status: number;
      key: 'offline' | 'saveForbidden' | 'saveConflict' | 'saveError';
    }[] = [
      { status: 0, key: 'offline' },
      { status: 403, key: 'saveForbidden' },
      { status: 409, key: 'saveConflict' },
      { status: 500, key: 'saveError' },
    ];

    for (const { status, key } of saveErrors) {
      it(`shows the ${key} message for an HTTP ${status} and keeps the form`, async () => {
        api.submit.and.returnValue(throwError(() => new HttpErrorResponse({ status })));
        await create();
        component.onSaveDraft();
        expect(component.saveMessage()).toEqual({ kind: 'error', text: text()[key] });
        expect(component.saving()).toBeFalse();
        expect(component.form()).not.toBeNull();
        expect(api.form).toHaveBeenCalledTimes(1);
      });
    }
  });

  describe('rejected input', () => {
    it('shows the message the API sends for an HTTP 400', async () => {
      const message = 'Nhận xét quá dài.';
      api.submit.and.returnValue(
        throwError(
          () => new HttpErrorResponse({ status: 400, error: { success: false, message } }),
        ),
      );
      await create();
      component.onSaveDraft();
      expect(component.saveMessage()).toEqual({ kind: 'error', text: message });
      expect(component.saving()).toBeFalse();
    });

    it('falls back to the generic message for an HTTP 400 without a message', async () => {
      api.submit.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
      await create();
      component.onSaveDraft();
      expect(component.saveMessage()).toEqual({ kind: 'error', text: text().saveError });
    });
  });

  describe('unsaved changes', () => {
    let confirmSpy: jasmine.Spy;

    beforeEach(() => {
      confirmSpy = spyOn(window, 'confirm').and.returnValue(true);
    });

    it('does not ask while nothing was changed', async () => {
      await create();
      expect(component.hasUnsavedChanges()).toBeFalse();
      component.onNext();
      component.onBackToList();
      expect(confirmSpy).not.toHaveBeenCalled();
      expect(api.form).toHaveBeenCalledWith('e3', 'period-1');
      expect(router.navigate).toHaveBeenCalledOnceWith(['/evaluations']);
    });

    it('asks before moving to another employee when the comment was edited', async () => {
      await create();
      component.commentInput.set('Edited comment');
      expect(component.hasUnsavedChanges()).toBeTrue();
      component.onNext();
      expect(confirmSpy).toHaveBeenCalledOnceWith(text().confirmDiscard);
      expect(api.form).toHaveBeenCalledWith('e3', 'period-1');
    });

    it('stays on the form and keeps the input when the reviewer declines', async () => {
      confirmSpy.and.returnValue(false);
      await create();
      component.commentInput.set('Edited comment');
      api.form.calls.reset();
      component.onNext();
      component.onPrev();
      component.onBackToList();
      expect(api.form).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
      expect(target.read()).toEqual({ employeeId: 'e2', periodId: 'period-1' });
      expect(component.commentInput()).toBe('Edited comment');
    });

    it('does not ask on a read only form', async () => {
      await create(buildForm({ canEdit: false }));
      component.commentInput.set('Edited comment');
      expect(component.hasUnsavedChanges()).toBeFalse();
    });
  });

  describe('previous and next', () => {
    it('loads the roster once, without a department, so it matches the list', async () => {
      await create();
      expect(api.departmentEmployees).toHaveBeenCalledOnceWith('period-1');
    });

    it('shows the position in the roster and opens the neighbours', async () => {
      await create();
      expect(component.rosterIndex()).toBe(1);
      expect(component.navIndicatorText()).toBe('2 / 3');
      component.onPrev();
      expect(api.form).toHaveBeenCalledWith('e1', 'period-1');
      expect(target.read()).toEqual({ employeeId: 'e1', periodId: 'period-1' });
    });

    it('does not move past either end of the roster', async () => {
      await create(buildForm({ employee: { ...buildForm().employee, id: 'e1' } }));
      api.form.calls.reset();
      component.onPrev();
      expect(api.form).not.toHaveBeenCalled();
    });

    it('hides the navigation when the employee is not in the roster', async () => {
      await create(buildForm(), ['x1', 'x2']);
      expect(component.hasRosterNavigation()).toBeFalse();
      expect(fixture.nativeElement.querySelector('.pagination-controls')).toBeNull();
    });
  });

  describe('side panels', () => {
    it('shows the attention counts from the API', async () => {
      await create();
      expect(component.pendingPillText()).toContain('2');
      expect(component.appealPillText()).toContain('1');
    });

    it('shows the risk count and the number of events', async () => {
      await create();
      expect(component.riskCountPillText()).toContain('1');
    });

    it('shows the previous period delta in the KPI pill', async () => {
      await create();
      expect(component.scorePillText()).toContain('3');
    });

    it('hides the KPI pill when there is no previous period', async () => {
      await create(buildForm({ previousPeriod: null }));
      expect(component.scorePillText()).toBeNull();
    });

    it('renders criteria summary and SLA summary from the API instead of placeholders', async () => {
      await create();
      expect(component.criteriaValueText()).toBe('3 / 6');
      expect(component.criteriaPillText()).toContain('50');
      expect(component.slaValueText()).toContain('96');
    });

    it('shows empty value for criteria and SLA when there are no scorable items or SLA tasks', async () => {
      await create(
        buildForm({
          criteriaSummary: {
            achievedCount: 0,
            nearCount: 0,
            belowCount: 0,
            totalCount: 0,
            achievedRatePercent: null,
          },
          slaSummary: {
            onTimeCompletedCount: 0,
            overdueCount: 0,
            inProgressCount: 0,
            onTimeRatePercent: null,
          },
        }),
      );
      expect(component.criteriaValueText()).toBe(emptyValue());
      expect(component.criteriaPillText()).toBe(emptyValue());
      expect(component.slaValueText()).toBe(emptyValue());
    });

    it('applies dynamic formSettings for comment maxlength and KPI score tone classes', async () => {
      await create(
        buildForm({
          formSettings: {
            maxCommentLength: 600,
            kpiMetThresholdPercent: 95,
            kpiNearThresholdPercent: 80,
          },
        }),
      );
      const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
      expect(textarea.getAttribute('maxlength')).toBe('600');
      expect(component.charCounterText()).toContain('600');
      expect(component.scoreToneClass(95)).toBe('tone-good');
      expect(component.scoreToneClass(94.9)).toBe('tone-warn');
      expect(component.scoreToneClass(80)).toBe('tone-warn');
      expect(component.scoreToneClass(79.9)).toBe('tone-bad');
      expect(component.scoreToneClass(null)).toBe('');
    });
  });

  describe('completing', () => {
    it('completes with isDraft false when the form allows it and the comment is filled', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.commentInput.set('Done');
      component.onCompleteEvaluation();
      expect(api.submit).toHaveBeenCalledOnceWith({
        employeeId: 'e2',
        periodId: 'period-1',
        comment: 'Done',
        coachingPlan: 'plan',
        coachingRequired: true,
        isDraft: false,
      });
      expect(component.saveMessage()?.text).toBe(text().completeSuccess);
    });

    it('does not complete without a comment and asks for one', async () => {
      await create();
      component.commentInput.set('   ');
      component.onCompleteEvaluation();
      expect(api.submit).not.toHaveBeenCalled();
      expect(component.saveMessage()?.text).toBe(text().completeRequirements);
    });

    it('is blocked before the ranking lock day and shows the lock notice', async () => {
      await create(buildForm({ canComplete: false }));
      expect(component.canSubmit()).toBeTrue();
      expect(component.completeBlocked()).toBeFalse();
      expect(fixture.nativeElement.querySelector('.save-message')?.textContent?.trim()).toBe(
        text().completeLockedNotice,
      );
    });
  });

  describe('localization', () => {
    it('switches the visible texts live when the language changes', async () => {
      await create();
      const i18n = TestBed.inject(I18nService);
      i18n.setLanguage('en');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain(
        i18n.localization.evaluationForm.btnSaveDraft,
      );
      i18n.setLanguage('vi');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain(
        i18n.localization.evaluationForm.btnSaveDraft,
      );
    });
  });

  it('goes back to the evaluation list', async () => {
    await create();
    component.onBackToList();
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations']);
  });

  it('selects active period matching today when switching period type', async () => {
    await create();
    const today = new Date();
    const currentMonthCode = `${today.getFullYear()}-M${String(today.getMonth() + 1).padStart(2, '0')}`;
    const startOfMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-01`;
    const endOfMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-28`;

    component.periods.set([
      {
        id: 'past-month',
        code: '2025-M01',
        name: 'Tháng 1/2025',
        startDate: '2025-01-01',
        endDate: '2025-01-31',
        periodType: 'MONTH',
        status: 'LOCKED',
        isActive: false,
      },
      {
        id: 'current-month-id',
        code: currentMonthCode,
        name: 'Tháng Hiện Tại',
        startDate: startOfMonth,
        endDate: endOfMonth,
        periodType: 'MONTH',
        status: 'OPEN',
        isActive: true,
      },
    ]);

    api.form.calls.reset();
    component.onPeriodTypeChange('MONTH');
    expect(api.form).toHaveBeenCalledWith('e2', 'current-month-id');
  });
});
