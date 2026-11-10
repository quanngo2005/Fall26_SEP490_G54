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
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppNumberPipe } from '../../shared/pipes';
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
    LayoutIconComponent,
    AppNumberPipe,
  ],
  templateUrl: './my-evaluation.component.html',
  styleUrls: [
    './my-evaluation.component.scss',
    './my-evaluation.cards.scss',
    './my-evaluation.table.scss',
    './my-evaluation.dialogs.scss',
    './my-evaluation.filter.scss',
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

  readonly showAcknowledgeModal = signal<boolean>(false);
  readonly acknowledgeSuccess = signal<boolean>(false);
  readonly acknowledging = signal<boolean>(false);
  readonly acknowledgeError = signal<boolean>(false);

  readonly selectedDrilldownCriteria = signal<PersonalEvaluationCriteriaItem | null>(null);
  readonly ledgerItems = signal<readonly PersonalCriteriaLedgerItem[]>([]);
  readonly ledgerLoading = signal<boolean>(false);
  readonly ledgerError = signal<boolean>(false);

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

  @HostListener('keydown.escape')
  onEscape(): void {
    if (this.showAcknowledgeModal()) {
      this.closeAcknowledgeModal();
    } else if (this.selectedDrilldownCriteria()) {
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

  onYearChange(event: Event): void {
    this.year.set(Number((event.target as HTMLSelectElement).value));
    this.quarter.set(this.defaultQuarter());
    this.month.set(this.defaultMonth());
    this.onPeriodSelected();
  }

  onQuarterChange(event: Event): void {
    this.quarter.set(Number((event.target as HTMLSelectElement).value));
    this.month.set(this.defaultMonth());
    this.onPeriodSelected();
  }

  onMonthChange(event: Event): void {
    this.month.set(Number((event.target as HTMLSelectElement).value));
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
      this.acknowledgeSuccess.set(false);
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
      (period) => period.isActive && period.startDate <= today && today <= period.endDate,
    );
    return (
      current.find((period) => period.periodType === 'QUARTER') ??
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

  openAcknowledgeModal(): void {
    if (this.evaluation()?.isAcknowledged) {
      return;
    }
    this.acknowledgeError.set(false);
    this.showAcknowledgeModal.set(true);
  }

  closeAcknowledgeModal(): void {
    this.acknowledgeError.set(false);
    this.showAcknowledgeModal.set(false);
  }

  confirmAcknowledge(): void {
    const evalData = this.evaluation();
    if (!evalData || evalData.isAcknowledged) {
      return;
    }

    this.acknowledging.set(true);
    this.acknowledgeError.set(false);
    this.evaluationService
      .acknowledgePersonalEvaluation(evalData.periodId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.acknowledging.set(false);
          this.showAcknowledgeModal.set(false);
          this.acknowledgeSuccess.set(true);
          this.evaluation.set({
            ...evalData,
            isAcknowledged: true,
            acknowledgedAt: result.acknowledgedAt,
          });
        },
        error: () => {
          this.acknowledging.set(false);
          this.acknowledgeError.set(true);
        },
      });
  }

  openAppeal(): void {
    void this.router.navigate(['/appeals/mine']);
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
  }

  absValue(value: number): number {
    return Math.abs(value);
  }

  scoreTrendKey(scoreDelta: number): string {
    return scoreDelta >= 0
      ? 'myEvaluation.cardTotalScoreTrend'
      : 'myEvaluation.cardTotalScoreTrendDown';
  }

  formatRatio(val: number | null | undefined): string {
    if (val == null) {
      return '—';
    }
    const formatted = val.toString().replace('.', ',');
    return `× ${formatted}`;
  }
}
