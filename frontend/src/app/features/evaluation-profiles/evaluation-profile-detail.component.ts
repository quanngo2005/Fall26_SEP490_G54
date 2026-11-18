import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { canApproveEvaluationProfiles } from '../../core/evaluation-profile-permissions';
import {
  EvaluationProfileDetail,
  EvaluationProfileStatus,
} from '../../core/evaluation-profile.models';
import { EvaluationProfileService } from '../../core/evaluation-profile.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { serverMessage } from '../campaigns/campaign.utils';
import { evaluationPeriodLabel } from './evaluation-profile-period';
import { evaluationStatusLabel } from './evaluation-profile-status';
import { ProfileRow, flattenItems } from './evaluation-profile-tree';

type DetailTab = 'criteria' | 'tier' | 'multiplier' | 'quality' | 'history';

interface CriteriaRow {
  group: string;
  kpi: string;
  weight: number;
  /** Weight in the previous version of the same profile; null when there is none. */
  previous: number | null;
}

const MAX_REASON = 500;

/** SCR-CFG-01-D1: evaluation profile detail, with approve / reject for the branch manager. */
@Component({
  selector: 'app-evaluation-profile-detail',
  standalone: true,
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent],
  templateUrl: './evaluation-profile-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    '../work-types/work-type-list.component.scss',
    './evaluation-profile-detail.component.scss',
  ],
})
export class EvaluationProfileDetailComponent extends LocalizedComponent {
  private readonly api = inject(EvaluationProfileService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tabs: readonly DetailTab[] = [
    'criteria',
    'tier',
    'multiplier',
    'quality',
    'history',
  ];
  protected readonly tab = signal<DetailTab>('criteria');
  protected readonly profile = signal<EvaluationProfileDetail | null>(null);
  protected readonly previous = signal<EvaluationProfileDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly acting = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly rejecting = signal(false);
  protected readonly maxReason = MAX_REASON;
  protected reason = '';

  protected readonly canDecide = computed(
    () =>
      this.profile()?.status === 'PENDING_APPROVAL' &&
      canApproveEvaluationProfiles(this.auth.currentUser()?.permissions),
  );

  protected readonly periodText = computed(() => {
    const profile = this.profile();
    return profile ? this.periodOf(profile) : '';
  });

  protected readonly previousPeriodText = computed(() => {
    const previous = this.previous();
    return previous ? this.periodOf(previous) : '';
  });

  protected readonly criteria = computed<CriteriaRow[]>(() => {
    const profile = this.profile();
    if (!profile) return [];
    const before = new Map<string, ProfileRow>(
      flattenItems(this.previous()?.items ?? []).rows.map((row) => [row.kpiDefinitionId, row]),
    );
    return flattenItems(profile.items).rows.map((row) => ({
      group: row.group,
      kpi: row.kpiLabel ?? '',
      weight: row.weight ?? 0,
      previous: this.previous() ? (before.get(row.kpiDefinitionId)?.weight ?? null) : null,
    }));
  });

  protected readonly criteriaTotal = computed(() =>
    this.criteria().reduce((sum, row) => sum + row.weight, 0),
  );

  protected readonly previousTotal = computed(() =>
    this.previous() ? this.criteria().reduce((sum, row) => sum + (row.previous ?? 0), 0) : null,
  );

  constructor() {
    super();
    this.route.paramMap
      .pipe(
        map((params) => params.get('id') ?? ''),
        switchMap((id) => this.fetch(id)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  protected reload(): void {
    this.fetch(this.route.snapshot.paramMap.get('id') ?? '')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  protected statusLabel(status: EvaluationProfileStatus): string {
    return evaluationStatusLabel(this.localization.evaluationProfiles, status);
  }

  protected tabLabel(tab: DetailTab): string {
    return this.localization.evaluationProfileDetail.tabs[tab];
  }

  protected weightText(value: number | null): string {
    return value === null
      ? this.localization.evaluationProfiles.notAvailable
      : `${this.i18n.formatNumber(value)}%`;
  }

  protected approve(): void {
    const profile = this.profile();
    if (!profile || !this.canDecide() || this.acting()) return;
    this.runDecision(this.api.approve(profile.id));
  }

  protected openReject(): void {
    this.reason = '';
    this.actionError.set(null);
    this.rejecting.set(true);
  }

  protected closeReject(): void {
    if (!this.acting()) this.rejecting.set(false);
  }

  protected reasonValid(): boolean {
    const reason = this.reason.trim();
    return reason.length > 0 && reason.length <= MAX_REASON;
  }

  protected reject(): void {
    const profile = this.profile();
    if (!profile || !this.canDecide() || this.acting() || !this.reasonValid()) return;
    this.runDecision(this.api.reject(profile.id, this.reason.trim()));
  }

  @HostListener('document:keydown.control.enter', ['$event'])
  protected onApproveShortcut(event: Event): void {
    if (this.rejecting() || !this.canDecide()) return;
    event.preventDefault();
    this.approve();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeReject();
  }

  private runDecision(request: ReturnType<EvaluationProfileService['approve']>): void {
    this.acting.set(true);
    this.actionError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.acting.set(false);
        this.rejecting.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        this.acting.set(false);
        this.actionError.set(
          serverMessage(error) ?? this.localization.evaluationProfileDetail.actionError,
        );
      },
    });
  }

  private periodOf(profile: EvaluationProfileDetail): string {
    return evaluationPeriodLabel(
      profile.effectiveFrom,
      profile.effectiveTo,
      (value) => this.i18n.formatDate(value, 'DD/MM/YYYY'),
      this.localization.evaluationProfiles.openEnded,
    );
  }

  /** Loads the profile, then the latest earlier version of the same code to compare the weights with. */
  private fetch(id: string) {
    this.loading.set(true);
    this.failed.set(false);
    this.notFound.set(false);
    return this.api.detail(id).pipe(
      switchMap((profile) => {
        this.profile.set(profile);
        this.previous.set(null);
        this.loading.set(false);
        return this.api
          .list({
            keyword: profile.code,
            jobPositionId: profile.jobPositionId,
            page: 1,
            pageSize: 100,
          })
          .pipe(
            map(
              (result) =>
                result.items
                  .filter((item) => item.code === profile.code && item.version < profile.version)
                  .sort((a, b) => b.version - a.version)[0],
            ),
            switchMap((earlier) => (earlier ? this.api.detail(earlier.id) : of(null))),
            catchError(() => of(null)),
            map((earlier) => this.previous.set(earlier)),
          );
      }),
      catchError((error: unknown) => {
        this.profile.set(null);
        this.loading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) this.notFound.set(true);
        else this.failed.set(true);
        return of(null);
      }),
    );
  }
}
