import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EvaluationService } from '../../core/evaluation.service';
import { DepartmentEmployeeEvaluation, EvaluationPeriodOption } from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';
import {
  findQuarterPeriod,
  findYearPeriod,
  periodQuarter,
  periodYear,
} from '../target-overview/target-overview.utils';

/** The period levels the year / quarter / month filter drills through. */
const SELECTABLE_PERIOD_TYPES = ['YEAR', 'QUARTER', 'MONTH'];

function periodMonth(period: EvaluationPeriodOption): number {
  return Number(period.startDate.slice(5, 7));
}

export type StatCardFilter = 'ALL' | 'NOT_EVALUATED' | 'DRAFT' | 'COMPLETED' | 'ATTENTION';
export type ListStatus = 'NOT_EVALUATED' | 'DRAFT' | 'COMPLETED';

@Component({
  selector: 'app-evaluations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    AppDatePipe,
    AppNumberPipe,
  ],
  templateUrl: './evaluations.component.html',
  styleUrls: ['./evaluations.component.scss', './evaluations.layout.scss'],
})
export class EvaluationsComponent extends LocalizedComponent {
  private readonly router = inject(Router);
  private readonly evaluationService = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);
  private rowsRequest?: Subscription;

  readonly periods = signal<EvaluationPeriodOption[]>([]);
  readonly year = signal<number | null>(null);
  /** 0 = the whole year. */
  readonly quarter = signal(0);
  /** 0 = the whole quarter. */
  readonly month = signal(0);
  readonly allEvaluations = signal<DepartmentEmployeeEvaluation[]>([]);
  readonly loading = signal(false);
  readonly failure = signal<'offline' | 'forbidden' | 'error' | null>(null);

  readonly activeStatFilter = signal<StatCardFilter>('ALL');
  readonly searchKeyword = signal<string>('');

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

  readonly periodInfo = computed(() => {
    const period = this.selectedPeriod();
    const list = this.allEvaluations();
    const completed = list.filter((item) => this.listStatus(item) === 'COMPLETED').length;
    const departmentNames = new Set(list.map((item) => item.departmentName));
    return {
      period,
      departmentName:
        departmentNames.size > 1
          ? this.i18n.translate('evaluations.departmentCount', { count: departmentNames.size })
          : (list[0]?.departmentName ?? null),
      employeeCount: list.length,
      completedCount: completed,
      progressPercent: list.length ? Math.round((completed * 100) / list.length) : null,
    };
  });

  readonly statSummary = computed(() => {
    const list = this.allEvaluations();
    const count = (status: ListStatus) => list.filter((i) => this.listStatus(i) === status).length;
    return {
      allCount: list.length,
      notEvaluatedCount: count('NOT_EVALUATED'),
      draftCount: count('DRAFT'),
      completedCount: count('COMPLETED'),
      attentionCount: list.filter((i) => this.needsAttention(i)).length,
    };
  });

  readonly periodStatusText = computed(() => {
    const text = this.localization.evaluations;
    switch (this.selectedPeriod()?.status) {
      case 'OPEN':
        return text.statusEvaluating;
      case 'IN_REVIEW':
        return text.periodInReview;
      case 'LOCKED':
        return text.periodLocked;
      case 'CLOSED':
        return text.periodClosed;
      default:
        return this.localization.evaluations.emptyValue;
    }
  });

  readonly progressText = computed(() => {
    const percent = this.periodInfo().progressPercent;
    return percent === null ? this.localization.evaluations.emptyValue : `${percent}%`;
  });

  readonly filteredEvaluations = computed(() => {
    if (this.loading() || this.failure()) return [];

    let list = this.allEvaluations();
    const statFilter = this.activeStatFilter();
    const keyword = this.searchKeyword().trim().toLowerCase();

    // 1. Stat filter
    if (statFilter === 'ATTENTION') {
      list = list.filter((i) => this.needsAttention(i));
    } else if (statFilter !== 'ALL') {
      list = list.filter((i) => this.listStatus(i) === statFilter);
    }

    // 2. Keyword filter
    if (keyword) {
      list = list.filter(
        (i) =>
          i.employeeCode.toLowerCase().includes(keyword) ||
          i.fullName.toLowerCase().includes(keyword),
      );
    }

    return list;
  });

  readonly displayedEvaluations = computed(() => {
    return this.filteredEvaluations();
  });

  readonly listStateText = computed(() => {
    const text = this.localization.evaluations;
    if (this.loading()) return text.loading;
    switch (this.failure()) {
      case 'offline':
        return text.offline;
      case 'forbidden':
        return text.forbidden;
      case 'error':
        return text.loadError;
      default:
        return text.emptyList;
    }
  });

  readonly footerSummaryText = computed(() =>
    this.loading() || this.failure()
      ? this.localization.evaluations.emptyValue
      : this.i18n.translate('evaluations.footerSummary', {
          shown: this.displayedEvaluations().length,
          total: this.periodInfo().employeeCount,
        }),
  );

  readonly bannerText = computed(() =>
    this.i18n.translate('evaluations.bannerNoteText', { count: this.periodInfo().employeeCount }),
  );

  constructor() {
    super();
    this.loadPeriods();
  }

  /** NOT_STARTED is "not evaluated", DRAFT stays a draft, every later state counts as completed. */
  listStatus(item: DepartmentEmployeeEvaluation): ListStatus {
    if (item.evaluationStatus === 'NOT_STARTED') return 'NOT_EVALUATED';
    return item.evaluationStatus === 'DRAFT' ? 'DRAFT' : 'COMPLETED';
  }

  needsAttention(item: DepartmentEmployeeEvaluation): boolean {
    return item.pendingSubmissionCount > 0 || item.hasOpenDispute;
  }

  pendingReviewLinkText(item: DepartmentEmployeeEvaluation): string {
    return this.i18n.translate('evaluations.pendingReviewLinkAria', { name: item.fullName });
  }

  /** Signed difference to the previous period: an increase gets a leading "+", a drop keeps its "-". */
  scoreChangeText(change: number): string {
    const text = this.i18n.formatNumber(change, 2);
    return change > 0 ? `+${text}` : text;
  }

  private loadPeriods(): void {
    this.loading.set(true);
    this.failure.set(null);
    this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (periods) => {
          const selectable = periods.filter((period) =>
            SELECTABLE_PERIOD_TYPES.includes(period.periodType),
          );
          this.periods.set(selectable);
          const initial = this.defaultPeriod(selectable);
          if (!initial) {
            this.allEvaluations.set([]);
            this.loading.set(false);
            return;
          }
          this.applyPeriod(initial);
          this.loadRows();
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  /** Targets are set per quarter, so the open quarter that contains today opens first, then any open period. */
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

  private loadRows(): void {
    this.rowsRequest?.unsubscribe();
    this.loading.set(true);
    this.failure.set(null);
    this.rowsRequest = this.evaluationService
      .departmentEmployees(this.selectedPeriodId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rows) => {
          this.allEvaluations.set(rows);
          this.loading.set(false);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  private fail(error: unknown): void {
    const status = error instanceof HttpErrorResponse ? error.status : null;
    this.allEvaluations.set([]);
    this.failure.set(status === 0 ? 'offline' : status === 403 ? 'forbidden' : 'error');
    this.loading.set(false);
  }

  setStatFilter(filter: StatCardFilter): void {
    this.activeStatFilter.set(filter);
  }

  onYearChange(event: Event): void {
    this.year.set(Number((event.target as HTMLSelectElement).value));
    this.quarter.set(this.defaultQuarter());
    this.month.set(this.defaultMonth());
    this.reload();
  }

  onQuarterChange(event: Event): void {
    this.quarter.set(Number((event.target as HTMLSelectElement).value));
    this.month.set(this.defaultMonth());
    this.reload();
  }

  onMonthChange(event: Event): void {
    this.month.set(Number((event.target as HTMLSelectElement).value));
    this.reload();
  }

  quarterOptionText(quarter: number): string {
    return this.i18n.translate('evaluations.quarterOption', { quarter });
  }

  monthOptionText(month: number): string {
    return this.i18n.translate('evaluations.monthOption', { month });
  }

  /** Puts the three selects on the level and position of the given period. */
  private applyPeriod(period: EvaluationPeriodOption): void {
    this.year.set(periodYear(period));
    this.quarter.set(period.periodType === 'YEAR' ? 0 : periodQuarter(period));
    this.month.set(period.periodType === 'MONTH' ? periodMonth(period) : 0);
  }

  /** After a year change: the whole year when it exists, otherwise the latest quarter of the year. */
  private defaultQuarter(): number {
    return this.hasYearPeriod() ? 0 : (this.quarterOptions().slice(-1)[0] ?? 0);
  }

  /** After a quarter change: the whole quarter when it exists, otherwise its first month. */
  private defaultMonth(): number {
    return this.quarter() === 0 || this.hasQuarterPeriod() ? 0 : (this.monthOptions()[0] ?? 0);
  }

  private reload(): void {
    if (this.selectedPeriodId()) {
      this.loadRows();
      return;
    }
    this.rowsRequest?.unsubscribe();
    this.allEvaluations.set([]);
    this.failure.set(null);
    this.loading.set(false);
  }

  onSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.searchKeyword.set(target.value);
  }

  openEvaluationForm(item: DepartmentEmployeeEvaluation): void {
    const periodCode = this.selectedPeriod()?.code;
    if (!periodCode) return;
    void this.router.navigate(['/evaluations/form', periodCode, item.employeeCode]);
  }
}
