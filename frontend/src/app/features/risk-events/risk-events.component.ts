import { UpperCasePipe } from '@angular/common';
import {
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
import { catchError, debounceTime, EMPTY, finalize, of, Subject, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { currentPeriod, sortPeriodsNewestFirst } from '../../core/period-options';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { canCreateRiskEvents } from '../../core/risk-event.guard';
import {
  RISK_SEVERITIES,
  RiskEventCounts,
  RiskEventDepartment,
  RiskEventListItem,
  RiskSeverity,
  RiskState,
} from '../../core/risk-event.models';
import { RiskEventService } from '../../core/risk-event.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { RiskEventCreateModalComponent } from './risk-event-create-modal.component';
import { RiskEventDrawerComponent } from './risk-event-drawer.component';

const SEARCH_DEBOUNCE_MS = 300;

const DEFAULT_SORT_KEY = 'occurredAt';
const DEFAULT_SORT_DIR = 'desc';
/** Columns whose first click shows the newest / biggest first. */
const DEFAULT_DESCENDING = ['occurredAt', 'penalty', 'severity'];

@Component({
  selector: 'app-risk-events',
  imports: [
    FormsModule,
    SidebarComponent,
    HeaderComponent,
    AppDatePipe,
    UpperCasePipe,
    RiskEventDrawerComponent,
    RiskEventCreateModalComponent,
  ],
  templateUrl: './risk-events.component.html',
  styleUrls: [
    '../appeals/appeals-shared.scss',
    '../appeals/appeals-list.scss',
    './risk-events-badges.scss',
    '../../shared/ui/sortable-table.scss',
    './risk-events.component.scss',
  ],
})
export class RiskEventsComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(RiskEventService);
  private readonly evaluations = inject(EvaluationService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private readonly searchChanged = new Subject<void>();

  protected readonly severities = RISK_SEVERITIES;
  protected readonly stateFilter = signal<RiskState | 'ALL'>('ALL');
  protected readonly severityFilter = signal<RiskSeverity | 'ALL'>('ALL');
  protected readonly departmentFilter = signal('ALL');
  protected readonly periodFilter = signal('ALL');
  protected readonly searchQuery = signal('');
  protected readonly page = signal(1);

  /** The column the list is sorted by; null means the default order of the screen. */
  protected readonly sortKey = signal<string | null>(null);
  protected readonly sortDirection = signal<'asc' | 'desc'>('asc');

  protected readonly items = signal<RiskEventListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(0);
  protected readonly counts = signal<RiskEventCounts>({ recorded: 0, reversed: 0 });
  protected readonly departments = signal<RiskEventDepartment[]>([]);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly notice = signal<string | null>(null);

  protected readonly canCreate = computed(() =>
    canCreateRiskEvents(this.auth.currentUser()?.permissions),
  );
  protected readonly pages = computed(() => {
    const size = this.pageSize();
    return size > 0
      ? Array.from({ length: Math.ceil(this.totalCount() / size) }, (_, i) => i + 1)
      : [];
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const state = this.stateFilter();
          const severity = this.severityFilter();
          const department = this.departmentFilter();
          const period = this.periodFilter();
          return this.api
            .list({
              ...(state !== 'ALL' ? { state } : {}),
              ...(severity !== 'ALL' ? { severity } : {}),
              ...(department !== 'ALL' ? { departmentId: department } : {}),
              ...(period !== 'ALL' ? { periodId: period } : {}),
              search: this.searchQuery().trim(),
              ...(this.sortKey() ? { sortBy: this.sortKey()!, sortDir: this.sortDirection() } : {}),
              page: this.page(),
            })
            .pipe(
              tap((list) => {
                this.items.set(list.items);
                this.totalCount.set(list.totalCount);
                this.pageSize.set(list.pageSize);
                this.counts.set(list.counts);
                this.departments.set(list.departments ?? []);
              }),
              catchError(() => {
                this.failed.set(true);
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    this.searchChanged
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page.set(1);
        this.reload.next();
      });
  }

  ngOnInit(): void {
    // The period filter is supplementary: the list must still work when the periods cannot be read. It opens on the
    // current period (a month rather than the quarter or year around it) so the first load is small.
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((periods) => {
        this.periods.set(sortPeriodsNewestFirst(periods));
        const current = currentPeriod(periods);
        if (current) this.periodFilter.set(current.id);
        this.reload.next();
      });
  }

  /** Header click: sort by this column ascending (descending for dates and amounts), a second click reverses it. */
  protected toggleSort(key: string): void {
    if (this.sortKey() === key) {
      this.sortDirection.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDirection.set(DEFAULT_DESCENDING.includes(key) ? 'desc' : 'asc');
    }
    this.page.set(1);
    this.reload.next();
  }

  protected ariaSort(key: string): 'ascending' | 'descending' | 'none' {
    if ((this.sortKey() ?? DEFAULT_SORT_KEY) !== key) return 'none';
    return (this.sortKey() === null ? DEFAULT_SORT_DIR : this.sortDirection()) === 'asc'
      ? 'ascending'
      : 'descending';
  }

  protected sortArrow(key: string): string {
    const state = this.ariaSort(key);
    return state === 'ascending' ? '▲' : state === 'descending' ? '▼' : '↕';
  }

  protected retry(): void {
    this.reload.next();
  }

  protected setState(state: RiskState | 'ALL'): void {
    this.stateFilter.set(state);
    this.refilter();
  }

  protected setSeverity(severity: RiskSeverity | 'ALL'): void {
    this.severityFilter.set(severity);
    this.refilter();
  }

  protected setDepartment(departmentId: string): void {
    this.departmentFilter.set(departmentId);
    this.refilter();
  }

  protected setPeriod(periodId: string): void {
    this.periodFilter.set(periodId);
    this.refilter();
  }

  protected setSearch(value: string): void {
    this.searchQuery.set(value);
    this.searchChanged.next();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.reload.next();
  }

  protected select(item: RiskEventListItem): void {
    this.selectedId.set(item.id);
  }

  protected closeDrawer(): void {
    this.selectedId.set(null);
  }

  protected openCreate(): void {
    this.notice.set(null);
    this.creating.set(true);
  }

  protected onCreated(result: { code: string; uploadFailed: boolean }): void {
    this.creating.set(false);
    this.notice.set(
      this.i18n.translate(
        result.uploadFailed ? 'riskEvents.createdUploadFailed' : 'riskEvents.created',
        {
          code: result.code,
        },
      ),
    );
    this.page.set(1);
    this.reload.next();
  }

  protected onReversed(code: string): void {
    this.notice.set(this.i18n.translate('riskEvents.reversed', { code }));
    this.reload.next();
  }

  protected severityLabel(severity: RiskSeverity): string {
    return this.localization.riskEvents.severity[severity];
  }

  protected stateLabel(item: RiskEventListItem): string {
    return this.localization.riskEvents.state[item.state];
  }

  protected typeLabel(code: string): string {
    return (this.localization.riskEvents.types as Record<string, string>)[code] ?? code;
  }

  protected penaltyText(points: number): string {
    return `−${this.i18n.formatNumber(points)}`;
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('riskEvents.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
    });
  }

  /** Up / down arrows move the open drawer to the neighbouring row, unless the user is typing. */
  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    const id = this.selectedId();
    if (!id || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return;
    const target = event.target as HTMLElement | null;
    if (target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) return;
    const rows = this.items();
    const index = rows.findIndex((row) => row.id === id);
    const next = rows[index + (event.key === 'ArrowDown' ? 1 : -1)];
    if (index >= 0 && next) {
      event.preventDefault();
      this.selectedId.set(next.id);
    }
  }

  private refilter(): void {
    this.page.set(1);
    this.reload.next();
  }
}
