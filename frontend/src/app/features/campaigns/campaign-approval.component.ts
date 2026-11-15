import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize } from 'rxjs';
import {
  CampaignDecision,
  CampaignDetail,
  CampaignStatus,
  CampaignSummary,
} from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { serverMessage } from './campaign.utils';

const PENDING_PAGE_SIZE = 50;

@Component({
  selector: 'app-campaign-approval',
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent, AppDatePipe],
  templateUrl: './campaign-approval.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './campaign-approval.component.scss',
  ],
})
export class CampaignApprovalComponent extends LocalizedComponent {
  private readonly api = inject(CampaignService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly pending = signal<CampaignSummary[]>([]);
  protected readonly selected = signal<CampaignDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly deciding = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected search = '';
  protected note = '';

  constructor() {
    super();
    this.load(this.route.snapshot.queryParamMap.get('id'));
  }

  protected load(preferredId: string | null = null): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .list({ status: 'PLANNED', search: this.search.trim(), pageSize: PENDING_PAGE_SIZE })
      .pipe(
        catchError(() => {
          this.failed.set(true);
          return EMPTY;
        }),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((overview) => {
        const items = overview.campaigns.items;
        this.pending.set(items);
        const keep = preferredId ?? this.selected()?.id ?? null;
        const next = items.find((item) => item.id === keep) ?? items[0] ?? null;
        if (next) {
          this.select(next.id);
        } else {
          this.selected.set(null);
        }
      });
  }

  protected select(id: string): void {
    if (this.selected()?.id === id && !this.detailFailed()) return;
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.error.set(null);
    this.note = '';
    this.api
      .detail(id)
      .pipe(
        catchError(() => {
          this.selected.set(null);
          this.detailFailed.set(true);
          return EMPTY;
        }),
        finalize(() => this.detailLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((campaign) => this.selected.set(campaign));
  }

  protected canSubmit(decision: CampaignDecision): boolean {
    const campaign = this.selected();
    if (!campaign || !campaign.canApprove || this.deciding()) return false;
    return decision === 'APPROVE' || this.note.trim().length > 0;
  }

  protected decide(decision: CampaignDecision): void {
    const campaign = this.selected();
    if (!campaign || !this.canSubmit(decision)) return;
    this.deciding.set(true);
    this.error.set(null);
    this.api
      .decide(campaign.id, { decision, note: this.note.trim() || null })
      .pipe(
        catchError((err: unknown) => {
          this.error.set(serverMessage(err) ?? this.localization.campaign.genericError);
          return EMPTY;
        }),
        finalize(() => this.deciding.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.notice.set(result.message);
        this.selected.set(null);
        this.note = '';
        this.load();
      });
  }

  protected statusLabel(status: CampaignStatus): string {
    return this.localization.campaign.status[status] ?? status;
  }

  /** Participant count per job position, e.g. "5 SO, 3 Teller"; empty when no position is known. */
  protected positionBreakdown(campaign: CampaignDetail): string {
    const counts = new Map<string, number>();
    for (const { jobPositionTitle } of campaign.participants) {
      if (jobPositionTitle) counts.set(jobPositionTitle, (counts.get(jobPositionTitle) ?? 0) + 1);
    }
    return [...counts].map(([title, count]) => `${count} ${title}`).join(', ');
  }
}
