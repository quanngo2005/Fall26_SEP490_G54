import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import {
  CampaignCustomer,
  CONTACT_STATUSES,
  ContactStatus,
  MyCampaign,
} from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { TablePaginationComponent, UiSearchInputComponent } from '../../shared/ui';
import { contactBadgeClass, fillTemplate } from '../campaigns/campaign.utils';
import { UpdateContactModalComponent } from './update-contact-modal.component';

const PAGE_SIZE = 20;

/** Customers that still need action come first; closed ones last. The API does not enforce an order. */
const ORDER: Record<ContactStatus, number> = {
  NOT_CONTACTED: 0,
  CALLED: 1,
  APPOINTMENT_SET: 2,
  MET: 3,
  CLOSED: 4,
};

/** SCR-CMP-03-D1: my customers in one campaign, with the update action. */
@Component({
  selector: 'app-my-campaign-customers',
  imports: [
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    AppDatePipe,
    TablePaginationComponent,
    UiSearchInputComponent,
    UpdateContactModalComponent,
  ],
  templateUrl: './my-campaign-customers.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './my-campaign-customers.component.scss',
  ],
})
export class MyCampaignCustomersComponent extends LocalizedComponent {
  private readonly api = inject(CampaignCustomerService);
  private readonly route = inject(ActivatedRoute);

  protected readonly campaignId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: '' },
  );
  protected readonly statuses = CONTACT_STATUSES;
  protected readonly pageSize = PAGE_SIZE;
  protected readonly badge = contactBadgeClass;

  protected readonly campaign = signal<MyCampaign | null>(null);
  protected readonly customers = signal<CampaignCustomer[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly campaignMissing = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly editing = signal<CampaignCustomer | null>(null);

  protected readonly searchDraft = signal('');
  protected readonly search = signal('');
  protected readonly status = signal<ContactStatus | ''>('');
  protected readonly page = signal(1);

  protected readonly running = computed(() => this.campaign()?.status === 'RUNNING');
  protected readonly sorted = computed(() =>
    [...this.customers()].sort((a, b) => ORDER[a.contactStatus] - ORDER[b.contactStatus]),
  );

  constructor() {
    super();
    effect(() => {
      const id = this.campaignId();
      if (id) {
        this.loadCampaign(id);
      }
    });
    effect(() => {
      const id = this.campaignId();
      const filter = {
        search: this.search(),
        status: this.status(),
        page: this.page(),
        pageSize: PAGE_SIZE,
      };
      if (id) {
        this.loadCustomers(id, filter);
      }
    });
  }

  private requestId = 0;

  protected statusLabel(status: ContactStatus): string {
    return this.localization.campaignCustomers.status[status];
  }

  protected campaignStatusLabel(status: string): string {
    const labels = this.localization.myCampaigns.campaignStatus as Record<string, string>;
    return labels[status] ?? status;
  }

  protected count(status: ContactStatus): number {
    return this.campaign()?.byStatus.find((item) => item.status === status)?.count ?? 0;
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

  protected rangeTemplate(): string {
    return this.localization.myCampaigns.range;
  }

  protected setSearch(value: string): void {
    this.search.set(value.trim());
    this.page.set(1);
  }

  protected onSearchInput(value: string): void {
    this.searchDraft.set(value);
    if (value === '') {
      this.setSearch('');
    }
  }

  protected setStatus(value: string): void {
    this.status.set(value as ContactStatus | '');
    this.page.set(1);
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.notice.set(this.localization.myCampaigns.updateModal.saved);
    this.loadCampaign(this.campaignId());
    this.loadCustomers(this.campaignId(), {
      search: this.search(),
      status: this.status(),
      page: this.page(),
      pageSize: PAGE_SIZE,
    });
  }

  protected reload(): void {
    this.loadCampaign(this.campaignId());
    this.loadCustomers(this.campaignId(), {
      search: this.search(),
      status: this.status(),
      page: this.page(),
      pageSize: PAGE_SIZE,
    });
  }

  private loadCampaign(id: string): void {
    this.api.myCampaigns().subscribe({
      next: (items) => {
        const found = items.find((item) => item.campaignId === id) ?? null;
        this.campaign.set(found);
        this.campaignMissing.set(found === null);
      },
      error: () => this.failed.set(true),
    });
  }

  private loadCustomers(
    id: string,
    filter: Parameters<CampaignCustomerService['myCustomers']>[1],
  ): void {
    const request = ++this.requestId;
    this.loading.set(true);
    this.failed.set(false);
    this.api.myCustomers(id, filter).subscribe({
      next: (page) => {
        if (request !== this.requestId) {
          return;
        }
        this.customers.set(page.items);
        this.totalCount.set(page.totalCount);
        this.loading.set(false);
      },
      error: () => {
        if (request !== this.requestId) {
          return;
        }
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
