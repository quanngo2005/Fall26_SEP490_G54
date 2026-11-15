import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  EMPTY,
  finalize,
  of,
  Subject,
  switchMap,
  tap,
} from 'rxjs';
import { AppealSubmitModalComponent } from '../appeals/appeal-submit-modal.component';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  MatchStatus,
  MySubmissionDetail,
  MySubmissionListItem,
} from '../../core/my-submission.models';
import { MySubmissionService } from '../../core/my-submission.service';
import { AppDatePipe } from '../../shared/pipes';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { badgeOf, hoursLeft } from './my-submission-display';
import { MySubmissionDrawerComponent } from './my-submission-drawer.component';

const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-my-submissions',
  imports: [
    FormsModule,
    SidebarComponent,
    HeaderComponent,
    MySubmissionDrawerComponent,
    AppealSubmitModalComponent,
    AppDatePipe,
  ],
  templateUrl: './my-submissions.component.html',
  styleUrls: [
    '../../shared/ui/modal.scss',
    '../appeals/appeals-shared.scss',
    '../appeals/appeals-list.scss',
    './my-submissions.component.scss',
  ],
})
export class MySubmissionsComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(MySubmissionService);
  private readonly evaluations = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private readonly loadDetail = new Subject<string>();
  private readonly keywordInput = new Subject<string>();

  protected readonly keyword = signal('');
  protected readonly matchFilter = signal<MatchStatus | 'ALL'>('ALL');
  protected readonly periodFilter = signal('ALL');
  protected readonly page = signal(1);

  protected readonly items = signal<MySubmissionListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(0);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly detail = signal<MySubmissionDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly appealFor = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly selectedItem = computed(
    () => this.items().find((item) => item.submissionId === this.selectedId()) ?? null,
  );

  protected readonly pages = computed(() => {
    const size = this.pageSize();
    return size > 0
      ? Array.from({ length: Math.ceil(this.totalCount() / size) }, (_, i) => i + 1)
      : [];
  });

  protected readonly summary = computed(() => {
    const period = this.periods().find((p) => p.id === this.periodFilter());
    const count = this.totalCount();
    return period
      ? this.i18n.translate('submissions.periodSummary', { period: period.code, count })
      : this.i18n.translate('submissions.periodSummaryAll', { count });
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const match = this.matchFilter();
          const period = this.periodFilter();
          return this.api
            .list({
              ...(match !== 'ALL' ? { matchStatus: match } : {}),
              ...(period !== 'ALL' ? { periodId: period } : {}),
              keyword: this.keyword().trim(),
              page: this.page(),
            })
            .pipe(
              tap((list) => {
                this.items.set(list.items);
                this.totalCount.set(list.totalCount);
                this.pageSize.set(list.pageSize);
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

    this.keywordInput
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((text) => {
        this.keyword.set(text);
        this.page.set(1);
        this.reload.next();
      });
  }

  ngOnInit(): void {
    // The period filter is supplementary: the list must still work when the periods cannot be loaded.
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((periods) => {
        this.periods.set(periods);
        const active = periods.find((p) => p.isActive);
        if (active) this.periodFilter.set(active.id);
        this.reload.next();
      });
  }

  protected onKeyword(text: string): void {
    this.keywordInput.next(text);
  }

  protected setMatch(value: MatchStatus | 'ALL'): void {
    this.matchFilter.set(value);
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

  protected retry(): void {
    this.reload.next();
  }

  protected select(item: MySubmissionListItem): void {
    if (this.selectedId() === item.submissionId) {
      this.closeDrawer();
      return;
    }
    this.selectedId.set(item.submissionId);
    this.loadDetail.next(item.submissionId);
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

  /** Shows an earlier version of the selected work; the list keeps pointing at the latest one. */
  protected viewVersion(submissionId: string): void {
    this.loadDetail.next(submissionId);
  }

  protected openAppeal(submissionId: string): void {
    this.appealFor.set(submissionId);
  }

  protected onAppealed(): void {
    const id = this.selectedId();
    this.appealFor.set(null);
    this.notice.set(this.localization.submissions.appealSent);
    this.reload.next();
    if (id) this.loadDetail.next(id);
  }

  protected badge(item: MySubmissionListItem) {
    const { key, tone } = badgeOf(item.status, item.matchStatus);
    return { tone, label: this.localization.submissions.badge[key] };
  }

  protected transactionText(item: MySubmissionListItem): string {
    const text = this.localization.submissions.transactionCell;
    if (!item.firstTransactionCode) return item.workTypeName === null ? text.noType : text.noCode;
    return item.transactionCount > 1
      ? this.i18n.translate('submissions.transactionCell.more', {
          code: item.firstTransactionCode,
          count: item.transactionCount - 1,
        })
      : item.firstTransactionCode;
  }

  protected reconcileText(item: MySubmissionListItem): string {
    const text = this.localization.submissions.reconcile;
    if (item.appealDeadline) {
      return this.i18n.translate('submissions.reconcile.appealLeft', {
        hours: hoursLeft(item.appealDeadline),
      });
    }
    if (item.status === 'WITHDRAWN' || item.status === 'REJECTED') return text.none;
    switch (item.matchStatus) {
      case 'MATCHED':
        return text.kpiCounted;
      case 'IN_REVIEW':
        return text.waitingData;
      case 'NOT_MATCHED':
        return this.i18n.translate('submissions.reconcile.partial', {
          matched: item.matchedTransactionCount,
          total: item.transactionCount,
        });
      default:
        return item.status === 'APPROVED' ? text.none : text.waitingReviewer;
    }
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('submissions.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
      size: this.pageSize(),
    });
  }

  private step(direction: 1 | -1): void {
    const list = this.items();
    const index = list.findIndex((item) => item.submissionId === this.selectedId());
    const next = list[index + direction];
    if (index < 0 || !next) return;
    this.selectedId.set(next.submissionId);
    this.loadDetail.next(next.submissionId);
  }
}
