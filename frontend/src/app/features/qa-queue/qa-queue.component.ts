import { UpperCasePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, debounceTime, EMPTY, finalize, of, Subject, switchMap, tap } from 'rxjs';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { currentPeriod, sortPeriodsNewestFirst } from '../../core/period-options';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  QaDepartmentOption,
  QaKind,
  QaQueueCounts,
  QaQueueItem,
  QaTab,
} from '../../core/qa-queue.models';
import { QaQueueService } from '../../core/qa-queue.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { daysUntil, QaQueueContext } from './qa-queue-display';

const SEARCH_DEBOUNCE_MS = 300;

/** Without a chosen column the queue shows pending items by due date. */
const DEFAULT_SORT_KEY = 'due';
const DEFAULT_SORT_DIR = 'asc';
const DEFAULT_DESCENDING = ['created'];

@Component({
  selector: 'app-qa-queue',
  imports: [FormsModule, SidebarComponent, HeaderComponent, AppDatePipe, UpperCasePipe],
  templateUrl: './qa-queue.component.html',
  styleUrls: [
    '../appeals/appeals-shared.scss',
    '../appeals/appeals-list.scss',
    '../../shared/ui/sortable-table.scss',
    './qa-queue.component.scss',
    './qa-queue-badges.scss',
  ],
})
export class QaQueueComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(QaQueueService);
  private readonly evaluations = inject(EvaluationService);
  private readonly router = inject(Router);
  private readonly context = inject(QaQueueContext);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private readonly searchChanged = new Subject<void>();

  protected readonly activeTab = signal<QaTab>('PENDING');
  protected readonly kindFilter = signal<QaKind | 'ALL'>('ALL');
  protected readonly departmentFilter = signal('ALL');
  protected readonly periodFilter = signal('ALL');
  protected readonly searchQuery = signal('');
  protected readonly page = signal(1);

  /** The column the list is sorted by; null means the default order of the screen. */
  protected readonly sortKey = signal<string | null>(null);
  protected readonly sortDirection = signal<'asc' | 'desc'>('asc');

  protected readonly items = signal<QaQueueItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(0);
  protected readonly counts = signal<QaQueueCounts>({ pending: 0, confirmed: 0, rejected: 0 });
  protected readonly departments = signal<QaDepartmentOption[]>([]);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

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
          const kind = this.kindFilter();
          const department = this.departmentFilter();
          const period = this.periodFilter();
          return this.api
            .list({
              tab: this.activeTab(),
              ...(kind !== 'ALL' ? { kind } : {}),
              ...(department !== 'ALL' ? { departmentId: department } : {}),
              ...(period !== 'ALL' ? { periodId: period } : {}),
              search: this.searchQuery().trim(),
              ...(this.sortKey() ? { sortBy: this.sortKey()!, sortDir: this.sortDirection() } : {}),
              page: this.page(),
            })
            .pipe(
              tap((queue) => {
                this.items.set(queue.items);
                this.totalCount.set(queue.totalCount);
                this.pageSize.set(queue.pageSize);
                this.counts.set(queue.counts);
                this.departments.set(queue.departments ?? []);
                this.context.items.set(queue.items.map((i) => ({ kind: i.kind, id: i.id })));
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

  protected setTab(tab: QaTab): void {
    this.activeTab.set(tab);
    this.page.set(1);
    this.reload.next();
  }

  protected setKind(kind: QaKind | 'ALL'): void {
    this.kindFilter.set(kind);
    this.page.set(1);
    this.reload.next();
  }

  protected setDepartment(departmentId: string): void {
    this.departmentFilter.set(departmentId);
    this.page.set(1);
    this.reload.next();
  }

  protected setPeriod(periodId: string): void {
    this.periodFilter.set(periodId);
    this.page.set(1);
    this.reload.next();
  }

  protected setSearch(value: string): void {
    this.searchQuery.set(value);
    this.searchChanged.next();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.reload.next();
  }

  protected open(item: QaQueueItem): void {
    void this.router.navigate(['/qa-queue', item.kind, item.id]);
  }

  protected kindLabel(kind: QaKind): string {
    return kind === 'FLAG'
      ? this.localization.qaQueue.kindFlag
      : this.localization.qaQueue.kindSample;
  }

  protected statusLabel(item: QaQueueItem): string {
    return this.localization.qaQueue.status[item.status];
  }

  protected sourceLabel(item: QaQueueItem): string {
    return this.localization.qaQueue.source[item.source];
  }

  /** "còn 5 ngày" while the item is open; closed items show no deadline. */
  protected dueText(item: QaQueueItem): string {
    if (item.tab !== 'PENDING') return '';
    const days = daysUntil(item.dueAt);
    if (days > 0) return this.i18n.translate('qaQueue.dueDays', { days });
    if (days === 0) return this.localization.qaQueue.dueToday;
    return this.i18n.translate('qaQueue.dueOverdue', { days: -days });
  }

  protected isUrgent(item: QaQueueItem): boolean {
    return item.tab === 'PENDING' && daysUntil(item.dueAt) <= 1;
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('qaQueue.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
    });
  }
}
