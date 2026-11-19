import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, forkJoin, Observable, of, tap, throwError } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { RightDrawerComponent } from '../../shared/target/right-drawer/right-drawer.component';
import { PaceBarComponent } from '../../shared/target/pace-bar/pace-bar.component';
import { businessClock } from '../work-board/work-board.utils';
import { MyTargetsApiService } from './my-targets-api.service';
import {
  AckStatus,
  MyTargetFeedback,
  MyTargetKpi,
  MyTargetProgress,
  MyTargetProgressKpi,
  MyTargetYear,
  ProgressPeriod,
} from './my-targets.models';
import {
  MAX_FEEDBACK_REASON_LENGTH,
  ProgressBucket,
  QUARTER_KEYS,
  QuarterKey,
  YearViewRow,
  bannerFor,
  buildYearViewRows,
  changedYearCell,
  deltaVsPreviousYearPercent,
  filterProgress,
  groupChips,
  groupKpis,
  isIncreasedOver15,
  isNewKpi,
  missingToTarget,
  progressCardCounts,
  sortByPaceGap,
  summaryCards,
  yearElapsedPercent,
} from './my-targets.utils';

type ViewMode = 'list' | 'quarter' | 'year';
type ListFilter = 'ALL' | 'INCREASED' | string;
type YearValueMode = 'ACHIEVED_TARGET' | 'TARGET_ONLY';

type DrawerState =
  | { readonly kind: 'FEEDBACK'; readonly kpi: MyTargetKpi }
  | {
      readonly kind: 'KPI_DETAIL';
      readonly kpi: MyTargetProgressKpi;
      readonly tab: 'QUARTER' | 'CONTRIBUTIONS' | 'HISTORY';
    };

const CONFLICT = 409;

/** E1-E7: the employee's own targets for a year — confirm/reconfirm, feedback, quarter/year progress. */
@Component({
  selector: 'app-my-targets',
  standalone: true,
  imports: [
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    RightDrawerComponent,
    PaceBarComponent,
  ],
  templateUrl: './my-targets.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './my-targets.component.scss',
  ],
})
export class MyTargetsComponent extends LocalizedComponent {
  private readonly api = inject(MyTargetsApiService);
  private readonly employees = inject(EmployeeService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly maxReason = MAX_FEEDBACK_REASON_LENGTH;
  protected readonly today = businessClock(new Date()).date;

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly traceId = signal<string | null>(null);
  protected readonly data = signal<MyTargetYear | null>(null);
  protected readonly me = signal<EmployeeDetail | null>(null);
  protected readonly kpiGroupById = signal<ReadonlyMap<string, string>>(new Map());

  protected readonly year = signal(Number(this.today.slice(0, 4)));
  protected readonly view = signal<ViewMode>('list');
  protected readonly listFilter = signal<ListFilter>('ALL');

  protected readonly progressBucket = signal<ProgressBucket>('NEEDS_ATTENTION');
  protected readonly progressLoading = signal(false);
  protected readonly progress = signal<MyTargetProgress | null>(null);

  protected readonly yearRowsLoading = signal(false);
  protected readonly yearRows = signal<readonly YearViewRow[] | null>(null);
  protected readonly yearCumulativePercent = signal(0);
  protected readonly yearValueMode = signal<YearValueMode>('ACHIEVED_TARGET');

  protected readonly drawer = signal<DrawerState | null>(null);
  protected readonly confirmDialogOpen = signal(false);
  protected readonly sending = signal(false);
  protected readonly toast = signal<{ kind: 'success' | 'error'; text: string } | null>(null);

  /** Feedback sent this session, keyed by kpiId — see the gap note in my-targets-api.service.ts: there is no
   * `GET` endpoint to list a KPI's feedback/reply history yet, so this cache is the only history the UI can show. */
  protected readonly sentFeedback = signal<Record<string, MyTargetFeedback>>({});

  protected feedbackReason = '';
  protected feedbackProposedYear: number | null = null;
  protected feedbackProposedQ1: number | null = null;
  protected feedbackProposedQ2: number | null = null;
  protected feedbackProposedQ3: number | null = null;
  protected feedbackProposedQ4: number | null = null;
  protected feedbackError = false;

  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly summary = computed(() => {
    const data = this.data();
    return data ? summaryCards(data) : null;
  });

  protected readonly banner = computed(() => {
    const data = this.data();
    return data ? bannerFor(data) : null;
  });

  protected readonly groups = computed(() => {
    const data = this.data();
    return data ? groupKpis(data.kpis, this.kpiGroupById()) : [];
  });

  protected readonly chips = computed(() => groupChips(this.groups()));

  protected readonly filteredKpis = computed(() => {
    const data = this.data();
    if (!data) return [];
    const filter = this.listFilter();
    if (filter === 'ALL') return data.kpis;
    if (filter === 'INCREASED') return data.kpis.filter(isIncreasedOver15);
    const group = this.groups().find((g) => g.id === filter);
    return group ? group.kpis : data.kpis;
  });

  /** "Locked" (D-E7): a past, already-confirmed year is read-only. */
  protected readonly locked = computed(() => {
    const data = this.data();
    return (
      data !== null && data.ackStatus === 'CONFIRMED' && data.year < Number(this.today.slice(0, 4))
    );
  });

  protected readonly progressCounts = computed(() => {
    const progress = this.progress();
    return progress ? progressCardCounts(progress.kpis) : null;
  });

  protected readonly progressRows = computed(() => {
    const progress = this.progress();
    if (!progress) return [];
    return sortByPaceGap(filterProgress(progress.kpis, this.progressBucket()));
  });

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const yearParam = Number(params.get('year'));
      const nextYear = Number.isInteger(yearParam) && yearParam > 0 ? yearParam : this.year();
      const nextView = (params.get('view') as ViewMode | null) ?? 'list';
      const changed = nextYear !== this.year() || nextView !== this.view();
      this.year.set(nextYear);
      this.view.set(nextView);
      if (changed || this.data() === null) {
        this.load();
      } else if (nextView === 'quarter' && !this.progress()) {
        this.loadProgress();
      } else if (nextView === 'year' && !this.yearRows()) {
        this.loadYearView();
      }
    });
  }

  private setQuery(partial: Partial<{ year: number; view: ViewMode }>): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: partial,
      queryParamsHandling: 'merge',
    });
  }

  protected goToView(view: ViewMode): void {
    this.setQuery({ view });
  }

  // ---- loading ---------------------------------------------------------------------------------

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.traceId.set(null);
    forkJoin({
      year: this.api.year(this.year()),
      me: this.employees.me(),
      kpis: this.kpiApi.list({ pageSize: 200 }),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ year, me, kpis }) => {
          this.data.set(year);
          this.me.set(me);
          this.kpiGroupById.set(new Map(kpis.items.map((k) => [k.id, k.kpiGroupName])));
          this.loading.set(false);
          if (this.view() === 'quarter') this.loadProgress();
          if (this.view() === 'year') this.loadYearView();
        },
        error: (error: HttpErrorResponse) => {
          this.failed.set(true);
          this.loading.set(false);
          this.traceId.set((error.error?.traceId as string | undefined) ?? null);
        },
      });
  }

  private currentPeriod(): ProgressPeriod {
    const month = Number(this.today.slice(5, 7));
    const quarter = Math.ceil(month / 3);
    return `Q${quarter}` as ProgressPeriod;
  }

  protected loadProgress(): void {
    this.progressLoading.set(true);
    this.api
      .progress(this.year(), this.currentPeriod())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (progress) => {
          this.progress.set(progress);
          this.progressLoading.set(false);
        },
        error: () => this.progressLoading.set(false),
      });
  }

  protected loadYearView(): void {
    this.yearRowsLoading.set(true);
    const quarters = QUARTER_KEYS;
    forkJoin({
      yearTotal: this.api.progress(this.year(), 'YEAR'),
      ...Object.fromEntries(quarters.map((q) => [q, this.api.progress(this.year(), q)])),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          const byQuarter = Object.fromEntries(
            quarters.map((q) => [q, (result as Record<string, MyTargetProgress>)[q].kpis]),
          ) as Record<QuarterKey, readonly MyTargetProgressKpi[]>;
          this.yearRows.set(
            buildYearViewRows(result.yearTotal.kpis, byQuarter, this.year(), this.today),
          );
          this.yearCumulativePercent.set(
            result.yearTotal.kpis.length > 0
              ? result.yearTotal.kpis.reduce((s, k) => s + k.progress.achievedPercent, 0) /
                  result.yearTotal.kpis.length
              : 0,
          );
          this.yearRowsLoading.set(false);
        },
        error: () => this.yearRowsLoading.set(false),
      });
  }

  // ---- E1/E3 confirm -----------------------------------------------------------------------------

  protected openConfirmDialog(): void {
    if (this.sending()) return;
    this.confirmDialogOpen.set(true);
  }

  protected closeConfirmDialog(): void {
    this.confirmDialogOpen.set(false);
  }

  protected confirm(): void {
    if (this.sending()) return;
    this.sending.set(true);
    this.api
      .confirm(this.year())
      .pipe(
        catchError((error: HttpErrorResponse) =>
          error.status === CONFLICT ? of(null) : this.failWith(error),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.sending.set(false);
        this.confirmDialogOpen.set(false);
        if (result) {
          this.showToast('success', this.localization.targetEmp.confirmSuccess);
          this.load();
        }
      });
  }

  private failWith(error: HttpErrorResponse): Observable<never> {
    this.showToast('error', this.localization.targetEmp.actionFailed);
    this.sending.set(false);
    return throwError(() => error);
  }

  protected ackLabel(status: AckStatus): string {
    const text = this.localization.targetEmp;
    switch (status) {
      case 'PENDING':
        return text.ackPending;
      case 'RECONFIRM':
        return text.ackReconfirm;
      default:
        return text.ackConfirmed;
    }
  }

  // ---- E2 feedback drawer -------------------------------------------------------------------------

  protected openFeedback(kpi: MyTargetKpi): void {
    this.feedbackReason = '';
    this.feedbackProposedYear = null;
    this.feedbackProposedQ1 = null;
    this.feedbackProposedQ2 = null;
    this.feedbackProposedQ3 = null;
    this.feedbackProposedQ4 = null;
    this.feedbackError = false;
    this.drawer.set({ kind: 'FEEDBACK', kpi });
  }

  protected closeDrawer(): void {
    this.drawer.set(null);
  }

  protected sendFeedback(): void {
    const state = this.drawer();
    if (!state || state.kind !== 'FEEDBACK' || this.sending()) return;
    const reason = this.feedbackReason.trim();
    this.feedbackError = reason.length === 0 || reason.length > MAX_FEEDBACK_REASON_LENGTH;
    if (this.feedbackError) return;
    this.sending.set(true);
    this.api
      .feedback(this.year(), {
        kpiId: state.kpi.kpiId,
        reason,
        proposedYear: this.feedbackProposedYear,
        proposedQ1: this.feedbackProposedQ1,
        proposedQ2: this.feedbackProposedQ2,
        proposedQ3: this.feedbackProposedQ3,
        proposedQ4: this.feedbackProposedQ4,
      })
      .pipe(
        catchError((error: HttpErrorResponse) =>
          error.status === 400
            ? of(null).pipe(
                tap(() => this.showToast('error', this.localization.targetEmp.reasonTooLong)),
              )
            : this.failWith(error),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((feedback) => {
        this.sending.set(false);
        if (feedback) {
          this.sentFeedback.update((all) => ({ ...all, [state.kpi.kpiId]: feedback }));
          this.showToast('success', this.localization.targetEmp.feedbackSent);
          this.drawer.set(null);
          this.load();
        }
      });
  }

  protected hasFeedback(kpi: MyTargetKpi): boolean {
    return kpi.hasFeedback || this.sentFeedback()[kpi.kpiId] !== undefined;
  }

  // ---- E4/E5 quarter progress ----------------------------------------------------------------------

  protected setProgressBucket(bucket: ProgressBucket): void {
    this.progressBucket.set(bucket);
  }

  protected openKpiDetail(kpi: MyTargetProgressKpi): void {
    this.drawer.set({ kind: 'KPI_DETAIL', kpi, tab: 'QUARTER' });
  }

  protected setDetailTab(tab: 'QUARTER' | 'CONTRIBUTIONS' | 'HISTORY'): void {
    const state = this.drawer();
    if (state?.kind === 'KPI_DETAIL') this.drawer.set({ ...state, tab });
  }

  protected missing(kpi: MyTargetProgressKpi): number {
    return missingToTarget(kpi.progress);
  }

  // ---- E6 year view ---------------------------------------------------------------------------------

  protected toggleYearValueMode(): void {
    this.yearValueMode.update((mode) =>
      mode === 'ACHIEVED_TARGET' ? 'TARGET_ONLY' : 'ACHIEVED_TARGET',
    );
  }

  protected elapsedPercent(): number {
    return yearElapsedPercent(this.today, this.year());
  }

  // ---- shared helpers -----------------------------------------------------------------------------

  protected deltaPercent(kpi: MyTargetKpi): number | null {
    return deltaVsPreviousYearPercent(kpi);
  }

  protected isNew(kpi: MyTargetKpi): boolean {
    return isNewKpi(kpi);
  }

  protected isIncreased(kpi: MyTargetKpi): boolean {
    return isIncreasedOver15(kpi);
  }

  protected changedCell(kpi: MyTargetKpi) {
    return changedYearCell(kpi);
  }

  protected number(value: number | null): string {
    if (value === null) return '—';
    return this.i18n.formatNumber(value, 0);
  }

  protected percent(value: number | null): string {
    if (value === null) return '—';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${this.i18n.formatNumber(value, 1)}%`;
  }

  protected dateOnly(value: string | null): string {
    return this.i18n.formatDate(value, 'DD/MM/YYYY');
  }

  protected dateTime(value: string | null): string {
    return this.i18n.formatDate(value, 'HH:mm DD/MM/YYYY');
  }

  protected get reasonLength(): number {
    return this.feedbackReason.trim().length;
  }

  private showToast(kind: 'success' | 'error', text: string): void {
    clearTimeout(this.toastTimer);
    this.toast.set({ kind, text });
    this.toastTimer = setTimeout(() => this.toast.set(null), 4000);
  }

  protected dismissToast(): void {
    clearTimeout(this.toastTimer);
    this.toast.set(null);
  }
}
