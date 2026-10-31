import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, map, startWith, Subject, switchMap } from 'rxjs';
import { BatchWorkResult, CampaignDetail, CampaignStatus } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { CampaignBatchModalComponent } from './campaign-batch-modal.component';
import { statusBadgeClass, worksProgress } from './campaign.utils';

type DetailTab = 'overview' | 'participants';

const DAY_MS = 24 * 60 * 60 * 1000;

@Component({
  selector: 'app-campaign-detail',
  imports: [
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    CampaignBatchModalComponent,
    AppDatePipe,
  ],
  templateUrl: './campaign-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './campaign-detail.component.scss',
  ],
})
export class CampaignDetailComponent extends LocalizedComponent {
  private readonly api = inject(CampaignService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly reload = new Subject<void>();

  protected readonly campaign = signal<CampaignDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly tab = signal<DetailTab>('overview');
  protected readonly batchOpen = signal(false);
  protected readonly notice = signal<string | null>(null);

  protected readonly badgeClass = statusBadgeClass;
  protected readonly progress = worksProgress;

  constructor() {
    super();
    this.route.paramMap
      .pipe(
        map((params) => params.get('id') ?? ''),
        switchMap((id) =>
          this.reload.pipe(
            startWith(undefined),
            switchMap(() => {
              this.loading.set(true);
              this.failed.set(false);
              return this.api.detail(id).pipe(
                catchError(() => {
                  this.failed.set(true);
                  return EMPTY;
                }),
                finalize(() => this.loading.set(false)),
              );
            }),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((campaign) => this.campaign.set(campaign));
  }

  protected statusLabel(status: CampaignStatus): string {
    return this.localization.campaign.status[status] ?? status;
  }

  /** Whole days from today until the campaign ends; negative once it is over. */
  protected daysLeft(campaign: CampaignDetail): number {
    const end = new Date(`${campaign.endDate}T23:59:59`).getTime();
    return Math.ceil((end - Date.now()) / DAY_MS);
  }

  protected back(): void {
    void this.router.navigate(['/campaigns']);
  }

  protected onGenerated(result: BatchWorkResult): void {
    this.batchOpen.set(false);
    this.notice.set(result.message || this.localization.campaign.batch.done);
    this.reload.next();
  }
}
