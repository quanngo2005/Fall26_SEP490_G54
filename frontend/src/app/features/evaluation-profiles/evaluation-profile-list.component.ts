import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { canManageEvaluationProfiles } from '../../core/evaluation-profile-permissions';
import {
  EvaluationProfileListItem,
  EvaluationProfileStatus,
} from '../../core/evaluation-profile.models';
import { EvaluationProfileService } from '../../core/evaluation-profile.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { EvaluationProfileFormDialogComponent } from './evaluation-profile-form-dialog.component';
import { evaluationPeriodLabel, parseDate } from './evaluation-profile-period';
import { evaluationStatusLabel } from './evaluation-profile-status';

/** SCR-CFG-01: evaluation profile list for the system admin, branch manager and department manager. */
@Component({
  selector: 'app-evaluation-profile-list',
  standalone: true,
  imports: [FormsModule, HeaderComponent, SidebarComponent, EvaluationProfileFormDialogComponent],
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
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly canManage = computed(() =>
    canManageEvaluationProfiles(this.auth.currentUser()?.permissions),
  );
  protected readonly formOpen = signal(false);
  protected readonly formProfileId = signal<string | null>(null);

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

  protected periodLabel(item: EvaluationProfileListItem): string {
    return evaluationPeriodLabel(
      item.effectiveFrom,
      item.effectiveTo,
      (value) => this.i18n.formatDate(value, 'DD/MM/YYYY'),
      this.localization.evaluationProfiles.openEnded,
    );
  }

  /** SCR-CFG-01-D1: the whole row opens the profile detail. */
  protected openDetail(item: EvaluationProfileListItem): void {
    void this.router.navigate(['/evaluation-profiles', item.id]);
  }

  /** The row button views an approved or locked profile and edits any other. */
  protected openRowAction(item: EvaluationProfileListItem): void {
    if (this.isViewOnly(item)) this.openDetail(item);
    else this.openForm(item.id);
  }

  /** SCR-CFG-01-M1: `null` creates a profile, an id edits that profile. */
  protected openForm(profileId: string | null): void {
    this.formProfileId.set(profileId);
    this.formOpen.set(true);
  }

  protected closeForm(): void {
    this.formOpen.set(false);
  }

  protected onSaved(): void {
    this.formOpen.set(false);
    this.load();
  }

  /** A profile whose effective period has ended, or that is archived, is read-only. */
  protected isLocked(item: EvaluationProfileListItem): boolean {
    if (item.status === 'ARCHIVED') return true;
    if (!item.effectiveTo) return false;
    return parseDate(item.effectiveTo).getTime() < this.startOfTodayUtc();
  }

  /** An approved profile (ACTIVE, or INACTIVE once replaced) and any locked profile can only be viewed. */
  protected isViewOnly(item: EvaluationProfileListItem): boolean {
    return item.status === 'ACTIVE' || item.status === 'INACTIVE' || this.isLocked(item);
  }

  protected statusLabel(status: EvaluationProfileStatus): string {
    return evaluationStatusLabel(this.localization.evaluationProfiles, status);
  }

  protected weightText(value: number): string {
    return `${this.i18n.formatNumber(value)}%`;
  }

  private startOfTodayUtc(): number {
    const now = new Date();
    return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  }
}
