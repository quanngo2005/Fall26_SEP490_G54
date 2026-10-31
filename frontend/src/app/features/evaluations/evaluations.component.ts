import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EvaluationService } from '../../core/evaluation.service';
import { DepartmentEmployeeEvaluation, EvaluationPeriodOption } from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';

export type StatCardFilter = 'ALL' | 'NOT_EVALUATED' | 'DRAFT' | 'COMPLETED' | 'ATTENTION';
export type ListStatus = 'NOT_EVALUATED' | 'DRAFT' | 'COMPLETED';

@Component({
  selector: 'app-evaluations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
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
  readonly selectedPeriodId = signal<string>('');
  readonly allEvaluations = signal<DepartmentEmployeeEvaluation[]>([]);
  readonly loading = signal(false);
  readonly failure = signal<'offline' | 'forbidden' | 'error' | null>(null);

  readonly activeStatFilter = signal<StatCardFilter>('ALL');
  readonly searchKeyword = signal<string>('');
  readonly positionFilter = signal<string>('ALL');
  readonly attentionFilter = signal<string>('ALL');

  readonly selectedPeriod = computed(
    () => this.periods().find((period) => period.id === this.selectedPeriodId()) ?? null,
  );

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

  readonly positions = computed(() => {
    const byId = new Map<string, string>();
    for (const item of this.allEvaluations()) byId.set(item.jobPositionId, item.jobPositionTitle);
    return Array.from(byId, ([id, title]) => ({ id, title }));
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
    const pos = this.positionFilter();
    const att = this.attentionFilter();

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

    // 3. Position filter
    if (pos !== 'ALL') {
      list = list.filter((i) => i.jobPositionId === pos);
    }

    // 4. Attention filter
    if (att === 'ATTENTION') {
      list = list.filter((i) => this.needsAttention(i));
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

  private loadPeriods(): void {
    this.loading.set(true);
    this.failure.set(null);
    this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (periods) => {
          this.periods.set(periods);
          const initial = periods.find((period) => period.isActive) ?? periods[0];
          if (!initial) {
            this.allEvaluations.set([]);
            this.loading.set(false);
            return;
          }
          this.selectedPeriodId.set(initial.id);
          this.loadRows();
        },
        error: (error: unknown) => this.fail(error),
      });
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

  onPeriodChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.selectedPeriodId.set(target.value);
    this.loadRows();
  }

  onSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.searchKeyword.set(target.value);
  }

  onPositionFilterChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.positionFilter.set(target.value);
  }

  onAttentionFilterChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.attentionFilter.set(target.value);
  }

  openEvaluationForm(item: DepartmentEmployeeEvaluation): void {
    void this.router.navigate(['/evaluations/form'], {
      queryParams: {
        employeeId: item.employeeId,
        periodId: this.selectedPeriodId(),
      },
    });
  }
}
