import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CONTACT_STATUSES, ContactStatus, MyCampaign } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { contactBadgeClass, fillTemplate } from '../campaigns/campaign.utils';

type ListTab = 'running' | 'ended';

const ENDED = ['COMPLETED', 'CANCELLED'];

/** SCR-CMP-03: the campaigns where customers are assigned to me, with my own progress. */
@Component({
  selector: 'app-my-campaigns',
  imports: [RouterLink, HeaderComponent, SidebarComponent, AppDatePipe],
  templateUrl: './my-campaigns.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './my-campaigns.component.scss',
  ],
})
export class MyCampaignsComponent extends LocalizedComponent {
  private readonly api = inject(CampaignCustomerService);

  protected readonly statuses = CONTACT_STATUSES;
  protected readonly badge = contactBadgeClass;
  protected readonly campaigns = signal<MyCampaign[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly tab = signal<ListTab>('running');

  protected readonly running = computed(() =>
    this.campaigns().filter((item) => !ENDED.includes(item.status)),
  );
  protected readonly ended = computed(() =>
    this.campaigns().filter((item) => ENDED.includes(item.status)),
  );
  protected readonly visible = computed(() =>
    this.tab() === 'running' ? this.running() : this.ended(),
  );

  constructor() {
    super();
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.myCampaigns().subscribe({
      next: (items) => {
        this.campaigns.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected statusLabel(status: string): string {
    const labels = this.localization.myCampaigns.campaignStatus as Record<string, string>;
    return labels[status] ?? status;
  }

  protected contactLabel(status: ContactStatus): string {
    return this.localization.campaignCustomers.status[status];
  }

  protected count(campaign: MyCampaign, status: ContactStatus): number {
    return campaign.byStatus.find((item) => item.status === status)?.count ?? 0;
  }

  protected progress(campaign: MyCampaign): string {
    const percent =
      campaign.totalAssigned > 0 ? Math.round((campaign.closed * 100) / campaign.totalAssigned) : 0;
    return fillTemplate(this.localization.myCampaigns.closedOf, {
      closed: campaign.closed,
      total: campaign.totalAssigned,
      percent,
    });
  }

  protected warning(campaign: MyCampaign): string {
    return fillTemplate(this.localization.myCampaigns.notContactedWarning, {
      count: this.count(campaign, 'NOT_CONTACTED'),
    });
  }
}
