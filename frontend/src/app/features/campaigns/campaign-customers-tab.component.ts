import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import {
  CampaignCustomer,
  CampaignCustomerList,
  CONTACT_STATUSES,
  ContactStatus,
} from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { CampaignDetail } from '../../core/campaign.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { AppDatePipe } from '../../shared/pipes';
import { TablePaginationComponent, UiSearchInputComponent } from '../../shared/ui';
import { saveFile } from '../performance-dashboard/performance-page';
import { CampaignCustomerAllocateModalComponent } from './campaign-customer-allocate-modal.component';
import { CampaignCustomerImportModalComponent } from './campaign-customer-import-modal.component';
import { apiErrorCode, contactBadgeClass, fillTemplate } from './campaign.utils';

const PAGE_SIZE = 20;
const NOTICE_MS = 4000;
const EDITABLE_STATUSES = ['PLANNED', 'RUNNING'];

/** SCR-CMP-01-D1 tab "Khách hàng": conversion, progress per employee and the customer list. */
@Component({
  selector: 'app-campaign-customers-tab',
  imports: [
    AppDatePipe,
    TablePaginationComponent,
    UiSearchInputComponent,
    CampaignCustomerImportModalComponent,
    CampaignCustomerAllocateModalComponent,
  ],
  templateUrl: './campaign-customers-tab.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './campaign-customers-tab.component.scss',
  ],
})
export class CampaignCustomersTabComponent extends LocalizedComponent {
  private readonly api = inject(CampaignCustomerService);

  readonly campaign = input.required<CampaignDetail>();
  readonly canManage = input(false);
  /** Emitted after a change that alters the campaign's own numbers (import, allocate). */
  readonly changed = output<void>();

  protected readonly statuses = CONTACT_STATUSES;
  protected readonly pageSize = PAGE_SIZE;
  protected readonly data = signal<CampaignCustomerList | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly importOpen = signal(false);
  protected readonly allocateOpen = signal(false);

  protected readonly searchDraft = signal('');
  protected readonly search = signal('');
  protected readonly status = signal<ContactStatus | ''>('');
  protected readonly assigneeId = signal('');
  protected readonly unassignedOnly = signal(false);
  protected readonly page = signal(1);

  private readonly campaignId = computed(() => this.campaign().id);
  protected readonly editable = computed(
    () => this.canManage() && EDITABLE_STATUSES.includes(this.campaign().status),
  );
  protected readonly filtered = computed(
    () =>
      this.search() !== '' ||
      this.status() !== '' ||
      this.assigneeId() !== '' ||
      this.unassignedOnly(),
  );
  protected readonly badge = contactBadgeClass;

  private requestId = 0;

  constructor() {
    super();
    // Loads once the campaign is known and again whenever a filter or the page changes.
    effect(() => {
      const id = this.campaignId();
      const filter = {
        search: this.search(),
        status: this.status(),
        assigneeId: this.assigneeId(),
        unassignedOnly: this.unassignedOnly(),
        page: this.page(),
        pageSize: PAGE_SIZE,
      };
      this.load(id, filter);
    });
  }

  protected statusLabel(status: ContactStatus): string {
    return this.localization.campaignCustomers.status[status] ?? status;
  }

  protected statusCount(status: ContactStatus): number {
    return this.data()?.summary.byStatus.find((item) => item.status === status)?.count ?? 0;
  }

  protected workStatusLabel(status: string | null): string {
    const labels = this.localization.campaignCustomers.workStatus as Record<string, string>;
    return status ? (labels[status] ?? status) : '';
  }

  protected percent(part: number, total: number): number {
    return total > 0 ? Math.round((part * 100) / total) : 0;
  }

  protected rangeTemplate(): string {
    return this.localization.campaignCustomers.range;
  }

  protected reassignLabel(customer: CampaignCustomer): string {
    return fillTemplate(this.localization.campaignCustomers.reassignAria, {
      name: customer.customerName,
    });
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

  protected setAssignee(value: string): void {
    this.assigneeId.set(value);
    this.page.set(1);
  }

  protected toggleUnassigned(checked: boolean): void {
    this.unassignedOnly.set(checked);
    this.page.set(1);
  }

  protected reassign(
    customer: CampaignCustomer,
    employeeId: string,
    select: HTMLSelectElement,
  ): void {
    if (!employeeId || employeeId === customer.assigneeEmployeeId) {
      return;
    }
    this.error.set(null);
    this.api.reassign(this.campaign().id, customer.id, employeeId).subscribe({
      next: () => {
        this.showNotice(this.localization.campaignCustomers.reassignOk);
        this.reload();
      },
      error: (error: unknown) => {
        select.value = customer.assigneeEmployeeId ?? '';
        this.error.set(this.errorText(error));
      },
    });
  }

  protected downloadTemplate(): void {
    this.api.downloadTemplate(this.campaignId()).subscribe({
      next: (blob) => saveFile(blob, 'campaign-customers-template.xlsx'),
      error: (error: unknown) => this.error.set(this.errorText(error)),
    });
  }

  protected onImported(): void {
    this.importOpen.set(false);
    this.afterChange();
  }

  protected onAllocated(): void {
    this.allocateOpen.set(false);
    this.afterChange();
  }

  protected reload(): void {
    this.load(this.campaignId(), {
      search: this.search(),
      status: this.status(),
      assigneeId: this.assigneeId(),
      unassignedOnly: this.unassignedOnly(),
      page: this.page(),
      pageSize: PAGE_SIZE,
    });
  }

  private showNotice(message: string): void {
    this.notice.set(message);
    setTimeout(() => this.notice.set(null), NOTICE_MS);
  }

  private afterChange(): void {
    this.reload();
    this.changed.emit();
  }

  private errorText(error: unknown): string {
    const code = apiErrorCode(error);
    const messages = this.localization.campaignCustomers.errors as Record<string, string>;
    return (code && messages[code]) || this.localization.campaignCustomers.loadFailed;
  }

  private load(campaignId: string, filter: Parameters<CampaignCustomerService['list']>[1]): void {
    const request = ++this.requestId;
    this.loading.set(true);
    this.failed.set(false);
    this.api.list(campaignId, filter).subscribe({
      next: (list) => {
        if (request !== this.requestId) {
          return;
        }
        this.data.set(list);
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
