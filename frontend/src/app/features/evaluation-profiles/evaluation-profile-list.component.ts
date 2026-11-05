import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import {
  EvaluationProfileListItem,
  EvaluationProfileStatus,
} from '../../core/evaluation-profile.models';
import { EvaluationProfileService } from '../../core/evaluation-profile.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

/** SCR-CFG-01: evaluation profile list for the system admin, branch manager and department manager. */
@Component({
  selector: 'app-evaluation-profile-list',
  standalone: true,
  imports: [FormsModule, HeaderComponent, SidebarComponent],
  templateUrl: './evaluation-profile-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../work-types/work-type-list.component.scss',
    './evaluation-profile-list.component.scss',
  ],
})
export class EvaluationProfileListComponent extends LocalizedComponent {
  private readonly api = inject(EvaluationProfileService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly allProfiles = signal<EvaluationProfileListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly filterKeyword = signal('');
  protected readonly filterPosition = signal('');
  protected readonly filterPeriod = signal('');
  protected readonly filterStatus = signal<EvaluationProfileStatus | ''>('');
  protected readonly currentPage = signal(1);
  protected readonly currentPageSize = signal(20);

  protected readonly pageSizes = [10, 20, 50];
  protected keyword = '';
  protected position = '';
  protected period = '';
  protected status: EvaluationProfileStatus | '' = '';
  protected pageSize = 20;

  protected readonly statuses: readonly EvaluationProfileStatus[] = [
    'DRAFT',
    'PENDING_APPROVAL',
    'REJECTED',
    'ACTIVE',
    'INACTIVE',
    'ARCHIVED',
  ];

  protected readonly positionOptions = computed(() => {
    const byId = new Map<string, string>();
    for (const item of this.allProfiles()) byId.set(item.jobPositionId, item.jobPositionTitle);
    return [...byId].map(([id, title]) => ({ id, title }));
  });

  protected readonly periodOptions = computed(() => [
    ...new Set(this.allProfiles().map((item) => this.periodLabel(item))),
  ]);

  protected readonly pendingProfiles = computed(() =>
    this.allProfiles().filter((item) => item.status === 'PENDING_APPROVAL'),
  );

  protected readonly subtitleText = computed(() => {
    const positions = this.positionOptions();
    return this.i18n.translate('evaluationProfiles.subtitleCounts', {
      count: positions.length,
      names: positions.map((p) => p.title).join(', '),
    });
  });

  protected readonly pendingBannerText = computed(() => {
    const first = this.pendingProfiles()[0];
    return first
      ? this.i18n.translate('evaluationProfiles.bannerTitle', {
          profile: first.name,
          period: this.periodLabel(first),
        })
      : '';
  });

  protected readonly filteredProfiles = computed(() => {
    const keyword = this.filterKeyword().toLowerCase();
    const position = this.filterPosition();
    const period = this.filterPeriod();
    const status = this.filterStatus();
    return this.allProfiles().filter((item) => {
      if (position && item.jobPositionId !== position) return false;
      if (period && this.periodLabel(item) !== period) return false;
      if (status && item.status !== status) return false;
      if (
        keyword &&
        !item.name.toLowerCase().includes(keyword) &&
        !item.code.toLowerCase().includes(keyword)
      )
        return false;
      return true;
    });
  });

  protected readonly totalCount = computed(() => this.filteredProfiles().length);
  protected readonly profiles = computed(() => {
    const start = (this.currentPage() - 1) * this.currentPageSize();
    return this.filteredProfiles().slice(start, start + this.currentPageSize());
  });
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.currentPageSize())),
  );
  protected readonly rangeText = computed(() => {
    const total = this.totalCount();
    const first = total === 0 ? 0 : (this.currentPage() - 1) * this.currentPageSize() + 1;
    const last = Math.min(total, first + this.profiles().length - 1);
    return this.i18n.translate('evaluationProfiles.range', { first, last, total });
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api.list({ page: 1, pageSize: 100 }).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.allProfiles.set(result.items));
    this.reload.next();
  }

  protected load(): void {
    this.reload.next();
  }

  protected applyFilter(): void {
    this.currentPage.set(1);
    this.currentPageSize.set(this.pageSize);
    this.filterKeyword.set(this.keyword.trim());
    this.filterPosition.set(this.position);
    this.filterPeriod.set(this.period);
    this.filterStatus.set(this.status);
  }

  protected clearFilter(): void {
    this.keyword = '';
    this.position = '';
    this.period = '';
    this.status = '';
    this.applyFilter();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) return;
    this.currentPage.set(page);
  }

  /** Quarter label (Q4/2026) when the range is a whole calendar quarter, otherwise the date range. */
  protected periodLabel(item: EvaluationProfileListItem): string {
    const from = this.parseDate(item.effectiveFrom);
    const to = item.effectiveTo ? this.parseDate(item.effectiveTo) : null;
    if (to && from.getUTCDate() === 1 && from.getUTCMonth() % 3 === 0) {
      const quarterEnd = new Date(Date.UTC(from.getUTCFullYear(), from.getUTCMonth() + 3, 0));
      if (quarterEnd.getTime() === to.getTime())
        return `Q${from.getUTCMonth() / 3 + 1}/${from.getUTCFullYear()}`;
    }
    const start = this.i18n.formatDate(item.effectiveFrom, 'DD/MM/YYYY');
    return to
      ? `${start} – ${this.i18n.formatDate(item.effectiveTo as string, 'DD/MM/YYYY')}`
      : `${start} – ${this.localization.evaluationProfiles.openEnded}`;
  }

  /** A profile whose effective period has ended, or that is archived, is read-only. */
  protected isLocked(item: EvaluationProfileListItem): boolean {
    if (item.status === 'ARCHIVED') return true;
    if (!item.effectiveTo) return false;
    return this.parseDate(item.effectiveTo).getTime() < this.startOfTodayUtc();
  }

  protected statusLabel(status: EvaluationProfileStatus): string {
    const t = this.localization.evaluationProfiles;
    const labels: Record<EvaluationProfileStatus, string> = {
      DRAFT: t.statusDraft,
      PENDING_APPROVAL: t.statusPending,
      REJECTED: t.statusRejected,
      ACTIVE: t.statusActive,
      INACTIVE: t.statusInactive,
      ARCHIVED: t.statusArchived,
    };
    return labels[status] ?? status;
  }

  protected weightText(value: number): string {
    return `${this.i18n.formatNumber(value)}%`;
  }

  private parseDate(value: string): Date {
    const [y, m, d] = value.slice(0, 10).split('-').map(Number);
    return new Date(Date.UTC(y, m - 1, d));
  }

  private startOfTodayUtc(): number {
    const now = new Date();
    return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  }
}
