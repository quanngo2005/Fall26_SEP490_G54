import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppNumberPipe } from '../../shared/pipes';
import {
  MetricCardComponent,
  RiskSummaryCardComponent,
  StatusBadgeComponent,
  UiSelectComponent,
} from '../../shared/ui';
import type { SelectOption, StatusBadgeTone } from '../../shared/ui';
import {
  findQuarterPeriod,
  findYearPeriod,
  periodQuarter,
  periodYear,
} from '../target-overview/target-overview.utils';
import {
  EvaluationPeriodOption,
  PersonalCriteriaLedgerItem,
  PersonalEvaluationCriteriaItem,
  PersonalEvaluationView,
} from './my-evaluation.models';

/** The period levels the year / quarter / month filter drills through. */
const SELECTABLE_PERIOD_TYPES = ['YEAR', 'QUARTER', 'MONTH'];

const STATUS_KEY_MAP: Readonly<Record<string, string>> = {
  DRAFT: 'statusDraft',
  CALCULATED: 'statusCalculated',
  SUBMITTED: 'statusSubmitted',
  DISPUTED: 'statusDisputed',
  FINAL: 'statusFinal',
  PUBLISHED: 'statusPublished',
};

const LEDGER_STATUS_KEY_MAP: Readonly<Record<string, string>> = {
  VALID: 'ledgerVerified',
  MATCHED: 'ledgerStatusMatched',
  IN_REVIEW: 'ledgerStatusInReview',
  NOT_MATCHED: 'ledgerStatusNotMatched',
};

const LEDGER_STATUS_CLASS_MAP: Readonly<Record<string, string>> = {
  IN_REVIEW: 'ledger-status-review',
  NOT_MATCHED: 'ledger-status-rejected',
};

const ROLE_KEY_MAP: Readonly<Record<string, string>> = {
  'Chuyên viên Kinh doanh': 'roleSalesOfficer',
  'Giao dịch viên': 'roleTeller',
  'Trưởng phòng': 'roleDepartmentManager',
  'Nhân viên': 'roleEmployee',
};

const UNIT_KEY_MAP: readonly [RegExp, string][] = [
  [/\btỷ VND\b/g, 'unitBillionVnd'],
  [/\bkhách hàng\b/g, 'unitCustomers'],
  [/\bhợp đồng\b/g, 'unitContracts'],
  [/\bđiểm\b/g, 'unitPoints'],
];

const STATUS_TONE_MAP: Readonly<Record<string, StatusBadgeTone>> = {
  DRAFT: 'warning',
  CALCULATED: 'info',
  SUBMITTED: 'info',
  DISPUTED: 'danger',
  FINAL: 'success',
  PUBLISHED: 'success',
};

function periodMonth(period: EvaluationPeriodOption): number {
  return Number(period.startDate.slice(5, 7));
}

@Component({
  selector: 'app-my-evaluation',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    AppNumberPipe,
    StatusBadgeComponent,
    UiSelectComponent,
    MetricCardComponent,
    RiskSummaryCardComponent,
  ],
  templateUrl: './my-evaluation.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../evaluations/evaluation-form.component.scss',
    '../evaluations/evaluation-form.layout.scss',
    '../../shared/ui/metric-cards.scss',
    '../../shared/ui/side-cards.scss',
    './my-evaluation.component.scss',
    './my-evaluation.dialogs.scss',
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyEvaluationComponent extends LocalizedComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly evaluationService = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly periods = signal<EvaluationPeriodOption[]>([]);
  readonly year = signal<number | null>(null);
  /** 0 = the whole year. */
  readonly quarter = signal(0);
  /** 0 = the whole quarter. */
  readonly month = signal(0);

  readonly evaluation = signal<PersonalEvaluationView | null>(null);
  readonly loading = signal<boolean>(true);
  readonly loadFailed = signal<boolean>(false);

  readonly selectedDrilldownCriteria = signal<PersonalEvaluationCriteriaItem | null>(null);
  readonly ledgerItems = signal<readonly PersonalCriteriaLedgerItem[]>([]);
  readonly ledgerLoading = signal<boolean>(false);
  readonly ledgerError = signal<boolean>(false);
  readonly ledgerKeyword = signal('');
  readonly ledgerStatus = signal('');
  readonly ledgerDateFrom = signal('');
  readonly ledgerDateTo = signal('');

  readonly ledgerStatuses = computed(() => [...new Set(this.ledgerItems().map((tx) => tx.status))]);

  readonly filteredLedgerItems = computed(() => {
    const keyword = this.ledgerKeyword().trim().toLowerCase();
    const status = this.ledgerStatus();
    const from = this.ledgerDateFrom();
    const to = this.ledgerDateTo();
    return this.ledgerItems().filter(
      (tx) =>
        (!keyword ||
          [tx.txCode, tx.workCode, tx.workTitle].some((text) =>
            text?.toLowerCase().includes(keyword),
          )) &&
        (!status || tx.status === status) &&
        (!from || tx.occurredOn >= from) &&
        (!to || tx.occurredOn <= to),
    );
  });

  private evalSubscription?: Subscription;
  private ledgerSubscription?: Subscription;

  readonly years = computed(() =>
    [...new Set(this.periods().map((period) => periodYear(period)))].sort((a, b) => b - a),
  );

  readonly hasYearPeriod = computed(() => {
    const year = this.year();
    return year !== null && !!findYearPeriod(this.periods(), year);
  });

  readonly quarterOptions = computed(() => {
    const year = this.year();
    return [
      ...new Set(
        this.periods()
          .filter((period) => period.periodType !== 'YEAR' && periodYear(period) === year)
          .map((period) => periodQuarter(period)),
      ),
    ].sort((a, b) => a - b);
  });

  readonly hasQuarterPeriod = computed(() => {
    const year = this.year();
    return (
      year !== null &&
      this.quarter() !== 0 &&
      !!findQuarterPeriod(this.periods(), year, this.quarter())
    );
  });

  readonly monthOptions = computed(() => {
    const year = this.year();
    if (year === null || this.quarter() === 0) return [];
    return this.periods()
      .filter(
        (period) =>
          period.periodType === 'MONTH' &&
          periodYear(period) === year &&
          periodQuarter(period) === this.quarter(),
      )
      .map((period) => periodMonth(period))
      .sort((a, b) => a - b);
  });

  /** The most specific level chosen: a month, else the quarter, else the year. */
  readonly selectedPeriod = computed(() => {
    const periods = this.periods();
    const year = this.year();
    if (year === null) return null;
    if (this.quarter() === 0) return findYearPeriod(periods, year) ?? null;
    if (this.month() === 0) return findQuarterPeriod(periods, year, this.quarter()) ?? null;
    return (
      periods.find(
        (period) =>
          period.periodType === 'MONTH' &&
          periodYear(period) === year &&
          periodMonth(period) === this.month(),
      ) ?? null
    );
  });

  readonly selectedPeriodId = computed(() => this.selectedPeriod()?.id ?? '');

  readonly yearSelectOptions = computed<SelectOption<number>[]>(() =>
    this.years().map((year) => ({ value: year, label: String(year) })),
  );

  readonly quarterSelectOptions = computed<SelectOption<number>[]>(() => [
    ...(this.hasYearPeriod()
      ? [{ value: 0, label: this.localization.myEvaluation.wholeYear }]
      : []),
    ...this.quarterOptions().map((quarter) => ({
      value: quarter,
      label: this.quarterOptionText(quarter),
    })),
  ]);

  readonly monthSelectOptions = computed<SelectOption<number>[]>(() => [
    ...(this.hasQuarterPeriod()
      ? [{ value: 0, label: this.localization.myEvaluation.wholeQuarter }]
      : []),
    ...this.monthOptions().map((month) => ({
      value: month,
      label: this.monthOptionText(month),
    })),
  ]);

  @HostListener('keydown.escape')
  onEscape(): void {
    if (this.selectedDrilldownCriteria()) {
      this.closeDrilldown();
    }
  }

  ngOnInit(): void {
    this.loadPeriodsAndEvaluation();
  }

  loadPeriodsAndEvaluation(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (periodList) => {
          const selectable = periodList.filter((p) =>
            SELECTABLE_PERIOD_TYPES.includes(p.periodType),
          );
          this.periods.set(selectable);
          const initial = this.defaultPeriod(selectable);
          if (initial) {
            this.applyPeriod(initial);
            this.loadEvaluation(initial.id);
          } else {
            this.loadEvaluation(undefined);
          }
        },
        error: () => {
          this.loadEvaluation(undefined);
        },
      });
  }

  loadEvaluation(periodId?: string): void {
    this.evalSubscription?.unsubscribe();
    this.loading.set(true);
    this.loadFailed.set(false);

    this.evalSubscription = this.evaluationService
      .getPersonalEvaluation(periodId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (evalData) => {
          this.evaluation.set(evalData);
          const currentPeriod = this.periods().find((p) => p.id === evalData.periodId);
          if (currentPeriod && this.selectedPeriod()?.id !== evalData.periodId) {
            this.applyPeriod(currentPeriod);
          }
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  onYearChange(year: number): void {
    this.year.set(year);
    this.quarter.set(this.defaultQuarter());
    this.month.set(this.defaultMonth());
    this.onPeriodSelected();
  }

  onQuarterChange(quarter: number): void {
    this.quarter.set(quarter);
    this.month.set(this.defaultMonth());
    this.onPeriodSelected();
  }

  onMonthChange(month: number): void {
    this.month.set(month);
    this.onPeriodSelected();
  }

  onPeriodChange(periodId: string): void {
    const period = this.periods().find((p) => p.id === periodId);
    if (period) {
      this.applyPeriod(period);
      this.onPeriodSelected();
    } else {
      this.loadEvaluation(periodId);
    }
  }

  private onPeriodSelected(): void {
    const period = this.selectedPeriod();
    if (period) {
      this.closeDrilldown();
      this.loadEvaluation(period.id);
    }
  }

  quarterOptionText(quarter: number): string {
    return this.i18n.translate('myEvaluation.quarterOption', { quarter });
  }

  monthOptionText(month: number): string {
    return this.i18n.translate('myEvaluation.monthOption', { month });
  }

  private defaultPeriod(periods: EvaluationPeriodOption[]): EvaluationPeriodOption | undefined {
    const now = new Date();
    const today = [
      now.getFullYear(),
      String(now.getMonth() + 1).padStart(2, '0'),
      String(now.getDate()).padStart(2, '0'),
    ].join('-');
    const current = periods.filter(
      (period) => period.startDate <= today && today <= period.endDate,
    );
    return (
      current.find((period) => period.periodType === 'MONTH' && period.isActive) ??
      current.find((period) => period.periodType === 'MONTH') ??
      periods.find((period) => period.periodType === 'MONTH' && period.isActive) ??
      periods.find((period) => period.periodType === 'MONTH') ??
      current.find((period) => period.periodType === 'QUARTER' && period.isActive) ??
      current[0] ??
      periods.find((period) => period.isActive) ??
      periods[0]
    );
  }

  private applyPeriod(period: EvaluationPeriodOption): void {
    this.year.set(periodYear(period));
    this.quarter.set(period.periodType === 'YEAR' ? 0 : periodQuarter(period));
    this.month.set(period.periodType === 'MONTH' ? periodMonth(period) : 0);
  }

  private defaultQuarter(): number {
    return this.hasYearPeriod() ? 0 : (this.quarterOptions().slice(-1)[0] ?? 0);
  }

  private defaultMonth(): number {
    return this.hasQuarterPeriod() ? 0 : (this.monthOptions().slice(-1)[0] ?? 0);
  }

  openDrilldown(crit: PersonalEvaluationCriteriaItem): void {
    if (!crit.hasDrilldown) {
      return;
    }
    this.ledgerSubscription?.unsubscribe();
    this.selectedDrilldownCriteria.set(crit);
    this.ledgerLoading.set(true);
    this.ledgerError.set(false);
    this.ledgerItems.set([]);
    this.resetLedgerFilters();

    const periodId = this.selectedPeriodId();
    const criteriaId = crit.kpiDefinitionId ?? crit.id;

    this.ledgerSubscription = this.evaluationService
      .getPersonalCriteriaLedger(periodId, criteriaId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.ledgerItems.set(items);
          this.ledgerLoading.set(false);
        },
        error: () => {
          this.ledgerItems.set([]);
          this.ledgerError.set(true);
          this.ledgerLoading.set(false);
        },
      });
  }

  closeDrilldown(): void {
    this.ledgerSubscription?.unsubscribe();
    this.selectedDrilldownCriteria.set(null);
    this.ledgerItems.set([]);
    this.ledgerError.set(false);
    this.ledgerLoading.set(false);
    this.resetLedgerFilters();
  }

  openLedgerWork(tx: PersonalCriteriaLedgerItem): void {
    if (!tx.workId) {
      return;
    }
    this.closeDrilldown();
    void this.router.navigate(['/my-work'], {
      queryParams: { keyword: tx.workCode },
      state: { workId: tx.workId },
    });
  }

  ledgerStatusLabel(status: string): string {
    const key = LEDGER_STATUS_KEY_MAP[status];
    const myEval = this.localization.myEvaluation as Record<string, string>;
    return (key && myEval[key]) || status;
  }

  ledgerStatusClass(status: string): string {
    return LEDGER_STATUS_CLASS_MAP[status] ?? 'ledger-status-ok';
  }

  private resetLedgerFilters(): void {
    this.ledgerKeyword.set('');
    this.ledgerStatus.set('');
    this.ledgerDateFrom.set('');
    this.ledgerDateTo.set('');
  }

  absValue(value: number): number {
    return Math.abs(value);
  }

  scoreTrendKey(scoreDelta: number): string {
    return scoreDelta >= 0
      ? 'myEvaluation.cardTotalScoreTrend'
      : 'myEvaluation.cardTotalScoreTrendDown';
  }

  scoreDeltaPillText(evalData: PersonalEvaluationView): string | null {
    if (!evalData.previousPeriodName || evalData.scoreDelta === null) return null;
    return this.i18n.translate(this.scoreTrendKey(evalData.scoreDelta), {
      diff: this.i18n.formatNumber(Math.abs(evalData.scoreDelta), 1),
      prevPeriod: this.formatPeriodName(evalData.previousPeriodName),
      prevScore: this.i18n.formatNumber(evalData.previousPeriodScore ?? 0, 1),
    });
  }

  statusLabel(status: string): string {
    const key = STATUS_KEY_MAP[status.toUpperCase()];
    const myEval = this.localization.myEvaluation as Record<string, string>;
    return (key && myEval[key]) || status;
  }

  formatPeriodName(name: string): string {
    const quarter = /^Quý\s+(\d+)\/(\d{4})$/i.exec(name);
    if (quarter) {
      return this.i18n.translate('myEvaluation.periodQuarter', {
        quarter: quarter[1],
        year: quarter[2],
      });
    }
    const month = /^Tháng\s+0?(\d+)\/(\d{4})$/i.exec(name);
    if (month) {
      return this.i18n.translate('myEvaluation.periodMonth', { month: month[1], year: month[2] });
    }
    const year = /^Năm\s+(\d{4})$/i.exec(name);
    if (year) {
      return this.i18n.translate('myEvaluation.periodYear', { year: year[1] });
    }
    return name;
  }

  formatAppealDeadline(deadline: string): string {
    const match = /^(\d{2}:\d{2})\s+(?:ngày\s+|on\s+)?(\d{2}\/\d{2}\/\d{4})$/i.exec(
      deadline.trim(),
    );
    if (!match) {
      return deadline;
    }
    return this.i18n.translate('myEvaluation.appealDeadlineAt', { time: match[1], date: match[2] });
  }

  formatRoleTitle(roleTitle: string): string {
    const clean = roleTitle.replace(/\s+trong phòng$/i, '').trim();
    const key = ROLE_KEY_MAP[clean];
    return key ? this.i18n.translate(`myEvaluation.${key}`) : clean;
  }

  formatActualTarget(display: string): string {
    return UNIT_KEY_MAP.reduce(
      (acc, [pattern, key]) =>
        acc.replace(pattern, () => this.i18n.translate(`myEvaluation.${key}`)),
      display,
    );
  }

  formatTarget(crit: PersonalEvaluationCriteriaItem): string {
    if (crit.targetDisplay) {
      return this.formatActualTarget(crit.targetDisplay);
    }
    return this.formatActualTarget(this.splitActualTarget(crit.actualTargetDisplay).target);
  }

  formatActual(crit: PersonalEvaluationCriteriaItem): string {
    if (crit.actualDisplay) {
      return this.formatActualTarget(crit.actualDisplay);
    }
    return this.formatActualTarget(this.splitActualTarget(crit.actualTargetDisplay).actual);
  }

  tierToneClass(tier?: string | null | undefined): string {
    const evalData = this.evaluation();
    const isFinal = evalData?.status === 'FINAL' || evalData?.status === 'PUBLISHED';
    const effectiveTier = tier !== undefined ? tier : isFinal ? evalData?.tier : null;
    const key = effectiveTier?.trim().toUpperCase() ?? '';
    if (!key) return '';
    return key.startsWith('A') ? 'tier-a' : `tier-${key.toLowerCase()}`;
  }

  statusTone(status: string): StatusBadgeTone {
    return STATUS_TONE_MAP[status.toUpperCase()] ?? 'neutral';
  }

  private splitActualTarget(display?: string): { actual: string; target: string } {
    if (!display || display.trim() === '—') {
      return { actual: '—', target: '—' };
    }
    const match = /^\s*(.+?)\s*\/\s*(\S+)(?:\s+(.+))?\s*$/.exec(display);
    if (!match) {
      return { actual: display, target: '—' };
    }
    const unitSuffix = match[3] ? ` ${match[3].trim()}` : '';
    return {
      actual: `${match[1].trim()}${unitSuffix}`,
      target: `${match[2].trim()}${unitSuffix}`,
    };
  }

  readonly quantitativeMetaText = computed(() => {
    const evalData = this.evaluation();
    if (!evalData) return '';
    return this.i18n.translate('evaluationForm.quantitativeMeta', {
      profile:
        this.formatRoleTitle(evalData.rankingRoleTitle) ||
        this.localization.evaluationForm.quantitativeSectionTitle,
      period: evalData.periodCode || this.formatPeriodName(evalData.periodName),
    });
  });

  readonly scorePillText = computed(() => {
    const delta = this.evaluation()?.scoreDelta ?? null;
    if (delta === null) return null;
    const sign = delta > 0 ? '+' : delta < 0 ? '-' : '';
    return `${sign}${this.i18n.formatNumber(Math.abs(delta), 2)}`;
  });

  readonly scorePillClass = computed(() =>
    (this.evaluation()?.scoreDelta ?? 0) < 0 ? 'pill-warning' : 'pill-success',
  );

  readonly criteriaSummary = computed(() => {
    const evalData = this.evaluation();
    if (!evalData) return null;
    if (evalData.criteriaSummary) return evalData.criteriaSummary;
    const kpis = evalData.criteria.filter((c) => c.score !== null);
    if (kpis.length === 0) return null;
    const achieved = kpis.filter((c) => (c.score ?? 0) >= 100).length;
    const rate = Math.round((achieved * 100) / kpis.length);
    return {
      achievedCount: achieved,
      nearCount: 0,
      belowCount: kpis.length - achieved,
      totalCount: kpis.length,
      achievedRatePercent: rate,
    };
  });

  readonly criteriaPillText = computed(() => {
    const rate = this.criteriaSummary()?.achievedRatePercent ?? null;
    if (rate === null) return this.localization.evaluations.emptyValue;
    return this.i18n.translate('evaluationForm.criteriaPill', {
      percent: this.i18n.formatNumber(rate, 0),
    });
  });

  readonly criteriaValueText = computed(() => {
    const summary = this.criteriaSummary();
    if (!summary || summary.totalCount === 0) return this.localization.evaluations.emptyValue;
    return `${summary.achievedCount} / ${summary.totalCount}`;
  });

  readonly criteriaToneClass = computed(() => {
    const summary = this.criteriaSummary();
    if (!summary || summary.totalCount === 0) return '';
    const rate = (summary.achievedCount * 100) / summary.totalCount;
    if (rate >= 80) return 'tone-good';
    if (rate >= 50) return 'tone-warn';
    return 'tone-bad';
  });

  readonly slaValueText = computed(() => {
    const rate = this.evaluation()?.slaSummary?.onTimeRatePercent ?? null;
    if (rate === null) return this.localization.evaluations.emptyValue;
    return `${this.i18n.formatNumber(rate, 1)}%`;
  });

  readonly slaToneClass = computed(() => {
    const rate = this.evaluation()?.slaSummary?.onTimeRatePercent ?? null;
    if (rate === null) return '';
    if (rate >= 90) return 'tone-good';
    if (rate >= 70) return 'tone-warn';
    return 'tone-bad';
  });

  readonly riskCount = computed(() => {
    const evalData = this.evaluation();
    if (!evalData) return 0;
    return evalData.riskCount ?? evalData.riskEvents?.length ?? (evalData.riskPenalty > 0 ? 1 : 0);
  });

  readonly isFinalEvaluation = computed(() => {
    const status = this.evaluation()?.status?.toUpperCase();
    return status === 'FINAL' || status === 'PUBLISHED';
  });

  readonly isCalculatedOrAbove = computed(() => {
    const status = this.evaluation()?.status?.toUpperCase();
    return (
      status === 'CALCULATED' ||
      status === 'SUBMITTED' ||
      status === 'DISPUTED' ||
      status === 'FINAL' ||
      status === 'PUBLISHED'
    );
  });

  readonly statusNoticeText = computed(() => {
    const evalData = this.evaluation();
    if (!evalData) return '';
    const status = evalData.status?.toUpperCase();

    if (status === 'DRAFT') {
      return this.localization.myEvaluation.noticeDraft;
    }
    if (status === 'CALCULATED') {
      if (evalData.canAppeal && evalData.appealRemainingDays > 0) {
        return this.i18n.translate('myEvaluation.noticeCalculatedWithAppeal', {
          deadline: this.formatAppealDeadline(evalData.appealDeadlineText),
          remainingDays: evalData.appealRemainingDays,
        });
      }
      return this.localization.myEvaluation.noticeCalculated;
    }
    if (status === 'DISPUTED') {
      return this.localization.myEvaluation.noticeDisputed;
    }
    if (status === 'SUBMITTED') {
      return this.localization.myEvaluation.noticeSubmitted;
    }
    if (status === 'FINAL' || status === 'PUBLISHED') {
      return this.localization.myEvaluation.noticeFinal;
    }
    return '';
  });

  readonly statusNoticeToneClass = computed(() => {
    const status = this.evaluation()?.status?.toUpperCase();
    switch (status) {
      case 'DRAFT':
        return 'text-draft';
      case 'CALCULATED':
        return 'text-calculated';
      case 'DISPUTED':
        return 'text-disputed';
      case 'SUBMITTED':
        return 'text-submitted';
      case 'FINAL':
      case 'PUBLISHED':
        return 'text-final';
      default:
        return 'text-draft';
    }
  });

  readonly departmentRankingValue = computed(() => {
    if (!this.isFinalEvaluation()) {
      return this.localization.evaluations.emptyValue;
    }
    return this.evaluation()?.rankingPercentile || this.localization.evaluations.emptyValue;
  });

  readonly departmentRankingDetail = computed(() => {
    if (!this.isFinalEvaluation()) {
      return null;
    }
    const evalData = this.evaluation();
    if (!evalData?.rankingPosition || !evalData?.rankingTotalEmployees) {
      return null;
    }
    return this.i18n.translate('myEvaluation.cardRankingDetail', {
      rank: evalData.rankingPosition,
      total: evalData.rankingTotalEmployees,
      roleName: this.formatRoleTitle(evalData.rankingRoleTitle),
    });
  });

  readonly tierText = computed(() => {
    const evalData = this.evaluation();
    const tier = this.isFinalEvaluation() ? evalData?.tier : null;
    return tier
      ? this.i18n.translate('evaluationForm.proposedTier', { tier })
      : this.localization.evaluations.emptyValue;
  });

  readonly tierPillText = computed(() => {
    const evalData = this.evaluation();
    const tier = this.isFinalEvaluation() ? evalData?.tier : null;
    return tier ? this.i18n.translate('evaluationForm.proposedTier', { tier }) : null;
  });

  scoreToneClass(score: number | null): string {
    if (score === null) return '';
    if (score >= 100) return 'tone-good';
    if (score >= 80) return 'tone-warn';
    return 'tone-bad';
  }

  riskPenaltyPillText(evalData: PersonalEvaluationView): string {
    if (!evalData.riskPenalty) return '';
    return `-${this.i18n.formatNumber(evalData.riskPenalty, 1)}`;
  }

  formatRatio(val: number | null | undefined): string {
    if (val == null) {
      return '—';
    }
    const formatted = val.toString().replace('.', ',');
    return `× ${formatted}`;
  }
}
