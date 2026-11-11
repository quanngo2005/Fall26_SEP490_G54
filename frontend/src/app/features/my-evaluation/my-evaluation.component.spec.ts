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
    status: 'Published',
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
    qualityRatio: 0.9,
    riskPenalty: 0,
    managerFeedback: 'Dẫn dắt tốt chiến dịch thẻ Q2; cần cải thiện tỷ lệ chốt tiền gửi.',
    isAcknowledged: false,
    acknowledgedAt: null,
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
    acknowledgePersonalEvaluation: () =>
      of({
        success: true,
        acknowledgedAt: '2026-07-20T10:30:00Z',
        message: 'ok',
      }),
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
    const titleEl = fixture.debugElement.query(By.css('.eval-title'));
    expect(titleEl).toBeTruthy();
    expect(titleEl.nativeElement.textContent).toContain('2026');

    const badgeEl = fixture.debugElement.query(By.css('.badge-published'));
    expect(badgeEl).toBeTruthy();
    expect(badgeEl.nativeElement.textContent).toContain('Published');
  });

  it('renders 3 summary metric cards with initial Q2 values', () => {
    const heroScore = fixture.debugElement.query(By.css('.card-hero-score'));
    expect(heroScore).toBeTruthy();
    expect(heroScore.nativeElement.textContent.trim()).toBe('82,0');

    const heroTier = fixture.debugElement.query(By.css('.tier-hero-pill'));
    expect(heroTier.nativeElement.textContent.trim()).toBe('A2');
    expect(heroTier.classes['tier-a2']).toBeTrue();
    expect(fixture.debugElement.query(By.css('.tier-track'))).toBeNull();

    const rankingVal = fixture.debugElement.query(By.css('.ranking-hero-val'));
    expect(rankingVal.nativeElement.textContent.trim()).toBe('Top 40%');
  });

  it('renders all KPI criteria table rows, 6 ordered columns, and modifiers', () => {
    const headers = fixture.debugElement.queryAll(By.css('.evaluation-table thead th'));
    expect(headers.map((h) => h.nativeElement.className)).toEqual([
      'th-criteria',
      'th-target',
      'th-actual',
      'th-score',
      'th-weight',
      'th-weighted-score',
    ]);

    const rows = fixture.debugElement.queryAll(By.css('.criteria-row'));
    expect(rows.length).toBe(4);

    const firstRowCriteria = rows[0].query(By.css('.criteria-link-btn'));
    expect(firstRowCriteria.nativeElement.textContent).toContain('Sales – Tiền gửi – xử lý');
    expect(rows[0].query(By.css('.col-target')).nativeElement.textContent.trim()).toBe(
      '5.500.000.000 VND',
    );
    expect(rows[0].query(By.css('.col-actual')).nativeElement.textContent.trim()).toBe(
      '4.920.000.000 VND',
    );

    const modifierRows = fixture.debugElement.queryAll(By.css('.modifier-row'));
    expect(modifierRows.length).toBe(2);

    const totalRow = fixture.debugElement.query(By.css('.total-row .total-score'));
    expect(totalRow.nativeElement.textContent.trim()).toBe('82,0');
  });

  it('switches evaluation period when selecting from dropdown', () => {
    component.onPeriodChange('2026-Q1');
    fixture.detectChanges();

    const heroScore = fixture.debugElement.query(By.css('.card-hero-score'));
    expect(heroScore.nativeElement.textContent.trim()).toBe('78,5');

    const heroTier = fixture.debugElement.query(By.css('.tier-hero-pill'));
    expect(heroTier.nativeElement.textContent.trim()).toBe('B');
    expect(heroTier.classes['tier-b']).toBeTrue();
  });

  it('opens acknowledge modal and confirms acknowledgment (UC108)', () => {
    const ackBtn = fixture.debugElement.query(By.css('.ack-btn'));
    expect(ackBtn).toBeTruthy();

    ackBtn.nativeElement.click();
    fixture.detectChanges();

    const dialog = fixture.debugElement.query(By.css('.ack-dialog'));
    expect(dialog).toBeTruthy();

    // Confirm acknowledgment
    component.confirmAcknowledge();
    fixture.detectChanges();

    expect(component['evaluation']()?.isAcknowledged).toBeTrue();
    expect(fixture.debugElement.query(By.css('.alert-success'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.badge-acknowledged'))).toBeTruthy();
  });

  it('cancels acknowledge modal', () => {
    component.openAcknowledgeModal();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ack-dialog'))).toBeTruthy();

    component.closeAcknowledgeModal();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ack-dialog'))).toBeFalsy();
  });

  it('navigates to appeals when clicking appeal button', () => {
    const appealBtn = fixture.debugElement.query(By.css('.appeal-btn'));
    appealBtn.nativeElement.click();

    expect(router.navigate).toHaveBeenCalledWith(['/appeals/mine']);
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
    expect(component.tierBadgeClass('A1')).toBe('tier-a1');
    expect(component.tierBadgeClass('a2')).toBe('tier-a2');
    expect(component.tierBadgeClass('A3')).toBe('tier-a3');
    expect(component.tierBadgeClass('B')).toBe('tier-b');
    expect(component.tierBadgeClass('C')).toBe('tier-c');
    expect(component.tierBadgeClass(null)).toBe('tier-default');

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
      expect(component.ledgerStatusLabel('IN_REVIEW')).toBe('Pending verification');
      expect(component.ledgerStatusLabel('NOT_MATCHED')).toBe('Rejected');
      expect(component.ledgerStatusClass('VALID')).toBe('ledger-status-ok');
      expect(component.ledgerStatusClass('IN_REVIEW')).toBe('ledger-status-review');
      expect(component.ledgerStatusClass('NOT_MATCHED')).toBe('ledger-status-rejected');

      component.openDrilldown(component['evaluation']()!.criteria[0]);
      fixture.detectChanges();

      const modalText = fixture.debugElement.query(By.css('.ledger-dialog')).nativeElement
        .textContent;
      expect(modalText).toContain('Task');
      expect(modalText).toContain('Open task');
      expect(modalText).toContain('Direct progress');
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

    component.onMonthChange({ target: { value: '5' } } as unknown as Event);
    expect(getEvalSpy).toHaveBeenCalledWith('2026-M05');

    component.onQuarterChange({ target: { value: '1' } } as unknown as Event);
    expect(getEvalSpy).toHaveBeenCalledWith('2026-Q1');

    component.onYearChange({ target: { value: '2025' } } as unknown as Event);
    expect(getEvalSpy).toHaveBeenCalledWith('2025-Y');
  });

  it('shows error state and retries loading when evaluation request fails', () => {
    const getEvalSpy = spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      throwError(() => new Error('Network error')),
    );

    component.loadEvaluation('2026-Q2');
    fixture.detectChanges();

    expect(component.loadFailed()).toBeTrue();
    expect(fixture.debugElement.query(By.css('.error-state'))).toBeTruthy();

    getEvalSpy.and.returnValue(of(mockEvalData));
    fixture.debugElement.query(By.css('.error-state .btn-primary')).nativeElement.click();
    fixture.detectChanges();

    expect(component.loadFailed()).toBeFalse();
    expect(component.evaluation()?.periodId).toBe('2026-Q2');
  });

  it('shows modal error when acknowledge fails and keeps modal open', () => {
    spyOn(mockEvaluationService, 'acknowledgePersonalEvaluation').and.returnValue(
      throwError(() => new Error('Server error')),
    );

    component.openAcknowledgeModal();
    fixture.detectChanges();
    component.confirmAcknowledge();
    fixture.detectChanges();

    expect(component.acknowledging()).toBeFalse();
    expect(component.acknowledgeError()).toBeTrue();
    expect(fixture.debugElement.query(By.css('.ack-dialog .modal-error'))).toBeTruthy();
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

    const deltaBadge = fixture.debugElement.query(By.css('.card-delta-badge'));
    expect(deltaBadge.classes['text-negative']).toBeTrue();
    expect(deltaBadge.nativeElement.textContent).toContain('↓');
    expect(deltaBadge.nativeElement.textContent).toContain('2,4');
  });

  it('localizes status, deadline, period name, role title, and units cleanly in English', () => {
    spyOn(mockEvaluationService, 'getPersonalEvaluation').and.returnValue(
      of({
        ...mockEvalData,
        status: 'CALCULATED',
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
      }),
    );

    const i18n = TestBed.inject(I18nService);
    component.loadEvaluation('2026-Q2');
    i18n.setLanguage('en');
    fixture.detectChanges();

    try {
      const titleText = fixture.debugElement.query(By.css('.eval-title')).nativeElement.textContent;
      const badgeText = fixture.debugElement.query(By.css('.badge-published')).nativeElement
        .textContent;
      const deadlineText = fixture.debugElement.query(By.css('.appeal-deadline-notice'))
        .nativeElement.textContent;
      const rankingText = fixture.debugElement.query(By.css('.ranking-sub-text')).nativeElement
        .textContent;
      const targetText = fixture.debugElement.query(By.css('.col-target')).nativeElement
        .textContent;
      const actualText = fixture.debugElement.query(By.css('.col-actual')).nativeElement
        .textContent;

      expect(titleText).toContain('Q2/2026');
      expect(badgeText.trim()).toBe('Calculated');
      expect(deadlineText).toContain('23:59 on 31/12/2026');
      expect(deadlineText).not.toContain('ngày');
      expect(rankingText).toContain('Sales Officer in department');
      expect(rankingText).not.toContain('trong phòng');
      expect(targetText).toContain('450 B VND');
      expect(actualText).toContain('261 B VND');
    } finally {
      i18n.setLanguage('vi');
    }
  });
});
