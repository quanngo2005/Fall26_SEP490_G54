import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  CampaignCustomer,
  CONTACT_STATUSES,
  ContactStatus,
  UpdateContactPayload,
} from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  apiErrorCode,
  contactBadgeClass,
  fillTemplate,
  serverMessage,
} from '../campaigns/campaign.utils';

const MAX_CODES = 10;
const NOTE_MAX = 1000;

type Field = 'answered' | 'when' | 'codes' | 'note';

/** SCR-CMP-03-D1-M1: the employee records the latest contact status of one of their customers. */
@Component({
  selector: 'app-update-contact-modal',
  imports: [FormsModule],
  templateUrl: './update-contact-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './update-contact-modal.component.scss',
  ],
})
export class UpdateContactModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(CampaignCustomerService);

  readonly campaignId = input.required<string>();
  readonly customer = input.required<CampaignCustomer>();
  readonly closed = output<void>();
  readonly saved = output<CampaignCustomer>();

  protected readonly statuses = CONTACT_STATUSES;
  protected readonly badge = contactBadgeClass;
  protected readonly status = signal<ContactStatus>('CALLED');
  protected readonly answered = signal<boolean | null>(null);
  protected readonly saving = signal(false);
  protected readonly confirming = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly submitted = signal(false);
  protected readonly codesText = signal('');

  protected whenValue = '';
  protected closeNote = '';

  protected readonly codes = computed(() => this.parseCodes(this.codesText()));

  ngOnInit(): void {
    const current = this.customer();
    this.status.set(current.contactStatus);
    if (current.contactStatus === 'APPOINTMENT_SET') {
      this.whenValue = this.toLocalInput(current.appointmentAt);
    } else if (current.contactStatus === 'MET') {
      this.whenValue = this.toLocalInput(current.metAt);
    }
  }

  protected get text() {
    return this.localization.myCampaigns.updateModal;
  }

  protected statusLabel(status: ContactStatus): string {
    return this.localization.campaignCustomers.status[status];
  }

  protected missedText(): string {
    return fillTemplate(this.text.missedCount, { count: this.customer().missedCallCount });
  }

  protected confirmBody(): string {
    return fillTemplate(this.text.confirmBody, { count: this.codes().length });
  }

  protected setStatus(value: string): void {
    this.status.set(value as ContactStatus);
    this.submitted.set(false);
    this.serverError.set(null);
    this.whenValue = '';
  }

  protected needsWhen(): boolean {
    return this.status() === 'APPOINTMENT_SET' || this.status() === 'MET';
  }

  /** The message to show under a field that is missing or invalid, or null. */
  protected fieldError(field: Field): string | null {
    if (!this.submitted()) {
      return null;
    }
    const text = this.text;
    switch (field) {
      case 'answered':
        return this.status() === 'CALLED' && this.answered() === null ? text.required : null;
      case 'when':
        return this.needsWhen() && !this.whenValue ? text.required : null;
      case 'codes':
        if (this.status() !== 'CLOSED') {
          return null;
        }
        if (this.codes().length === 0) {
          return text.codesRequired;
        }
        return this.codes().length > MAX_CODES ? text.codesTooMany : null;
      default:
        return this.status() === 'CLOSED' && this.closeNote.length > NOTE_MAX ? text.noteMax : null;
    }
  }

  protected save(): void {
    this.submitted.set(true);
    this.serverError.set(null);
    const fields: Field[] = ['answered', 'when', 'codes', 'note'];
    if (fields.some((field) => this.fieldError(field))) {
      return;
    }
    if (this.status() === 'CLOSED' && !this.confirming()) {
      this.confirming.set(true);
      return;
    }
    this.send();
  }

  protected backFromConfirm(): void {
    this.confirming.set(false);
  }

  private send(): void {
    const status = this.status();
    const when = this.whenValue ? new Date(this.whenValue).toISOString() : null;
    const payload: UpdateContactPayload = {
      status,
      callAnswered: status === 'CALLED' ? this.answered() : null,
      appointmentAt: status === 'APPOINTMENT_SET' ? when : null,
      metAt: status === 'MET' ? when : null,
      transactionCodes: status === 'CLOSED' ? this.codes() : null,
      note: status === 'CLOSED' && this.closeNote.trim() ? this.closeNote.trim() : null,
    };
    this.saving.set(true);
    this.api.updateContact(this.campaignId(), this.customer().id, payload).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.saved.emit(updated);
      },
      error: (error: unknown) => {
        const messages = this.localization.myCampaigns.errors as Record<string, string>;
        const code = apiErrorCode(error);
        this.saving.set(false);
        this.confirming.set(false);
        this.serverError.set((code && messages[code]) || serverMessage(error) || this.text.failed);
      },
    });
  }

  private parseCodes(value: string): string[] {
    return [...new Set(value.split(/[\s,;]+/).filter((code) => code.length > 0))];
  }

  /** `datetime-local` wants local time without a zone, `YYYY-MM-DDTHH:mm`. */
  private toLocalInput(iso: string | null): string {
    if (!iso) {
      return '';
    }
    const date = new Date(iso);
    const pad = (n: number) => String(n).padStart(2, '0');
    const day = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    return `${day}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
}
