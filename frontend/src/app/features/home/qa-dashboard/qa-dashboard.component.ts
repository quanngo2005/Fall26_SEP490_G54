import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, Subject, switchMap, tap } from 'rxjs';
import { AuthService } from '../../../core/auth.service';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { FULL_ACCESS_PERMISSION, normalizePermission } from '../../../core/organization.guard';
import { QaDashboard, QaDashboardFilterOptions } from '../../../core/qa-dashboard.models';
import { QaDashboardService } from '../../../core/qa-dashboard.service';
import { RouteStateService, stateValue } from '../../../core/route-state.service';
import { SelectOption, UiSelectComponent } from '../../../shared/ui';
import {
  daysUntil,
  fill,
  formatNumber,
  NOTICE_WINDOW_DAYS,
  shortDatePattern,
} from './qa-dashboard-display';
import { QaEmployeesComponent } from './qa-employees.component';
import { QaReasonsComponent } from './qa-reasons.component';
import { QaTrendChartComponent } from './qa-trend-chart.component';

/** Permission of the transaction import screen; only then does the deadline notice link to it. */
export const TRANSACTION_IMPORT_PERMISSION = 'TRANSACTION_IMPORT_MANAGE';

/**
 * Filters of the dashboard. They are record ids, so they live in the navigation state instead of the URL; the
 * reconciliation list reads the same state names, so they are passed on as is.
 */
interface DashboardQuery {
  period: string;
  department: string;
  job: string;
}

type CardTone = 'red' | 'amber' | 'blue' | 'slate';

interface Card {
  id: string;
  tone: CardTone;
  label: string;
  count: number;
  pill: string;
  route: string;
  params: Record<string, string> | null;
  /** Record ids for the target screen, carried in the navigation state. */
  state?: Record<string, string>;
}

function readQuery(state: unknown): DashboardQuery {
  return {
    period: stateValue(state, 'period') ?? '',
    department: stateValue(state, 'department') ?? '',
    job: stateValue(state, 'job') ?? '',
  };
}

/** SCR-PRF-04 · QA dashboard (UC118): the QA role's home. Read-only figures that each lead to the screen where the work is done. */
@Component({
  selector: 'app-qa-dashboard',
  imports: [
    RouterLink,
    UiSelectComponent,
    QaReasonsComponent,
    QaEmployeesComponent,
    QaTrendChartComponent,
  ],
  templateUrl: './qa-dashboard.component.html',
  styleUrl: './qa-dashboard.component.scss',
})
export class QaDashboardComponent extends LocalizedComponent {
  private readonly service = inject(QaDashboardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly routeState = inject(RouteStateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly user = inject(AuthService).currentUser;
  private readonly reload = new Subject<void>();

  protected readonly data = signal<QaDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly options = signal<QaDashboardFilterOptions | null>(null);

  private readonly query$ = this.routeState.watchState().pipe(map(readQuery));
  protected readonly query = toSignal(this.query$, { initialValue: readQuery(null) });

  protected readonly text = computed(() => this.localization.qaDashboard);

  protected readonly canImport = computed(() =>
    (this.user()?.permissions ?? []).some((permission) => {
      const code = normalizePermission(permission);
      return code === TRANSACTION_IMPORT_PERMISSION || code === FULL_ACCESS_PERMISSION;
    }),
  );

  /** Filters carried over to the reconciliation screen in the navigation state (it uses the same names). */
  protected readonly linkState = computed<Record<string, string>>(() => {
    const query = this.query();
    const period = query.period || this.data()?.period.id || '';
    return {
      ...(period ? { period } : {}),
      ...(query.department ? { department: query.department } : {}),
      ...(query.job ? { job: query.job } : {}),
    };
  });

  protected readonly periodOptions = computed<SelectOption<string>[]>(() => {
    const periods = this.options()?.periods ?? [];
    const current = this.data()?.period;
    const list = periods.map((p) => ({ value: p.id, label: this.periodLabel(p.name) }));
    if (current && !list.some((p) => p.value === current.id)) {
      list.unshift({ value: current.id, label: this.periodLabel(current.name) });
    }
    return list;
  });

  protected readonly departmentOptions = computed<SelectOption<string>[]>(() => [
    { value: '', label: this.text().filters.all },
    ...(this.options()?.departments ?? []).map((d) => ({ value: d.id, label: d.name })),
  ]);

  protected readonly jobOptions = computed<SelectOption<string>[]>(() => [
    { value: '', label: this.text().filters.all },
    ...(this.options()?.jobPositions ?? []).map((j) => ({ value: j.id, label: j.title })),
  ]);

  protected readonly selectedPeriod = computed(
    () => this.query().period || this.data()?.period.id || '',
  );

  protected readonly hasFilters = computed(() => {
    const query = this.query();
    return !!(query.period || query.department || query.job);
  });

  protected readonly asOf = computed(() => {
    const data = this.data();
    if (!data) return '';
    const pattern = `HH:mm · ${this.i18n.currentFormatConfig().date}`;
    return fill(this.text().filters.asOf, { time: this.i18n.formatDate(data.asOf, pattern) });
  });

  /** One line about the lock deadline; hidden while it is more than a week away and nothing waits for an import. */
  protected readonly notice = computed(() => {
    const data = this.data();
    if (!data) return null;
    const text = this.text().notice;
    const { period, banner } = data;
    if (period.isLocked) {
      return { locked: true, deadline: fill(text.locked, { period: period.name }), detail: '' };
    }
    const days = daysUntil(period.lockDeadline);
    if (days > NOTICE_WINDOW_DAYS && banner.inReviewCount === 0) return null;
    const values = {
      period: period.name,
      time: this.i18n.formatDate(period.lockDeadline, 'HH:mm'),
      date: this.shortDate(period.lockDeadline),
      days,
    };
    const template =
      days > 0 ? text.deadline : days === 0 ? text.deadlineToday : text.deadlinePassed;
    return {
      locked: false,
      deadline: fill(template, values),
      detail: banner.inReviewCount > 0 ? fill(text.inReview, { count: banner.inReviewCount }) : '',
    };
  });

  protected readonly cards = computed<Card[]>(() => {
    const data = this.data();
    if (!data) return [];
    const text = this.text().cards;
    const { notMatched, inReview, qaQueue, riskEvents } = data.kpiCards;
    const state = this.linkState();
    const join = (parts: (string | null)[]) => parts.filter((p): p is string => !!p).join(' · ');
    const locale = this.i18n.currentLanguage();
    return [
      {
        id: 'notMatched',
        tone: 'red',
        label: text.notMatched,
        count: notMatched.count,
        pill:
          notMatched.count === 0 || notMatched.oldestDays == null
            ? ''
            : notMatched.oldestDays > 0
              ? fill(text.oldest, { days: notMatched.oldestDays })
              : text.oldestToday,
        route: '/reconciliations',
        params: { status: 'NOT_MATCHED' },
        state,
      },
      {
        id: 'inReview',
        tone: 'amber',
        label: text.inReview,
        count: inReview.count,
        pill:
          inReview.count > 0 && !data.period.isLocked
            ? fill(text.importBy, { date: this.shortDate(data.period.lockDeadline) })
            : '',
        route: '/reconciliations',
        params: { status: 'IN_REVIEW' },
        state,
      },
      {
        id: 'qaQueue',
        tone: 'blue',
        label: text.qaQueue,
        count: qaQueue.total,
        pill: join([
          qaQueue.sampleCount > 0 ? fill(text.samples, { count: qaQueue.sampleCount }) : null,
          qaQueue.flagCount > 0 ? fill(text.flags, { count: qaQueue.flagCount }) : null,
          qaQueue.aiSegmentCount ? fill(text.aiSegments, { count: qaQueue.aiSegmentCount }) : null,
        ]),
        route: '/qa-queue',
        params: null,
      },
      {
        id: 'riskEvents',
        tone: 'slate',
        label: text.riskEvents,
        count: riskEvents.count,
        pill: join([
          riskEvents.openCount ? fill(text.openEvents, { count: riskEvents.openCount }) : null,
          riskEvents.totalPenaltyPoints
            ? fill(text.penalty, {
                points: formatNumber(locale, Math.abs(riskEvents.totalPenaltyPoints), 0),
              })
            : null,
        ]),
        route: '/risk-events',
        params: null,
      },
    ];
  });

  constructor() {
    super();
    this.service
      .filters()
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => this.options.set(options));

    combineLatest([this.query$, this.reload.pipe(startWith(null))])
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap(([query]) =>
          this.service
            .get({
              periodId: query.period || null,
              departmentId: query.department || null,
              jobPositionId: query.job || null,
            })
            .pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.data.set(data);
        this.failed.set(data === null);
        this.loading.set(false);
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected setFilter(key: keyof DashboardQuery, value: string): void {
    const next = { ...this.query(), [key]: value };
    const state = Object.fromEntries(Object.entries(next).filter(([, v]) => v !== ''));
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, state });
  }

  protected resetFilters(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, state: {} });
  }

  protected cardAria(card: Card): string {
    return fill(this.text().cards.open, {
      label: [card.label, card.count, card.pill].filter((p) => p !== '').join(', '),
    });
  }

  /** Monthly periods are named "10/2026"; the selector reads them as "Tháng 10/2026". */
  private periodLabel(name: string): string {
    return /^\d{1,2}\/\d{4}$/.test(name) ? fill(this.text().filters.periodName, { name }) : name;
  }

  private shortDate(value: string): string {
    return this.i18n.formatDate(value, shortDatePattern(this.i18n.currentFormatConfig().date));
  }
}
