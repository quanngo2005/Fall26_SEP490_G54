import {
  HostListener,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, EMPTY, finalize, of, Subject, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { currentPeriod, sortPeriodsNewestFirst } from '../../core/period-options';
import {
  ReconciliationCounts,
  ReconciliationFilter,
  ReconciliationFilterOptions,
  ReconciliationItem,
  ReconciliationReason,
  ReconciliationStatus,
} from '../../core/reconciliation.models';
import { ReconciliationService } from '../../core/reconciliation.service';
import { canManageTransactionImport } from '../../core/transaction-import.guard';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';
import { saveFile } from '../performance-dashboard/performance-page';
import {
  readSavedFilters,
  SavedReconciliationFilters,
  statusTone,
  writeSavedFilters,
} from './reconciliation-display';

const SEARCH_DEBOUNCE_MS = 300;
const ALL = 'ALL';
const EMPTY_OPTIONS: ReconciliationFilterOptions = {
  departments: [],
  jobPositions: [],
  products: [],
  reasons: [],
};

type StatusCard = { key: 'ALL' | ReconciliationStatus; label: string; count: number };

/** SCR-REV-03: every transaction code against the imported Core Banking file, with the filters of QA and managers. */
@Component({
  selector: 'app-reconciliation-list',
  imports: [
    FormsModule,
    RouterLink,
    SidebarComponent,
    HeaderComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
    AppDatePipe,
    AppNumberPipe,
  ],
  templateUrl: './reconciliation-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './reconciliation-list.component.scss',
  ],
})
export class ReconciliationListComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(ReconciliationService);
  private readonly auth = inject(AuthService);
  private readonly evaluations = inject(EvaluationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** SCR-ADM-06 entry point: only those who may import the Core Banking file see it (the API checks the same). */
  protected readonly canImport = computed(() =>
    canManageTransactionImport(this.auth.currentUser()?.permissions),
  );
  private readonly reload = new Subject<void>();
  private readonly searchChanged = new Subject<void>();

  protected readonly statusFilter = signal<'ALL' | ReconciliationStatus>('ALL');
  protected readonly searchQuery = signal('');
  protected readonly periodFilter = signal(ALL);
  protected readonly dateFrom = signal('');
  protected readonly dateTo = signal('');
  protected readonly departmentFilter = signal(ALL);
  protected readonly jobFilter = signal(ALL);
  protected readonly productFilter = signal(ALL);
  protected readonly reasonFilter = signal(ALL);
  protected readonly page = signal(1);

  protected readonly items = signal<ReconciliationItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly counts = signal<ReconciliationCounts>({
    all: 0,
    notMatched: 0,
    inReview: 0,
    matched: 0,
  });
  protected readonly options = signal<ReconciliationFilterOptions>(EMPTY_OPTIONS);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly exporting = signal(false);
  protected readonly exportFailed = signal(false);
  protected readonly notice = signal<string | null>(null);

  protected readonly cards = computed<StatusCard[]>(() => {
    const text = this.localization.reconciliation;
    const counts = this.counts();
    return [
      { key: 'ALL', label: text.cards.all, count: counts.all },
      { key: 'NOT_MATCHED', label: text.status.NOT_MATCHED, count: counts.notMatched },
      { key: 'IN_REVIEW', label: text.status.IN_REVIEW, count: counts.inReview },
      { key: 'MATCHED', label: text.status.MATCHED, count: counts.matched },
    ];
  });

  protected readonly periodOptions = computed<SelectOption<string>[]>(() => [
    { value: ALL, label: this.localization.reconciliation.allPeriods },
    ...this.periods().map((p) => ({ value: p.id, label: p.code })),
  ]);
  protected readonly departmentOptions = computed<SelectOption<string>[]>(() => [
    { value: ALL, label: this.localization.reconciliation.allDepartments },
    ...this.options().departments.map((d) => ({ value: d.id, label: d.name })),
  ]);
  protected readonly jobOptions = computed<SelectOption<string>[]>(() => [
    { value: ALL, label: this.localization.reconciliation.allJobs },
    ...this.options().jobPositions.map((j) => ({ value: j.id, label: j.title })),
  ]);
  protected readonly productOptions = computed<SelectOption<string>[]>(() => [
    { value: ALL, label: this.localization.reconciliation.allProducts },
    ...this.options().products.map((p) => ({
      value: p.code,
      label: p.name ? `${p.name} (${p.code})` : p.code,
    })),
  ]);
  protected readonly reasonOptions = computed<SelectOption<string>[]>(() => [
    { value: ALL, label: this.localization.reconciliation.allReasons },
    ...this.options().reasons.map((r) => ({ value: r, label: this.reasonLabel(r) })),
  ]);

  protected readonly hasFilters = computed(
    () =>
      this.statusFilter() !== ALL ||
      this.searchQuery().trim() !== '' ||
      this.periodFilter() !== ALL ||
      this.dateFrom() !== '' ||
      this.dateTo() !== '' ||
      this.departmentFilter() !== ALL ||
      this.jobFilter() !== ALL ||
      this.productFilter() !== ALL ||
      this.reasonFilter() !== ALL,
  );

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          this.syncUrl();
          return this.api.list({ ...this.filter(), page: this.page() }).pipe(
            tap((list) => {
              this.items.set(list.items);
              this.totalCount.set(list.totalCount);
              this.pageSize.set(list.pageSize);
              this.counts.set(list.counts);
              this.options.set(list.options ?? EMPTY_OPTIONS);
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
      .subscribe(() => this.refilter());
  }

  ngOnInit(): void {
    const query = this.route.snapshot.queryParamMap;
    const fromUrl = query.keys.length > 0;
    const saved = fromUrl ? null : readSavedFilters();
    if (fromUrl) {
      this.applyFilters({
        status: query.get('status') ?? ALL,
        search: query.get('q') ?? '',
        period: query.get('period') ?? ALL,
        from: query.get('from') ?? '',
        to: query.get('to') ?? '',
        department: query.get('department') ?? ALL,
        jobPosition: query.get('job') ?? ALL,
        product: query.get('product') ?? ALL,
        reason: query.get('reason') ?? ALL,
      });
      const requested = Number(query.get('page'));
      if (requested > 1) this.page.set(requested);
    } else if (saved) {
      this.applyFilters(saved);
    }

    // The period list is supplementary: the screen still works without it. With nothing chosen or saved it opens on the
    // current month so the first load is small.
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((periods) => {
        this.periods.set(sortPeriodsNewestFirst(periods));
        if (!fromUrl && !saved) {
          const current = currentPeriod(periods);
          if (current) this.periodFilter.set(current.id);
        }
        this.reload.next();
      });
  }

  protected retry(): void {
    this.reload.next();
  }

  protected setStatus(status: 'ALL' | ReconciliationStatus): void {
    this.statusFilter.set(status);
    this.refilter();
  }

  protected setSearch(value: string): void {
    this.searchQuery.set(value);
    this.searchChanged.next();
  }

  protected setPeriod(value: string): void {
    this.periodFilter.set(value);
    // A period chosen on purpose replaces the dates typed before it.
    this.dateFrom.set('');
    this.dateTo.set('');
    this.refilter();
  }

  protected setDate(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.dateFrom : this.dateTo).set(value);
    this.refilter();
  }

  protected setDepartment(value: string): void {
    this.departmentFilter.set(value);
    this.refilter();
  }

  protected setJob(value: string): void {
    this.jobFilter.set(value);
    this.refilter();
  }

  protected setProduct(value: string): void {
    this.productFilter.set(value);
    this.refilter();
  }

  protected setReason(value: string): void {
    this.reasonFilter.set(value);
    this.refilter();
  }

  protected clearFilters(): void {
    this.applyFilters({
      status: ALL,
      search: '',
      period: ALL,
      from: '',
      to: '',
      department: ALL,
      jobPosition: ALL,
      product: ALL,
      reason: ALL,
    });
    writeSavedFilters(null);
    this.notice.set(null);
    this.refilter();
  }

  protected saveFilters(): void {
    const saved = writeSavedFilters(this.snapshot());
    this.notice.set(saved ? this.localization.reconciliation.filtersSaved : null);
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.reload.next();
  }

  protected open(item: ReconciliationItem): void {
    void this.router.navigate(['/reconciliations', item.id]);
  }

  protected exportFile(): void {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.exportFailed.set(false);
    this.api
      .export(this.filter())
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => saveFile(blob, `doi-soat-${new Date().toISOString().slice(0, 10)}.xlsx`),
        error: () => this.exportFailed.set(true),
      });
  }

  protected tone(item: ReconciliationItem): StatusBadgeTone {
    return statusTone(item.status);
  }

  protected statusLabel(status: ReconciliationStatus): string {
    return this.localization.reconciliation.status[status];
  }

  protected reasonLabel(reason: string | null): string {
    if (!reason) return '';
    return (this.localization.reconciliation.reasons as Record<string, string>)[reason] ?? reason;
  }

  protected amountText(amount: number | null): string {
    return amount === null
      ? this.localization.reconciliation.noValue
      : this.i18n.formatNumber(amount);
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('reconciliation.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
    });
  }

  /** Escape closes the notice. */
  @HostListener('document:keydown.escape')
  protected dismissNotice(): void {
    this.notice.set(null);
  }

  private refilter(): void {
    this.page.set(1);
    this.reload.next();
  }

  private filter(): ReconciliationFilter {
    const status = this.statusFilter();
    const period = this.periodFilter();
    const department = this.departmentFilter();
    const job = this.jobFilter();
    const product = this.productFilter();
    const reason = this.reasonFilter();
    return {
      ...(status !== ALL ? { status } : {}),
      ...(period !== ALL ? { periodId: period } : {}),
      ...(this.dateFrom() ? { from: this.dateFrom() } : {}),
      ...(this.dateTo() ? { to: this.dateTo() } : {}),
      ...(department !== ALL ? { departmentId: department } : {}),
      ...(job !== ALL ? { jobPositionId: job } : {}),
      ...(product !== ALL ? { productCode: product } : {}),
      ...(reason !== ALL ? { reason: reason as ReconciliationReason } : {}),
      ...(this.searchQuery().trim() ? { search: this.searchQuery().trim() } : {}),
    };
  }

  private snapshot(): SavedReconciliationFilters {
    return {
      status: this.statusFilter(),
      search: this.searchQuery(),
      period: this.periodFilter(),
      from: this.dateFrom(),
      to: this.dateTo(),
      department: this.departmentFilter(),
      jobPosition: this.jobFilter(),
      product: this.productFilter(),
      reason: this.reasonFilter(),
    };
  }

  private applyFilters(filters: SavedReconciliationFilters): void {
    this.statusFilter.set(filters.status as 'ALL' | ReconciliationStatus);
    this.searchQuery.set(filters.search);
    this.periodFilter.set(filters.period);
    this.dateFrom.set(filters.from);
    this.dateTo.set(filters.to);
    this.departmentFilter.set(filters.department);
    this.jobFilter.set(filters.jobPosition);
    this.productFilter.set(filters.product);
    this.reasonFilter.set(filters.reason);
  }

  /** Keeps the filters in the address, so a refresh or a shared link opens the same list. */
  private syncUrl(): void {
    const all = this.snapshot();
    const queryParams: Record<string, string | null> = {
      status: all.status !== ALL ? all.status : null,
      q: all.search.trim() || null,
      // Always written, so a deliberate "all periods" survives a refresh instead of falling back to the current month.
      period: all.period,
      from: all.from || null,
      to: all.to || null,
      department: all.department !== ALL ? all.department : null,
      job: all.jobPosition !== ALL ? all.jobPosition : null,
      product: all.product !== ALL ? all.product : null,
      reason: all.reason !== ALL ? all.reason : null,
      page: this.page() > 1 ? String(this.page()) : null,
    };
    void this.router.navigate([], { relativeTo: this.route, queryParams, replaceUrl: true });
  }
}
