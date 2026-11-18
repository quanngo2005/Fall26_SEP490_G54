import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, map, of, switchMap, throwError } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignmentFeedback } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { displayNumber } from '../../shared/ui/display';
import { businessClock } from '../work-board/work-board.utils';
import {
  MonthRow,
  PeriodSlice,
  QuarterRow,
  YearView,
  buildYearView,
  changedAfterConfirm,
  pendingQuarter,
  quarterComments,
  slicesOfMonth,
} from './my-targets.utils';

const MAX_COMMENT_LENGTH = 500;
const KPI_PAGE_SIZE = 200;
const CONFLICT = 409;

type Granularity = 'WEEK' | 'DAY';

interface SlicePanel {
  periodId: string;
  month: MonthRow;
  granularity: Granularity;
  slices: PeriodSlice[];
  loading: boolean;
  failed: boolean;
}

/** SCR-STRAT-03-D1: the employee's own quarterly targets, split by month, with confirm and feedback. */
@Component({
  selector: 'app-my-targets',
  standalone: true,
  imports: [FormsModule, HeaderComponent, SidebarComponent],
  templateUrl: './my-targets.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './my-targets.component.scss',
  ],
})
export class MyTargetsComponent extends LocalizedComponent {
  private readonly targets = inject(TargetAssignmentService);
  private readonly employees = inject(EmployeeService);
  private readonly evaluations = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly maxComment = MAX_COMMENT_LENGTH;
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly view = signal<YearView | null>(null);
  protected readonly me = signal<EmployeeDetail | null>(null);
  protected readonly history = signal<Record<string, TargetAssignmentFeedback[]>>({});
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  protected readonly panel = signal<SlicePanel | null>(null);
  protected readonly sending = signal(false);
  protected readonly toast = signal<{ kind: 'success' | 'error'; text: string } | null>(null);
  protected readonly commentError = signal(false);

  protected comment = '';

  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  /** The quarter waiting for the employee's confirmation, if any. */
  protected readonly pending = computed(() => {
    const view = this.view();
    return view ? pendingQuarter(view) : null;
  });

  /** The quarter the header talks about: the pending one, else the latest. */
  protected readonly focus = computed(() => {
    const view = this.view();
    return this.pending() ?? view?.quarters[view.quarters.length - 1] ?? null;
  });

  protected readonly changed = computed(() => {
    const pending = this.pending();
    return pending !== null && changedAfterConfirm(pending, this.history());
  });

  protected readonly comments = computed(() => {
    const focus = this.focus();
    return focus ? quarterComments(focus, this.history()) : [];
  });

  /** When the employee last confirmed the focused quarter (also shown after the manager changed it). */
  protected readonly lastConfirmedAt = computed(() => {
    const focus = this.focus();
    if (!focus) return null;
    const confirms = focus.targets
      .flatMap((target) => this.history()[target.id] ?? [])
      .filter((entry) => entry.type === 'CONFIRM')
      .map((entry) => entry.createdAt)
      .sort();
    return confirms[confirms.length - 1] ?? focus.feedbackAt;
  });

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    const today = businessClock(new Date()).date;

    forkJoin({
      me: this.employees.me(),
      periods: this.evaluations.periods(),
      targets: this.targets.mine(),
      kpis: this.kpiApi.list({ pageSize: KPI_PAGE_SIZE }),
    })
      .pipe(
        switchMap(({ me, periods, targets, kpis }) => {
          const quarterIds = periods
            .filter(
              (p) =>
                p.periodType === 'QUARTER' && targets.some((t) => t.evaluationPeriodId === p.id),
            )
            .map((p) => p.id);
          const daily$ = quarterIds.length
            ? forkJoin(
                quarterIds.map((id) =>
                  this.targets.suggestedDaily(me.id, id).pipe(catchError(() => of(null))),
                ),
              )
            : of([]);
          return daily$.pipe(
            map((dailies) => {
              const daily = Object.fromEntries(
                dailies.filter((d) => d !== null).map((d) => [d.evaluationPeriodId, d]),
              );
              return {
                me,
                view: buildYearView({ periods, targets, daily, kpis: kpis.items, today }),
              };
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ me, view }) => {
          this.me.set(me);
          this.view.set(view);
          this.loading.set(false);
          const focus = view
            ? (pendingQuarter(view) ?? view.quarters[view.quarters.length - 1])
            : null;
          if (focus) {
            this.expanded.set(new Set([focus.periodId]));
            this.loadHistory(focus);
          }
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  private loadHistory(quarter: QuarterRow): void {
    forkJoin(
      quarter.targets.map((target) =>
        this.targets
          .feedback(target.id)
          .pipe(catchError(() => of([] as TargetAssignmentFeedback[]))),
      ),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((lists) =>
        this.history.set(
          Object.fromEntries(quarter.targets.map((target, i) => [target.id, lists[i]])),
        ),
      );
  }

  protected toggle(periodId: string): void {
    this.expanded.update((open) => {
      const next = new Set(open);
      if (!next.delete(periodId)) next.add(periodId);
      return next;
    });
  }

  /** "Quý 4/2026" -> "Quý 4": the badge and button name the quarter, the year is in the title. */
  protected shortName(quarter: QuarterRow): string {
    return quarter.name.replace(/\/\d{4}$/, '');
  }

  protected monthLabel(month: MonthRow): string {
    const [year, number] = month.key.split('-');
    return this.i18n.translate('myTargets.monthLabel', { month: Number(number), year });
  }

  protected value(values: Readonly<Record<string, number | null>>, kpiId: string): string {
    return displayNumber(
      values[kpiId],
      this.i18n.currentLanguage(),
      this.localization.department.notAvailable,
    );
  }

  /** "Trưởng phòng X giao lúc ...", or just the time when the manager is unknown. */
  protected assignedText(quarter: QuarterRow): string {
    const time = this.dateTime(quarter.assignedAt);
    const manager = this.me()?.managerName;
    return manager
      ? this.i18n.translate('myTargets.assignedBy', { manager, time })
      : this.i18n.translate('myTargets.assignedAt', { time });
  }

  protected number(value: number): string {
    return displayNumber(
      value,
      this.i18n.currentLanguage(),
      this.localization.department.notAvailable,
    );
  }

  protected dateTime(value: string | null): string {
    return this.i18n.formatDate(value, 'HH:mm:ss DD/MM/YYYY');
  }

  protected day(value: string): string {
    return this.i18n.formatDate(value, 'DD/MM');
  }

  protected get commentLength(): number {
    return this.comment.trim().length;
  }

  // ---- Confirm and feedback ------------------------------------------------------------------

  protected confirm(): void {
    const quarter = this.pending();
    if (!quarter || this.sending()) return;
    this.sending.set(true);
    const open = quarter.targets.filter((target) => target.feedbackStatus !== 'CONFIRMED');
    forkJoin(
      open.map((target) =>
        // 409 means it was already confirmed since the last change: nothing left to do for that target.
        this.targets
          .confirm(target.id)
          .pipe(
            catchError((error: HttpErrorResponse) =>
              error.status === CONFLICT ? of(null) : throwError(() => error),
            ),
          ),
      ),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.showToast(
            'success',
            this.i18n.translate('myTargets.confirmed', { quarter: this.shortName(quarter) }),
          );
          this.load();
        },
        error: () => {
          this.sending.set(false);
          this.showToast('error', this.localization.myTargets.confirmFailed);
        },
      });
  }

  protected sendComment(): void {
    const quarter = this.focus();
    const text = this.comment.trim();
    this.commentError.set(false);
    if (!quarter || this.sending()) return;
    if (text.length === 0 || text.length > MAX_COMMENT_LENGTH) {
      this.commentError.set(true);
      return;
    }
    this.sending.set(true);
    forkJoin(quarter.targets.map((target) => this.targets.comment(target.id, text)))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.comment = '';
          this.showToast('success', this.localization.myTargets.commentSent);
          this.loadHistory(quarter);
        },
        error: () => {
          this.sending.set(false);
          this.showToast('error', this.localization.myTargets.commentFailed);
        },
      });
  }

  // ---- Week / day panel ----------------------------------------------------------------------

  protected openPanel(
    quarter: QuarterRow,
    month: MonthRow,
    granularity: Granularity = 'WEEK',
  ): void {
    this.panel.set({
      periodId: quarter.periodId,
      month,
      granularity,
      slices: [],
      loading: true,
      failed: false,
    });
    forkJoin(
      quarter.targets.map((target) =>
        this.targets.breakdown(target.id, granularity).pipe(
          map((breakdown) => ({
            kpiDefinitionId: target.kpiDefinitionId,
            items: breakdown.items,
          })),
        ),
      ),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (breakdowns) =>
          this.panel.update((current) =>
            current && current.month.key === month.key && current.granularity === granularity
              ? { ...current, slices: slicesOfMonth(month, breakdowns), loading: false }
              : current,
          ),
        error: () =>
          this.panel.update((current) =>
            current ? { ...current, loading: false, failed: true } : current,
          ),
      });
  }

  protected setGranularity(granularity: Granularity): void {
    const current = this.panel();
    const quarter = this.view()?.quarters.find((q) => q.periodId === current?.periodId);
    if (current && quarter && current.granularity !== granularity) {
      this.openPanel(quarter, current.month, granularity);
    }
  }

  protected closePanel(): void {
    this.panel.set(null);
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
