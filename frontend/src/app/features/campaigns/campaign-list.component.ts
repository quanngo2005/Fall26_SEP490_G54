import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import {
  CampaignListOverview,
  CampaignObjective,
  CampaignOverviewMetrics,
  CampaignStatus,
  CampaignSummary,
} from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { CampaignCreateModalComponent } from './campaign-create-modal.component';
import { statusBadgeClass, worksProgress } from './campaign.utils';

type StatusTab = CampaignStatus | '';

interface TabDefinition {
  status: StatusTab;
  count: keyof CampaignOverviewMetrics;
}

const PAGE_SIZE = 10;

@Component({
  selector: 'app-campaign-list',
  imports: [
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    CampaignCreateModalComponent,
    AppDatePipe,
  ],
  templateUrl: './campaign-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './campaign-list.component.scss',
  ],
})
export class CampaignListComponent extends LocalizedComponent {
  private readonly api = inject(CampaignService);
  private readonly router = inject(Router);
  private readonly reload = new Subject<void>();

  protected readonly tabs: readonly TabDefinition[] = [
    { status: '', count: 'totalCampaigns' },
    { status: 'PLANNED', count: 'plannedCampaigns' },
    { status: 'RUNNING', count: 'runningCampaigns' },
    { status: 'COMPLETED', count: 'completedCampaigns' },
  ];
  protected readonly overview = signal<CampaignListOverview | null>(null);
  protected readonly objectives = signal<CampaignObjective[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly createOpen = signal(false);

  protected status: StatusTab = '';
  protected search = '';
  protected objectiveId = '';
  protected page = 1;

  protected readonly badgeClass = statusBadgeClass;
  protected readonly progress = worksProgress;

  constructor() {
    super();
    const destroyRef = inject(DestroyRef);
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api
            .list({
              search: this.search.trim(),
              status: this.status,
              objectiveId: this.objectiveId,
              page: this.page,
              pageSize: PAGE_SIZE,
            })
            .pipe(
              catchError(() => {
                this.failed.set(true);
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
        }),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((overview) => this.overview.set(overview));
    this.api
      .objectives()
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((objectives) => this.objectives.set(objectives));
    this.refresh();
  }

  protected refresh(resetPage = false): void {
    if (resetPage) this.page = 1;
    this.reload.next();
  }

  protected selectTab(status: StatusTab): void {
    this.status = status;
    this.refresh(true);
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.refresh();
  }

  protected tabLabel(status: StatusTab): string {
    const text = this.localization.campaign;
    switch (status) {
      case 'PLANNED':
        return text.tabPlanned;
      case 'RUNNING':
        return text.tabRunning;
      case 'COMPLETED':
        return text.tabCompleted;
      default:
        return text.tabAll;
    }
  }

  protected statusLabel(status: CampaignStatus): string {
    return this.localization.campaign.status[status] ?? status;
  }

  protected totalPages(): number {
    const total = this.overview()?.campaigns.totalCount ?? 0;
    return Math.max(1, Math.ceil(total / PAGE_SIZE));
  }

  protected rangeText(): string {
    const total = this.overview()?.campaigns.totalCount ?? 0;
    const from = total === 0 ? 0 : (this.page - 1) * PAGE_SIZE + 1;
    const to = Math.min(this.page * PAGE_SIZE, total);
    return `${from}–${to} / ${total} ${this.localization.campaign.campaigns}`;
  }

  protected open(campaign: CampaignSummary): void {
    void this.router.navigate(['/campaigns', campaign.id]);
  }

  protected onCreated(id: string): void {
    this.createOpen.set(false);
    void this.router.navigate(['/campaigns', id]);
  }
}
