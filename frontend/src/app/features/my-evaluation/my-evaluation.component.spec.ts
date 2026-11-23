import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthenticatedUser } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { MyEvaluationComponent } from './my-evaluation.component';
import {
  EvaluationPeriodOption,
  PersonalCriteriaLedgerItem,
  PersonalEvaluationView,
} from './my-evaluation.models';

describe('MyEvaluationComponent', () => {
  let component: MyEvaluationComponent;
  let fixture: ComponentFixture<MyEvaluationComponent>;
  let router: Router;

  const mockUser = signal<AuthenticatedUser | null>({
    id: 'emp-101',
    email: 'khangdm@bank.example',
    fullName: 'Đỗ Minh Khang',
    roles: ['EMPLOYEE', 'SALES_OFFICER'],
    permissions: ['EVALUATION_READ', 'WORK_READ'],
  });

  const mockEvalData: PersonalEvaluationView = {
    periodId: '2026-Q2',
    periodName: 'Quý 2/2026',
    periodCode: '2026-Q2',
    status: 'PUBLISHED',
    appealDeadlineText: '23:59 ngày 22/07/2026',
    appealRemainingDays: 3,
    canAppeal: true,
    totalPeScore: 82.0,
    scoreDelta: 3.5,
    previousPeriodName: 'Quý 1/2026',
    previousPeriodScore: 78.5,
    tier: 'A2',
    allTiers: ['A1', 'A2', 'A3', 'B', 'C'],
    rankingPercentile: 'Top 40%',
    rankingPosition: 4,
    rankingTotalEmployees: 11,
    rankingRoleTitle: 'Sales Officer',
    criteria: [
      {
        id: 'crit-deposit',
        name: 'Sales – Tiền gửi – xử lý',
        weightPercent: 40,
        actualTargetDisplay: '4.920.000.000 / 5.500.000.000 VND',
        targetDisplay: '5.500.000.000 VND',
        actualDisplay: '4.920.000.000 VND',
        score: 89.5,
        weightedScore: 35.8,
        hasDrilldown: true,
        kpiDefinitionId: 'crit-deposit',
      },
      {
        id: 'crit-cards',
        name: 'Sales – Mở thẻ – xử lý',
        weightPercent: 20,
        actualTargetDisplay: '40 / 42 hợp đồng',
        targetDisplay: '42 hợp đồng',
        actualDisplay: '40 hợp đồng',
        score: 95.2,
        weightedScore: 19.0,
        hasDrilldown: true,
        kpiDefinitionId: 'crit-cards',
      },
      {
        id: 'crit-service',
        name: 'Chất lượng dịch vụ',
        weightPercent: 20,
        actualTargetDisplay: '4,3 / 4,5 điểm',
        targetDisplay: '4,5 điểm',
        actualDisplay: '4,3 điểm',
        score: 95.6,
        weightedScore: 19.1,
        hasDrilldown: true,
        kpiDefinitionId: 'crit-service',
      },
      {
        id: 'crit-qualitative',
        name: 'Đánh giá định tính (Trưởng phòng)',
        weightPercent: 20,
        actualTargetDisplay: '—',
        targetDisplay: '—',
        actualDisplay: '—',
        score: 86.0,
        weightedScore: 17.2,
        hasDrilldown: false,
        kpiDefinitionId: null,
      },
    ],
    criteriaGroups: [
      {
        evaluationProfileItemId: 'grp-business',
        groupName: 'Kinh doanh',
        weightPercent: 60,
        groupScore: 91.4,
        criteria: [
          {
            id: 'crit-deposit',
            name: 'Sales – Tiền gửi – xử lý',
            weightPercent: 40,
            actualTargetDisplay: '4.920.000.000 / 5.500.000.000 VND',
            targetDisplay: '5.500.000.000 VND',
            actualDisplay: '4.920.000.000 VND',
            score: 89.5,
            weightedScore: 35.8,
            hasDrilldown: true,
            kpiDefinitionId: 'crit-deposit',
          },
          {
            id: 'crit-cards',
            name: 'Sales – Mở thẻ – xử lý',
            weightPercent: 20,
            actualTargetDisplay: '40 / 42 hợp đồng',
            targetDisplay: '42 hợp đồng',
            actualDisplay: '40 hợp đồng',
            score: 95.2,
            weightedScore: 19.0,
            hasDrilldown: true,
            kpiDefinitionId: 'crit-cards',
          },
        ],
      },
      {
        evaluationProfileItemId: 'grp-service',
        groupName: 'Chất lượng dịch vụ',
        weightPercent: 20,
        groupScore: 95.6,
        criteria: [
          {
            id: 'crit-service',
            name: 'Chất lượng dịch vụ',
            weightPercent: 20,
            actualTargetDisplay: '4,3 / 4,5 điểm',
            targetDisplay: '4,5 điểm',
            actualDisplay: '4,3 điểm',
            score: 95.6,
            weightedScore: 19.1,
            hasDrilldown: true,
            kpiDefinitionId: 'crit-service',
          },
        ],
      },
      {
        evaluationProfileItemId: 'grp-qualitative',
        groupName: 'Tuân thủ & Kỷ luật',
        weightPercent: 20,
        groupScore: 86.0,
        criteria: [
          {
            id: 'crit-qualitative',
            name: 'Đánh giá định tính (Trưởng phòng)',
            weightPercent: 20,
            actualTargetDisplay: '—',
            targetDisplay: '—',
            actualDisplay: '—',
            score: 86.0,
            weightedScore: 17.2,
            hasDrilldown: false,
            kpiDefinitionId: null,
          },
        ],
      },
    ],
    qualityRatio: 0.9,
    riskPenalty: 0,
    criteriaSummary: {
      achievedCount: 3,
      nearCount: 0,
      belowCount: 1,
      totalCount: 4,
      achievedRatePercent: 75.0,
    },
    slaSummary: {
      onTimeCompletedCount: 17,
      overdueCount: 3,
      inProgressCount: 2,
      onTimeRatePercent: 85.0,
    },
    riskCount: 0,
    riskEvents: [],
    managerFeedback: 'Dẫn dắt tốt chiến dịch thẻ Q2; cần cải thiện tỷ lệ chốt tiền gửi.',
  };

  const mockPeriods: EvaluationPeriodOption[] = [
    {
      id: '2026-Q2',
      code: '2026-Q2',
      name: 'Quý 2/2026',
      startDate: '2026-04-01',
      endDate: '2026-06-30',
      periodType: 'QUARTER',
      status: 'IN_REVIEW',
      isActive: true,
    },
    {
      id: '2026-Q1',
      code: '2026-Q1',
      name: 'Quý 1/2026',
      startDate: '2026-01-01',
      endDate: '2026-03-31',
      periodType: 'QUARTER',
      status: 'CLOSED',
      isActive: false,
    },
    {
      id: '2026-M05',
      code: '2026-05',
      name: 'Tháng 05/2026',
      startDate: '2026-05-01',
      endDate: '2026-05-31',
      periodType: 'MONTH',
      status: 'IN_REVIEW',
      isActive: false,
    },
    {
      id: '2025-Y',
      code: '2025',
      name: 'Năm 2025',
      startDate: '2025-01-01',
      endDate: '2025-12-31',
      periodType: 'YEAR',
      status: 'CLOSED',
      isActive: false,
    },
  ];

  const mockLedgerItems: PersonalCriteriaLedgerItem[] = [
    {
      id: 'tx-1',
      txCode: 'TX-20260405-001',
      date: '05/04/2026',
      amount: '1.500.000.000 VND',
      kpiContribution: 10.5,
      status: 'MATCHED',
      occurredOn: '2026-04-05',
      workId: 'work-1',
      workCode: 'W-TX-1',
      workTitle: 'Giao dich tiet kiem',
    },
    {
      id: 'tx-2',
      txCode: 'AP-0000002',
      date: '20/04/2026',
      amount: '2',
      kpiContribution: 2,
      status: 'VALID',
      occurredOn: '2026-04-20',
    },
  ];

  const mockEvaluationService = {
    periods: () => of(mockPeriods),
    getPersonalEvaluation: (periodId?: string) =>
      of(
        periodId === '2026-Q1'
          ? {
              ...mockEvalData,
              periodId: '2026-Q1',
              periodName: 'Quý 1/2026',
              totalPeScore: 78.5,
              tier: 'B',
            }
          : mockEvalData,
      ),
    getPersonalCriteriaLedger: () => of(mockLedgerItems),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyEvaluationComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: mockUser.asReadonly(),
          },
        },
        {
          provide: EvaluationService,
          useValue: mockEvaluationService,
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    fixture = TestBed.createComponent(MyEvaluationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders page header and published status badge', () => {
    const titleEl = fixture.debugElement.query(By.css('.ui-page-title'));
    expect(titleEl).toBeTruthy();
    expect(titleEl.nativeElement.textContent).toContain('2026');

    const badgeEl = fixture.debugElement.query(By.css('app-status-badge .badge'));
    expect(badgeEl).toBeTruthy();
    expect(badgeEl.nativeElement.textContent).toContain('Published');
  });

  it('renders 5 summary metric cards with initial Q2 values matching /form plus peer ranking in side column', () => {
    const cards = fixture.debugElement.queryAll(By.css('.metric-grid app-metric-card'));
    expect(cards.length).toBe(5);

    const sideRankingCard = fixture.debugElement.query(By.css('.side-column app-metric-card'));
    expect(sideRankingCard).toBeTruthy();

    const values = fixture.debugElement.queryAll(By.css('.metric-value'));
    expect(values[0].nativeElement.textContent.trim()).toBe('82,00');
    expect(values[1].nativeElement.textContent.trim()).toBe('3 / 4');
    expect(values[2].nativeElement.textContent.trim()).toBe('85,0%');
    expect(values[3].nativeElement.textContent.trim()).toBe('0');
    expect(values[4].nativeElement.textContent.trim()).toBe('Hạng A2');
    expect(values[4].classes['tier-a']).toBeTrue();
    expect(values[5].nativeElement.textContent.trim()).toBe('Top 40%');
  });

  it('renders all KPI criteria table rows, 6 ordered columns, and modifiers', () => {
    const headers = fixture.debugElement.queryAll(By.css('.kpi-table thead th'));
    expect(headers.length).toBe(6);

    const groupRows = fixture.debugElement.queryAll(By.css('.kpi-table tbody .group-row'));
    expect(groupRows.length).toBe(3);
    expect(groupRows[0].nativeElement.textContent).toContain('Kinh doanh');
    expect(groupRows[1].nativeElement.textContent).toContain('Chất lượng dịch vụ');
    expect(groupRows[2].nativeElement.textContent).toContain('Tuân thủ & Kỷ luật');

    const rows = fixture.debugElement.queryAll(By.css('.kpi-table tbody .kpi-row'));
    expect(rows.length).toBe(4);

    const firstRowCells = rows[0].queryAll(By.css('td'));
    expect(firstRowCells[0].query(By.css('.criteria-link-btn'))).toBeTruthy();
    expect(firstRowCells[0].nativeElement.textContent).toContain('Sales – Tiền gửi – xử lý');
    expect(firstRowCells[2].nativeElement.textContent.trim()).toBe('5.500.000.000 VND');
    expect(firstRowCells[3].nativeElement.textContent.trim()).toBe('4.920.000.000 VND');

    expect(fixture.debugElement.queryAll(By.css('.risk-table-row')).length).toBe(1);
    expect(fixture.debugElement.queryAll(By.css('.total-row')).length).toBe(1);
    const totalCells = fixture.debugElement.query(By.css('.total-row')).queryAll(By.css('td'));
    expect(totalCells[totalCells.length - 1].nativeElement.textContent.trim()).toBe('82,00');
  });

  it('switches evaluation period when selecting from dropdown', () => {
    component.onPeriodChange('2026-Q1');
    fixture.detectChanges();

    const values = fixture.debugElement.queryAll(By.css('.metric-value'));
    expect(values[0].nativeElement.textContent.trim()).toBe('78,50');
    expect(values[4].nativeElement.textContent.trim()).toBe('Hạng B');
    expect(values[4].classes['tier-b']).toBeTrue();
  });

  it('does not render an appeal button on the personal evaluation page', () => {
    const appealBtn = fixture.debugElement.query(By.css('.appeal-btn'));
    expect(appealBtn).toBeNull();
  });

  it('opens and closes drilldown ledger modal for clickable KPI rows', () => {
    const depositCrit = component['evaluation']()?.criteria[0];
    expect(depositCrit).toBeTruthy();
    component.openDrilldown(depositCrit!);
    fixture.detectChanges();

    const ledgerDialog = fixture.debugElement.query(By.css('.ledger-dialog'));
    expect(ledgerDialog).toBeTruthy();
    expect(component['ledgerItems']().length).toBeGreaterThan(0);

    // Escape closes drilldown
    component.onEscape();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ledger-dialog'))).toBeFalsy();
  });

  it('filters the ledger by keyword, status and date', () => {
    component.openDrilldown(component['evaluation']()!.criteria[0]);
    fixture.detectChanges();
    expect(component.filteredLedgerItems().length).toBe(2);

    component.ledgerKeyword.set('tiet kiem');
    expect(component.filteredLedgerItems().map((tx) => tx.id)).toEqual(['tx-1']);
    component.ledgerKeyword.set('ap-000');
    expect(component.filteredLedgerItems().map((tx) => tx.id)).toEqual(['tx-2']);

    component.ledgerKeyword.set('');
    component.ledgerStatus.set('MATCHED');
    expect(component.filteredLedgerItems().map((tx) => tx.id)).toEqual(['tx-1']);

    component.ledgerStatus.set('');
    component.ledgerDateFrom.set('2026-04-10');
    expect(component.filteredLedgerItems().map((tx) => tx.id)).toEqual(['tx-2']);
    component.ledgerDateFrom.set('');
    component.ledgerDateTo.set('2026-04-10');
    expect(component.filteredLedgerItems().map((tx) => tx.id)).toEqual(['tx-1']);

    component.ledgerKeyword.set('zzz');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ledger-dialog .muted-empty'))).toBeTruthy();
  });

  it('opens the work of a ledger transaction with its code searched', () => {
    component.openDrilldown(component['evaluation']()!.criteria[0]);
    fixture.detectChanges();

    const [linked, unlinked] = component.ledgerItems();
    component.openLedgerWork(unlinked);
    expect(router.navigate).not.toHaveBeenCalled();

    component.openLedgerWork(linked);
    expect(router.navigate).toHaveBeenCalledWith(['/my-work'], {
      queryParams: { keyword: 'W-TX-1', workId: 'work-1' },
    });
    expect(component.selectedDrilldownCriteria()).toBeNull();
  });

  it('maps tier color classes and splits legacy actualTargetDisplay fallback cleanly', () => {
    expect(component.tierToneClass('A1')).toBe('tier-a');
    expect(component.tierToneClass('a2')).toBe('tier-a');
    expect(component.tierToneClass('A3')).toBe('tier-a');
    expect(component.tierToneClass('B')).toBe('tier-b');
    expect(component.tierToneClass('C')).toBe('tier-c');
    expect(component.tierToneClass(null)).toBe('');

    const legacyItem = {
      id: 'legacy',
      name: 'Legacy KPI',
      weightPercent: 10,
      actualTargetDisplay: '91 / 100 tỷ VND',
      score: 91,
      weightedScore: 9.1,
      hasDrilldown: false,
    };
    expect(component.formatTarget(legacyItem)).toBe('100 tỷ VND');
    expect(component.formatActual(legacyItem)).toBe('91 tỷ VND');
  });

  it('localizes ledger status badges and renders work column in English', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    try {
      expect(component.ledgerStatusLabel('MATCHED')).toBe('Matched');
      expect(component.ledgerStatusLabel('IN_REVIEW')).toBe('In review');
      expect(component.ledgerStatusLabel('NOT_MATCHED')).toBe('Not matched');
      expect(component.ledgerStatusClass('VALID')).toBe('ledger-status-ok');
      expect(component.ledgerStatusClass('IN_REVIEW')).toBe('ledger-status-review');
      expect(component.ledgerStatusClass('NOT_MATCHED')).toBe('ledger-status-rejected');

      component.openDrilldown(component['evaluation']()!.criteria[0]);
      fixture.detectChanges();

      const modalText = fixture.debugElement.query(By.css('.ledger-dialog')).nativeElement
        .textContent;
      expect(modalText).toContain('Work');
      expect(modalText).toContain('View work progress');
      expect(modalText).toContain('No linked work');
    } finally {
      i18n.setLanguage('vi');
    }
  });

  it('does not open drilldown for qualitative criteria', () => {
    const qualCrit = component['evaluation']()?.criteria[3];
    expect(qualCrit).toBeTruthy();
    expect(qualCrit!.hasDrilldown).toBeFalse();

    component.openDrilldown(qualCrit!);
    expect(component['selectedDrilldownCriteria']()).toBeNull();
  });

  it('switches evaluation via year, quarter and month filters', () => {
    const getEvalSpy = spyOn(mockEvaluationService, 'getPersonalEvaluation').and.callThrough();

    component.onMonthChange(5);
    expect(getEvalSpy).toHaveBeenCalledWith('2026-M05');

    component.onQuarterChange(1);
    expect(getEvalSpy).toHaveBeenCalledWith('2026-Q1');

    component.onYearChange(2025);
    expect(getEvalSpy).toHaveBeenCalledWith('2025-Y');
  });

  it('shows error state and retries loading when evaluation request fails', () => {
    const getEvalSpy = spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      throwError(() => new Error('Network error')),
    );

    component.loadEvaluation('2026-Q2');
    fixture.detectChanges();

    expect(component.loadFailed()).toBeTrue();
    expect(fixture.debugElement.query(By.css('.state-card[role="alert"]'))).toBeTruthy();

    getEvalSpy.and.returnValue(of(mockEvalData));
    fixture.debugElement.query(By.css('.state-card .button-secondary')).nativeElement.click();
    fixture.detectChanges();

    expect(component.loadFailed()).toBeFalse();
    expect(component.evaluation()?.periodId).toBe('2026-Q2');
  });

  it('shows modal error when ledger drilldown fails', () => {
    spyOn(mockEvaluationService, 'getPersonalCriteriaLedger').and.returnValue(
      throwError(() => new Error('Server error')),
    );

    const depositCrit = component['evaluation']()?.criteria[0];
    component.openDrilldown(depositCrit!);
    fixture.detectChanges();

    expect(component.ledgerLoading()).toBeFalse();
    expect(component.ledgerError()).toBeTrue();
    expect(fixture.debugElement.query(By.css('.ledger-dialog .modal-error'))).toBeTruthy();
  });

  it('renders downward trend styling when scoreDelta is negative', () => {
    spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      of({ ...mockEvalData, scoreDelta: -2.4 }),
    );

    component.loadEvaluation('2026-Q2');
    fixture.detectChanges();

    const deltaBadge = fixture.debugElement.query(By.css('.metric-pill.pill-warning'));
    expect(deltaBadge).toBeTruthy();
    expect(deltaBadge.nativeElement.textContent).toContain('▼');
    expect(deltaBadge.nativeElement.textContent).toContain('2,4');
  });

  it('renders empty tier "-" and no tier pill when status is not FINAL', () => {
    spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      of({ ...mockEvalData, status: 'CALCULATED', tier: 'A2' }),
    );

    component.loadEvaluation('2026-Q2');
    fixture.detectChanges();

    const values = fixture.debugElement.queryAll(By.css('.metric-value'));
    expect(values[4].nativeElement.textContent.trim()).toBe('-');
    expect(values[4].classes['tier-a']).toBeFalsy();
    expect(component.tierPillText()).toBeNull();
    expect(values[5].nativeElement.textContent.trim()).toBe('-');
    expect(component.departmentRankingDetail()).toBeNull();
  });

  it('localizes status, deadline, period name, role title, and units cleanly in English', () => {
    spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      of({
        ...mockEvalData,
        status: 'FINAL',
        appealDeadlineText: '23:59 ngày 31/12/2026',
        appealRemainingDays: 52,
        rankingRoleTitle: 'Chuyên viên Kinh doanh trong phòng',
        criteria: [
          {
            ...mockEvalData.criteria[0],
            actualTargetDisplay: '261 / 450 tỷ VND',
            targetDisplay: '450 tỷ VND',
            actualDisplay: '261 tỷ VND',
          },
        ],
        criteriaGroups: [
          {
            ...mockEvalData.criteriaGroups![0],
            criteria: [
              {
                ...mockEvalData.criteriaGroups![0].criteria[0],
                actualTargetDisplay: '261 / 450 tỷ VND',
                targetDisplay: '450 tỷ VND',
                actualDisplay: '261 tỷ VND',
              },
            ],
          },
        ],
      }),
    );

    const i18n = TestBed.inject(I18nService);
    component.loadEvaluation('2026-Q2');
    i18n.setLanguage('en');
    fixture.detectChanges();

    try {
      const titleText = fixture.debugElement.query(By.css('.ui-page-title')).nativeElement
        .textContent;
      const badgeText = fixture.debugElement.query(By.css('app-status-badge .badge')).nativeElement
        .textContent;
      const deadlineText = fixture.debugElement.query(By.css('.appeal-deadline-notice'))
        .nativeElement.textContent;
      const metaText = fixture.debugElement.query(By.css('.table-card-meta')).nativeElement
        .textContent;
      const riskUnitText = fixture.debugElement.queryAll(By.css('.metric-scale'))[1].nativeElement
        .textContent;
      const rankingDetailText = fixture.debugElement.queryAll(By.css('.metric-scale'))[2]
        .nativeElement.textContent;
      const targetText = fixture.debugElement.query(
        By.css('.kpi-table tbody .kpi-row td:nth-child(3)'),
      ).nativeElement.textContent;
      const actualText = fixture.debugElement.query(
        By.css('.kpi-table tbody .kpi-row td:nth-child(4)'),
      ).nativeElement.textContent;

      expect(titleText).toContain('Q2/2026');
      expect(badgeText.trim()).toBe('Final');
      expect(deadlineText).toContain(
        'Evaluation results and tier ranking have been officially recorded in personnel files.',
      );
      expect(metaText).toContain('Sales Officer');
      expect(metaText).toContain('Calculated automatically');
      expect(metaText).not.toContain('Tự động tổng hợp');
      expect(riskUnitText).toContain('event(s)');
      expect(rankingDetailText).toContain('Sales Officer in department');
      expect(rankingDetailText).not.toContain('trong phòng');
      expect(targetText).toContain('450 B VND');
      expect(actualText).toContain('261 B VND');
    } finally {
      i18n.setLanguage('vi');
    }
  });

  it('hides metrics and table when evaluation status is DRAFT and displays not calculated notice', () => {
    const evalService = TestBed.inject(EvaluationService);
    spyOn(evalService, 'getPersonalEvaluation').and.returnValue(
      of({
        ...mockEvalData,
        status: 'DRAFT',
        tier: null,
        rankingPercentile: null,
      }),
    );

    component.loadEvaluation('2026-11');
    fixture.detectChanges();

    const uncalculatedCard = fixture.debugElement.query(By.css('.uncalculated-card'));
    const metricGrid = fixture.debugElement.query(By.css('.metric-grid'));
    const tableCard = fixture.debugElement.query(By.css('.table-card'));
    const notice = fixture.debugElement.query(By.css('.appeal-deadline-notice'));

    expect(uncalculatedCard).not.toBeNull();
    expect(metricGrid).toBeNull();
    expect(tableCard).toBeNull();
    expect(notice.nativeElement.textContent).toContain(
      'Kỳ đánh giá này chưa được tính toán số liệu.',
    );
  });

  it('renders correct notice for CALCULATED, DISPUTED, SUBMITTED and FINAL statuses', () => {
    const evalService = TestBed.inject(EvaluationService);
    // 1. CALCULATED with active appeal window
    spyOn(evalService, 'getPersonalEvaluation').and.returnValue(
      of({
        ...mockEvalData,
        status: 'CALCULATED',
        canAppeal: true,
        appealRemainingDays: 2,
        appealDeadlineText: '23:59 ngày 25/11/2026',
      }),
    );
    component.loadEvaluation('2026-11');
    fixture.detectChanges();

    let notice = fixture.debugElement.query(By.css('.appeal-deadline-notice'));
    expect(notice.nativeElement.textContent).toContain(
      'Kết quả sơ bộ đang kiểm kê, vui lòng tự kiểm tra và rà soát kết quả',
    );
    expect(notice.nativeElement.textContent).toContain('25/11/2026');

    // 2. DISPUTED
    (evalService.getPersonalEvaluation as jasmine.Spy).and.returnValue(
      of({
        ...mockEvalData,
        status: 'DISPUTED',
      }),
    );
    component.loadEvaluation('2026-11');
    fixture.detectChanges();
    notice = fixture.debugElement.query(By.css('.appeal-deadline-notice'));
    expect(notice.nativeElement.textContent).toContain('Đang xử lý khiếu nại');

    // 3. SUBMITTED
    (evalService.getPersonalEvaluation as jasmine.Spy).and.returnValue(
      of({
        ...mockEvalData,
        status: 'SUBMITTED',
      }),
    );
    component.loadEvaluation('2026-11');
    fixture.detectChanges();
    notice = fixture.debugElement.query(By.css('.appeal-deadline-notice'));
    expect(notice.nativeElement.textContent).toContain('gửi lên Giám đốc chi nhánh');

    // 4. FINAL
    (evalService.getPersonalEvaluation as jasmine.Spy).and.returnValue(
      of({
        ...mockEvalData,
        status: 'FINAL',
      }),
    );
    component.loadEvaluation('2026-11');
    fixture.detectChanges();
    notice = fixture.debugElement.query(By.css('.appeal-deadline-notice'));
    expect(notice.nativeElement.textContent).toContain('ghi nhận chính thức vào hồ sơ nhân sự');
  });
});
