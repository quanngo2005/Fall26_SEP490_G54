import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CampaignDetail } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { apiErrorCode, fillTemplate, serverMessage } from './campaign.utils';

const REASON_MAX = 1000;

/** SCR-CMP-01-D1-G: confirm completing or cancelling a campaign (docs/api/CAMPAIGN_CONTRACT.md §5.7). */
@Component({
  selector: 'app-campaign-lifecycle-modal',
  imports: [FormsModule],
  templateUrl: './campaign-lifecycle-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
  ],
})
export class CampaignLifecycleModalComponent extends LocalizedComponent {
  private readonly api = inject(CampaignService);

  readonly campaign = input.required<CampaignDetail>();
  readonly mode = input.required<'complete' | 'cancel'>();
  readonly closed = output<void>();
  readonly done = output<'complete' | 'cancel'>();

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly reasonError = signal<string | null>(null);
  protected reason = '';

  protected get text() {
    return this.localization.campaignCustomers.lifecycle;
  }

  protected title(): string {
    const template = this.mode() === 'complete' ? this.text.completeTitle : this.text.cancelTitle;
    return fillTemplate(template, { code: this.campaign().code });
  }

  protected body(): string {
    const open = Math.max(
      0,
      this.campaign().metrics.customerTotal - this.campaign().metrics.customerClosed,
    );
    if (this.mode() === 'complete' && open === 0) {
      return this.text.completeBodyAllClosed;
    }
    const template = this.mode() === 'complete' ? this.text.completeBody : this.text.cancelBody;
    return fillTemplate(template, { customers: open });
  }

  protected submit(): void {
    const mode = this.mode();
    const reason = this.reason.trim();
    this.reasonError.set(null);
    if (mode === 'cancel') {
      if (!reason) {
        this.reasonError.set(this.text.reasonRequired);
        return;
      }
      if (reason.length > REASON_MAX) {
        this.reasonError.set(this.text.reasonMax);
        return;
      }
    }
    this.saving.set(true);
    this.error.set(null);
    const request =
      mode === 'complete'
        ? this.api.complete(this.campaign().id)
        : this.api.cancel(this.campaign().id, reason);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.done.emit(mode);
      },
      error: (error: unknown) => {
        const messages = this.localization.campaignCustomers.errors as Record<string, string>;
        const code = apiErrorCode(error);
        this.error.set((code && messages[code]) || serverMessage(error) || messages['NOT_FOUND']);
        this.saving.set(false);
      },
    });
  }
}
