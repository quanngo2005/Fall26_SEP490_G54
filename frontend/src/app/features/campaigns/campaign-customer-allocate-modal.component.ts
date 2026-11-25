import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import {
  AllocateCustomersResult,
  CustomerAssigneeSummary,
} from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { ALLOCATION_RULES, AllocationRule, CampaignDetail } from '../../core/campaign.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { apiErrorCode, fillTemplate, serverMessage } from './campaign.utils';

/** Maximum customers the API allocates in one request (docs/api/CAMPAIGN_CONTRACT.md §5.6). */
const MAX_PER_REQUEST = 2000;

/** SCR-CMP-01-M4: allocate the unassigned customers across the campaign's participants. */
@Component({
  selector: 'app-campaign-customer-allocate-modal',
  templateUrl: './campaign-customer-allocate-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './campaign-customer-allocate-modal.component.scss',
  ],
})
export class CampaignCustomerAllocateModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(CampaignCustomerService);

  readonly campaign = input.required<CampaignDetail>();
  readonly unassigned = input.required<number>();
  /** Customers each participant already holds, from the list summary. */
  readonly holding = input<CustomerAssigneeSummary[]>([]);
  readonly closed = output<void>();
  readonly allocated = output<void>();

  protected readonly rules = ALLOCATION_RULES;
  protected readonly rule = signal<AllocationRule>('ROUND_ROBIN');
  protected readonly selectedIds = signal<string[]>([]);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<AllocateCustomersResult | null>(null);
  protected readonly maxPerRequest = MAX_PER_REQUEST;

  protected readonly overLimit = computed(() => this.unassigned() > MAX_PER_REQUEST);
  protected readonly canSubmit = computed(
    () =>
      this.unassigned() > 0 && !this.overLimit() && this.selectedIds().length > 0 && !this.saving(),
  );

  ngOnInit(): void {
    this.selectedIds.set(this.campaign().participants.map((p) => p.employeeId));
  }

  protected get text() {
    return this.localization.campaignCustomers.allocateModal;
  }

  protected ruleLabel(rule: AllocationRule): string {
    return this.text.rules[rule];
  }

  protected holdingLabel(employeeId: string): string {
    const count = this.holding().find((item) => item.employeeId === employeeId)?.assigned ?? 0;
    return fillTemplate(this.text.holding, { count });
  }

  protected toggle(employeeId: string, checked: boolean): void {
    this.selectedIds.update((ids) =>
      checked ? [...ids, employeeId] : ids.filter((id) => id !== employeeId),
    );
  }

  protected scopeText(): string {
    return fillTemplate(this.text.scope, { max: MAX_PER_REQUEST });
  }

  protected overLimitText(): string {
    return fillTemplate(this.text.overLimit, { count: this.unassigned(), max: MAX_PER_REQUEST });
  }

  protected submitLabel(): string {
    return this.saving()
      ? this.text.submitting
      : fillTemplate(this.text.submit, { count: this.unassigned() });
  }

  protected resultTitle(result: AllocateCustomersResult): string {
    return fillTemplate(this.text.resultTitle, { count: result.assignedCount });
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api
      .allocate(this.campaign().id, { rule: this.rule(), employeeIds: this.selectedIds() })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.saving.set(false);
        },
        error: (error: unknown) => {
          const messages = this.localization.campaignCustomers.errors as Record<string, string>;
          const code = apiErrorCode(error);
          this.error.set((code && messages[code]) || serverMessage(error) || this.text.failed);
          this.saving.set(false);
        },
      });
  }

  protected close(): void {
    if (this.result()) {
      this.allocated.emit();
    } else {
      this.closed.emit();
    }
  }
}
