import { HttpErrorResponse } from '@angular/common/http';
import { Component, HostListener, inject, input, output, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HandoverDetail, HandoverRecipient } from '../../core/handover.models';
import { HandoverService } from '../../core/handover.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkListItem } from '../../core/work.models';

/** Backend error codes of `POST /works/{id}/handovers` that deserve their own message. */
const ERROR_CODES = {
  pending: 'HANDOVER_ALREADY_PENDING',
  invalidState: 'WORK_INVALID_STATE',
  overLimit: 'HANDOVER_AMOUNT_EXCEEDS_THRESHOLD',
} as const;

/** The assignee asks a colleague to take over one of their works (`POST /works/{id}/handovers`). */
@Component({
  selector: 'app-handover-dialog',
  standalone: true,
  imports: [A11yModule, FormsModule],
  templateUrl: './handover-dialog.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', '../../shared/ui/dialog.scss'],
})
export class HandoverDialogComponent extends LocalizedComponent {
  private readonly api = inject(HandoverService);

  /** Works the caller can hand over (New or In progress, assigned to them). */
  readonly works = input.required<readonly WorkListItem[]>();
  readonly closed = output<void>();
  readonly requested = output<HandoverDetail>();

  protected readonly recipients = signal<HandoverRecipient[]>([]);
  protected readonly recipientsFailed = signal(false);
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  protected workId = '';
  protected toEmployeeId = '';
  protected reason = '';
  protected amount: number | null = 0;

  constructor() {
    super();
    this.api
      .recipients()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (list) => this.recipients.set(list),
        error: () => this.recipientsFailed.set(true),
      });
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected get amountInvalid(): boolean {
    return this.amount === null || Number.isNaN(Number(this.amount)) || Number(this.amount) < 0;
  }

  protected submit(): void {
    this.submitted.set(true);
    const reason = this.reason.trim();
    if (!this.workId || !this.toEmployeeId || !reason || this.amountInvalid) return;

    this.saving.set(true);
    this.errorText.set(null);
    this.api
      .create(this.workId, {
        toEmployeeId: this.toEmployeeId,
        reason,
        amount: Number(this.amount),
      })
      .subscribe({
        next: (handover) => {
          this.saving.set(false);
          this.requested.emit(handover);
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          this.errorText.set(this.messageFor(error));
        },
      });
  }

  private messageFor(error: HttpErrorResponse): string {
    const text = this.localization.work.handover;
    const code: unknown = error.error?.errorCode;
    if (code === ERROR_CODES.pending) return text.pending;
    if (code === ERROR_CODES.invalidState) return text.invalidState;
    if (code === ERROR_CODES.overLimit) return text.overLimit;
    if (error.status === 403) return text.forbidden;
    if (error.status === 400) return text.invalid;
    return text.error;
  }
}
