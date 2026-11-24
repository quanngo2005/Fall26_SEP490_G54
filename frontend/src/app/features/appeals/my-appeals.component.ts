import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, EMPTY, finalize, forkJoin, of, Subject, switchMap, tap } from 'rxjs';
import { AppealDetail, AppealListItem, AppealStatus } from '../../core/appeal.models';
import { AppealService } from '../../core/appeal.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { RouteStateService } from '../../core/route-state.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { AppDatePipe } from '../../shared/pipes';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';
import {
  appealCode,
  appealErrorKey,
  appealSubject,
  OPEN_STATUSES,
  RESOLVED_STATUSES,
} from './appeal-display';
import { AppealSubmitModalComponent } from './appeal-submit-modal.component';
import { MyAppealDrawerComponent } from './my-appeal-drawer.component';

type MineTab = 'ALL' | 'OPEN' | 'UNDER_REVIEW' | 'RESOLVED';

const TAB_STATUSES: Readonly<Record<MineTab, readonly AppealStatus[]>> = {
  ALL: [],
  OPEN: ['OPEN'],
  UNDER_REVIEW: ['UNDER_REVIEW'],
  RESOLVED: RESOLVED_STATUSES,
};

const STATUS_TONE: Readonly<Record<AppealStatus, StatusBadgeTone>> = {
  OPEN: 'warning',
  UNDER_REVIEW: 'info',
  ACCEPTED: 'success',
  REJECTED: 'danger',
  WITHDRAWN: 'neutral',
};

@Component({
  selector: 'app-my-appeals',
  imports: [
    FormsModule,
    SidebarComponent,
    HeaderComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
    MyAppealDrawerComponent,
    AppealSubmitModalComponent,
    AppDatePipe,
  ],
  templateUrl: './my-appeals.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/modal.scss',
    './my-appeals.component.scss',
  ],
})
export class MyAppealsComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(AppealService);
  private readonly evaluations = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly routeState = inject(RouteStateService);
  private readonly reload = new Subject<void>();
  private readonly loadDetail = new Subject<string>();

  protected readonly activeTab = signal<MineTab>('ALL');
  protected readonly searchQuery = signal('');
  protected readonly periodFilter = signal('ALL');
  protected readonly page = signal(1);

  protected readonly items = signal<AppealListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(0);
  protected readonly counts = signal({ all: 0, open: 0, underReview: 0, resolved: 0 });
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly detail = signal<AppealDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly submitOpen = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly errorKey = signal<string | null>(null);

  /**
   * The appeal whose drawer is open. It normally comes from the list; an appeal opened by link (?appeal=) may be
   * outside the page or filter on screen, so its loaded detail stands in for the list row.
   */
  protected readonly selectedAppeal = computed<AppealListItem | null>(() => {
    const id = this.selectedId();
    const fromList = this.items().find((item) => item.id === id);
    if (fromList) return fromList;
    const loaded = this.detail();
    return loaded && loaded.id === id ? { ...loaded, jobPositionTitle: null } : null;
  });

  protected readonly filteredItems = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return this.items();
    return this.items().filter((item) => {
      const text = `${appealCode(item.id)} ${appealSubject(item)}`.toLowerCase();
      return text.includes(query);
    });
  });

  protected readonly periodOptions = computed<SelectOption<string>[]>(() => [
    { value: 'ALL', label: this.localization.appeals.allPeriods },
    ...this.periods().map((p) => ({ value: p.id, label: p.code })),
  ]);

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const base = { scope: 'MINE' as const, ...this.baseFilter() };
          return forkJoin({
            list: this.api.list({
              ...base,
              status: TAB_STATUSES[this.activeTab()],
              page: this.page(),
            }),
            all: this.api.list({ ...base, pageSize: 1 }),
            open: this.api.list({ ...base, status: ['OPEN'], pageSize: 1 }),
            underReview: this.api.list({ ...base, status: ['UNDER_REVIEW'], pageSize: 1 }),
            resolved: this.api.list({ ...base, status: RESOLVED_STATUSES, pageSize: 1 }),
          }).pipe(
            tap(({ list, all, open, underReview, resolved }) => {
              this.items.set(list.items);
              this.totalCount.set(list.totalCount);
              this.pageSize.set(list.pageSize);
              this.counts.set({
                all: all.totalCount,
                open: open.totalCount,
                underReview: underReview.totalCount,
                resolved: resolved.totalCount,
              });
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

    this.loadDetail
      .pipe(
        switchMap((id) => {
          this.detail.set(null);
          this.detailLoading.set(true);
          this.detailFailed.set(false);
          return this.api.detail(id).pipe(
            tap((detail) => this.detail.set(detail)),
            catchError(() => {
              this.detailFailed.set(true);
              return EMPTY;
            }),
            finalize(() => this.detailLoading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  ngOnInit(): void {
    // The period filter is supplementary: the list must still work when it cannot be loaded.
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((periods) => this.periods.set(periods));
    this.reload.next();

    // "View appeal" in the submission history passes the appeal in the navigation state.
    const linked = this.routeState.value('appealId');
    if (linked) {
      this.selectedId.set(linked);
      this.loadDetail.next(linked);
    }
  }

  protected retry(): void {
    this.reload.next();
  }

  protected setTab(tab: MineTab): void {
    this.activeTab.set(tab);
    this.page.set(1);
    this.reload.next();
  }

  protected setPeriod(periodId: string): void {
    this.periodFilter.set(periodId);
    this.page.set(1);
    this.reload.next();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.reload.next();
  }

  protected selectAppeal(item: AppealListItem): void {
    if (this.selectedId() === item.id) {
      this.closeDrawer();
      return;
    }
    this.selectedId.set(item.id);
    this.loadDetail.next(item.id);
  }

  protected closeDrawer(): void {
    this.selectedId.set(null);
    this.detail.set(null);
  }

  protected selectNext(): void {
    this.step(1);
  }

  protected selectPrev(): void {
    this.step(-1);
  }

  protected onWithdraw(appeal: AppealListItem): void {
    this.errorKey.set(null);
    this.api
      .withdraw(appeal.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showNotice(this.localization.appeals.withdrawn);
          this.reload.next();
          this.loadDetail.next(appeal.id);
        },
        error: (error: unknown) => this.errorKey.set(appealErrorKey(error)),
      });
  }

  protected openSubmit(): void {
    this.submitOpen.set(true);
  }

  protected onSubmitted(): void {
    this.submitOpen.set(false);
    this.showNotice(this.localization.appeals.submitted);
    this.page.set(1);
    this.reload.next();
  }

  protected statusLabel(status: AppealStatus): string {
    return this.localization.appeals.status[status];
  }

  protected statusTone(status: AppealStatus): StatusBadgeTone {
    return STATUS_TONE[status];
  }

  protected subject(item: AppealListItem): string {
    return appealSubject(item);
  }

  protected code(item: AppealListItem): string {
    return appealCode(item.id);
  }

  protected outcome(item: AppealListItem): string {
    if (item.status === 'WITHDRAWN') return this.localization.appeals.status.WITHDRAWN;
    if (OPEN_STATUSES.includes(item.status)) return this.localization.appeals.notAvailable;
    return item.note ?? this.localization.appeals.notAvailable;
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('appeals.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
    });
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  private baseFilter(): { periodId?: string } {
    const period = this.periodFilter();
    return {
      ...(period !== 'ALL' ? { periodId: period } : {}),
    };
  }

  private step(direction: 1 | -1): void {
    const list = this.filteredItems();
    const index = list.findIndex((item) => item.id === this.selectedId());
    const next = list[index + direction];
    if (index < 0 || !next) return;
    this.selectedId.set(next.id);
    this.loadDetail.next(next.id);
  }

  private showNotice(text: string): void {
    this.notice.set(text);
  }
}
