import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { convertToParamMap, ActivatedRoute, provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { EvaluationFormComponent } from './evaluation-form.component';
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
  qualitativeScore: null,
  qualitativeNotes: null,
  isQualitative: false,
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

const qualitativeGroup: EvaluationCriterionGroup = {
  evaluationProfileItemId: 'g-q',
  groupCode: 'SOFT',
  groupName: 'Attitude',
  weightPercent: 30,
  groupScore: 100,
  qualitativeScore: 80,
  qualitativeNotes: 'note',
  isQualitative: true,
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
    criteriaGroups: [quantitativeGroup, qualitativeGroup],
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
      proposedTier: 'B',
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
    },
    canEdit: true,
    pendingSubmissionCount: 2,
    openAppealCount: 1,
    hasOpenDispute: false,
    previousPeriod: { periodCode: '2026-Q3', totalScore: 60, tier: 'B', scoreDifference: 3 },
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
    evaluationStatus: 'NOT_STARTED',
    proposedTier: null,
    calculatedAt: null,
    quantitativeScore: null,
    qualitativeScore: null,
    hasOpenDispute: false,
    pendingSubmissionCount: 0,
    canEvaluate: true,
    updatedAt: null,
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
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  const text = () => TestBed.inject(I18nService).localization.evaluationForm;

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
    api = jasmine.createSpyObj<EvaluationService>('EvaluationService', [
      'form',
      'submit',
      'departmentEmployees',
    ]);
    query = new BehaviorSubject(convertToParamMap({ employeeId: 'e2', periodId: 'period-1' }));

    await TestBed.configureTestingModule({
      imports: [EvaluationFormComponent],
      providers: [
        provideRouter([]),
        { provide: EvaluationService, useValue: api },
        { provide: ActivatedRoute, useValue: { queryParamMap: query.asObservable() } },
        I18nService,
      ],
    })
      .overrideComponent(EvaluationFormComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [StubHeaderComponent, StubSidebarComponent] },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
  });

  it('loads the form of the employee and period named in the query', async () => {
    await create();
    expect(api.form).toHaveBeenCalledOnceWith('e2', 'period-1');
    expect(component.form()?.employee.fullName).toBe('Le Minh Chau');
    expect(fixture.nativeElement.textContent).toContain('Le Minh Chau');
    expect(fixture.nativeElement.textContent).toContain('2026-Q4');
  });

  it('renders the groups, the KPI rows and the risk penalty from the API', async () => {
    await create();
    const body = fixture.nativeElement.textContent as string;
    expect(body).toContain('Business');
    expect(body).toContain('Deposits');
    expect(body).toContain('New customers');
    expect(body).toContain('RSK-1');
    expect(body).toContain('Sales profile');
  });

  it('shows N/A for a KPI that has no target instead of zero', async () => {
    await create();
    const rows = fixture.nativeElement.querySelectorAll('tr.kpi-row') as NodeListOf<HTMLElement>;
    const noTarget = Array.from(rows).find((r) => r.textContent?.includes('New customers'));
    const targetCell = noTarget?.querySelector('.td-target') as HTMLElement;
    expect(targetCell.textContent?.trim()).toBe(
      TestBed.inject(I18nService).localization.evaluations.emptyValue,
    );
  });

  it('places the weight column between the KPI item and the target', async () => {
    await create();
    const labels = text();
    const header = Array.from(
      fixture.nativeElement.querySelectorAll('thead th') as NodeListOf<HTMLElement>,
    ).map((cell) => cell.textContent?.trim());
    expect(header.slice(0, 3)).toEqual([labels.colKpiItem, labels.colWeight, labels.colTarget]);
  });

  it('shows the weight of each KPI as a percent of its group', async () => {
    await create();
    const rows = fixture.nativeElement.querySelectorAll('tr.kpi-row') as NodeListOf<HTMLElement>;
    const cells = Array.from(rows)
      .filter((row) => row.querySelector('.td-weight'))
      .map((row) =>
        (row.querySelector('.td-weight') as HTMLElement).textContent?.replace(/\s/g, ''),
      );
    expect(cells.length).toBe(2);
    expect(cells[0]).toContain('60');
    expect(cells[1]).toContain('40');
    expect(cells.every((cell) => cell?.endsWith('%'))).toBeTrue();
  });

  it('spans the group, summary and penalty rows over every column', async () => {
    await create();
    const columns = fixture.nativeElement.querySelectorAll('thead th').length;
    const width = (row: Element): number =>
      Array.from(row.children).reduce(
        (sum, cell) => sum + Number(cell.getAttribute('colspan') ?? 1),
        0,
      );
    const groupRows = fixture.nativeElement.querySelectorAll(
      'tr.group-header-row',
    ) as NodeListOf<HTMLElement>;
    expect(groupRows.length).toBeGreaterThan(0);
    groupRows.forEach((row) => expect(width(row)).toBe(columns));
    const penalty = fixture.nativeElement.querySelector('tr.penalty-row') as HTMLElement;
    const penaltyWidth = Array.from(penalty.children).reduce(
      (sum, cell) => sum + Number(cell.getAttribute('colspan') ?? 1),
      0,
    );
    expect(penaltyWidth).toBe(columns);
    const total = fixture.nativeElement.querySelector('tr.kpi-total-row') as HTMLElement;
    const totalWidth = Array.from(total.children).reduce(
      (sum, cell) => sum + Number(cell.getAttribute('colspan') ?? 1),
      0,
    );
    expect(totalWidth).toBe(columns);
  });

  it('shows the score of each group on its header row, in the score column', async () => {
    await create();
    const rows = Array.from(
      fixture.nativeElement.querySelectorAll('tr.group-header-row') as NodeListOf<HTMLElement>,
    );
    const columns = fixture.nativeElement.querySelectorAll('thead th').length;
    const scoreOf = (row: HTMLElement): string | undefined =>
      row.querySelector('.group-score-cell')?.textContent?.replace(/\s/g, '');

    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Business');
    expect(scoreOf(rows[0])).toContain('50');
    expect(scoreOf(rows[1])).toContain('100');
    rows.forEach((row) => {
      // The score cell is the last one, so it sits under the SCORE column.
      expect(row.lastElementChild?.classList.contains('group-score-cell')).toBeTrue();
      expect(row.lastElementChild?.classList.contains('text-center')).toBeTrue();
      expect(columns).toBe(6);
    });
  });

  it('does not repeat the score of a qualitative group on a second row', async () => {
    await create();
    const rows = Array.from(
      fixture.nativeElement.querySelectorAll('tr.group-header-row') as NodeListOf<HTMLElement>,
    );
    const qualitative = rows.find((row) => row.textContent?.includes('Attitude'));
    expect(qualitative?.querySelector('.group-score-cell')).not.toBeNull();
    const note = (qualitative?.nextElementSibling as HTMLElement).textContent?.trim();
    expect(note).toBe(text().qualitativeGroupRow);
    expect((qualitative?.nextElementSibling as HTMLElement).querySelector('.td-score')).toBeNull();
  });
  it('centres every table column except the KPI item', async () => {
    await create();
    const header = fixture.nativeElement.querySelectorAll('thead th') as NodeListOf<HTMLElement>;
    expect(header[0].classList.contains('text-center')).toBeFalse();
    Array.from(header)
      .slice(1)
      .forEach((cell) => expect(cell.classList.contains('text-center')).toBeTrue());
  });

  it('does not call the form API when the query lacks the employee or the period', async () => {
    query.next(convertToParamMap({ employeeId: 'e2' }));
    await create();
    expect(api.form).not.toHaveBeenCalled();
    expect(component.failure()).toBe('notFound');
    expect(component.stateText()).toBe(text().notFound);
  });

  it('reloads when the query changes', async () => {
    await create();
    query.next(convertToParamMap({ employeeId: 'e3', periodId: 'period-1' }));
    expect(api.form).toHaveBeenCalledWith('e3', 'period-1');
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

  it('fills the comment and the qualitative score from the stored evaluation', async () => {
    await create();
    expect(component.commentInput()).toBe('Saved comment');
    expect(component.qualitativeScores()['g-q']).toBe(80);
    expect(component.qualitativeAverage()).toBe(80);
  });

  it('keeps the qualitative average empty when no score is entered', async () => {
    await create();
    component.setQualitativeScore('g-q', null);
    expect(component.qualitativeAverage()).toBeNull();
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

    it('blocks saving while a qualitative score is out of range', async () => {
      await create();
      component.setQualitativeScore('g-q', component.scoreMax + 1);
      expect(component.scoreInvalid()).toBeTrue();
      expect(component.canSubmit()).toBeFalse();
      component.setQualitativeScore('g-q', component.scoreMin - 1);
      expect(component.scoreInvalid()).toBeTrue();
      component.setQualitativeScore('g-q', component.scoreMax);
      expect(component.scoreInvalid()).toBeFalse();
    });
  });

  describe('saving', () => {
    it('saves a draft with the entered comment and qualitative scores', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.commentInput.set('  New comment  ');
      component.setQualitativeScore('g-q', 90);
      component.onSaveDraft();
      expect(api.submit).toHaveBeenCalledOnceWith({
        employeeId: 'e2',
        periodId: 'period-1',
        comment: 'New comment',
        coachingPlan: 'plan',
        coachingRequired: true,
        qualitativeScores: [{ evaluationProfileItemId: 'g-q', score: 90, notes: 'note' }],
        isDraft: true,
      });
      expect(component.saveMessage()?.kind).toBe('success');
      expect(component.saveMessage()?.text).toBe(text().saveDraftSuccess);
    });

    it('reloads the form after a successful save and keeps the success message', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.onSaveDraft();
      expect(api.form).toHaveBeenCalledTimes(2);
      expect(component.saveMessage()?.kind).toBe('success');
    });

    it('completes the evaluation with isDraft false', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.onCompleteEvaluation();
      expect(api.submit.calls.mostRecent().args[0].isDraft).toBeFalse();
      expect(component.saveMessage()?.text).toBe(text().completeSuccess);
    });

    it('refuses to complete without a comment', async () => {
      await create();
      component.commentInput.set('   ');
      component.onCompleteEvaluation();
      expect(api.submit).not.toHaveBeenCalled();
      expect(component.saveMessage()).toEqual({
        kind: 'error',
        text: text().completeRequirements,
      });
    });

    it('refuses to complete while a qualitative score is missing', async () => {
      await create();
      component.setQualitativeScore('g-q', null);
      component.onCompleteEvaluation();
      expect(api.submit).not.toHaveBeenCalled();
      expect(component.saveMessage()?.kind).toBe('error');
    });

    it('still allows a draft without a comment or a score', async () => {
      api.submit.and.returnValue(of(savedResult));
      await create();
      component.commentInput.set('');
      component.setQualitativeScore('g-q', null);
      component.onSaveDraft();
      expect(api.submit).toHaveBeenCalledTimes(1);
      expect(api.submit.calls.mostRecent().args[0].qualitativeScores).toEqual([]);
      expect(api.submit.calls.mostRecent().args[0].comment).toBeNull();
    });

    it('ignores a second save while one is in flight', async () => {
      const pending = new Subject<EvaluationResult>();
      api.submit.and.returnValue(pending.asObservable());
      await create();
      component.onSaveDraft();
      expect(component.saving()).toBeTrue();
      component.onSaveDraft();
      component.onCompleteEvaluation();
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

  describe('previous and next', () => {
    it('loads the roster once, without a department, so it matches the list', async () => {
      await create();
      expect(api.departmentEmployees).toHaveBeenCalledOnceWith('period-1');
    });

    it('does not reload the roster when moving to another employee of the same period', async () => {
      await create();
      api.form.and.returnValue(of(buildForm({ employee: { ...buildForm().employee, id: 'e3' } })));
      query.next(convertToParamMap({ employeeId: 'e3', periodId: 'period-1' }));
      expect(api.departmentEmployees).toHaveBeenCalledTimes(1);
    });

    it('shows the position in the roster and opens the neighbours', async () => {
      await create();
      expect(component.rosterIndex()).toBe(1);
      expect(component.navIndicatorText()).toBe('2 / 3');
      component.onPrev();
      expect(router.navigate).toHaveBeenCalledWith(['/evaluations/form'], {
        queryParams: { employeeId: 'e1', periodId: 'period-1' },
      });
      component.onNext();
      expect(router.navigate).toHaveBeenCalledWith(['/evaluations/form'], {
        queryParams: { employeeId: 'e3', periodId: 'period-1' },
      });
    });

    it('does not move past either end of the roster', async () => {
      await create(buildForm({ employee: { ...buildForm().employee, id: 'e1' } }));
      (router.navigate as jasmine.Spy).calls.reset();
      component.onPrev();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('hides the navigation when the employee is not in the roster', async () => {
      await create(buildForm(), ['x1', 'x2']);
      expect(component.hasRosterNavigation()).toBeFalse();
      expect(fixture.nativeElement.querySelector('.pagination-controls')).toBeNull();
    });

    it('hides the navigation when the roster cannot be read', async () => {
      api.form.and.returnValue(of(buildForm()));
      api.departmentEmployees.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 403 })),
      );
      fixture = TestBed.createComponent(EvaluationFormComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      expect(component.roster()).toEqual([]);
      expect(component.hasRosterNavigation()).toBeFalse();
    });
  });

  describe('side panels', () => {
    it('builds the attention notes from the API counts', async () => {
      await create();
      expect(component.pendingNotesText()).toContain('2');
      expect(component.riskPenaltyNotesText()).toContain('1');
      expect(component.appealsNotesText()).toContain('1');
    });

    it('compares with the previous period when there is one', async () => {
      await create();
      expect(component.prevPeriodTitleText()).toContain('2026-Q3');
      expect(component.comparisonText()).toContain('3');
    });

    it('says there is no previous period instead of inventing one', async () => {
      await create(buildForm({ previousPeriod: null }));
      expect(component.prevPeriodTitleText()).toBe(text().prevPeriodNone);
      expect(component.comparisonText()).toBe(
        TestBed.inject(I18nService).localization.evaluations.emptyValue,
      );
      expect(fixture.nativeElement.querySelector('.prev-score')).toBeNull();
    });

    it('picks the lower and equal comparison wording', async () => {
      await create(
        buildForm({
          previousPeriod: { periodCode: 'p', totalScore: 70, tier: 'B', scoreDifference: -2 },
        }),
      );
      expect(component.comparisonText()).toContain('2');
      expect(component.comparisonText()).not.toBe(text().comparisonEqual);
      query.next(convertToParamMap({ employeeId: 'e2', periodId: 'period-1' }));
      api.form.and.returnValue(
        of(
          buildForm({
            previousPeriod: { periodCode: 'p', totalScore: 63, tier: 'B', scoreDifference: 0 },
          }),
        ),
      );
      query.next(convertToParamMap({ employeeId: 'e2', periodId: 'period-2' }));
      expect(component.comparisonText()).toBe(text().comparisonEqual);
    });

    it('shows the draft and the completed badge from the stored status', async () => {
      await create();
      expect(fixture.nativeElement.querySelector('.badge-draft')).not.toBeNull();
      await create(
        buildForm({
          currentEvaluation: { ...buildForm().currentEvaluation, status: 'CALCULATED' },
        }),
      );
      expect(fixture.nativeElement.querySelector('.badge-completed')).not.toBeNull();
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

    it('renders the aria label of the breadcrumb from the dictionary', async () => {
      await create();
      const nav = fixture.nativeElement.querySelector('nav.breadcrumb-bar') as HTMLElement;
      expect(nav.getAttribute('aria-label')).toBe(text().breadcrumbAria);
    });
  });

  it('goes back to the evaluation list', async () => {
    await create();
    component.onBackToList();
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations']);
  });
});
