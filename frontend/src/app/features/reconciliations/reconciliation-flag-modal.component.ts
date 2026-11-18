import { Component, DestroyRef, HostListener, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { QualityFlagSeverity } from '../../core/reconciliation.models';
import { ReconciliationService } from '../../core/reconciliation.service';
import { reconciliationErrorKey } from './reconciliation-display';

const MIN_REASON = 10;
const MAX_REASON = 2000;
const SEVERITIES: readonly QualityFlagSeverity[] = ['LOW', 'MEDIUM', 'HIGH'];

/** Hộp thoại "Gắn cờ chất lượng" của QA: tạo một mục trong hàng chờ QA cho kết quả đã duyệt của công việc. */
@Component({
  selector: 'app-reconciliation-flag-modal',
  imports: [FormsModule],
  template: `
    @let t = localization.reconciliation.flagModal;
<div class="modal-backdrop" role="presentation" (click)="dismiss()">
  <div
    class="modal-dialog flag-modal"
    role="dialog"
    aria-modal="true"
    aria-labelledby="flag-title"
    tabindex="-1"
    (click)="$event.stopPropagation()"
    (keydown)="$event.stopPropagation()"
  >
    <div class="modal-header">
      <h2 id="flag-title">{{t.title}}</h2>
      <button
        type="button"
        class="modal-close"
        [attr.aria-label]="t.close"
        [disabled]="submitting()"
        (click)="dismiss()"
      >
        ×
      </button>
    </div>
    <div class="modal-body">
      <p class="hint">{{t.hint}}</p>
      <div class="field">
        <label for="flag-reason">{{t.reason}} <b>*</b></label>
        <textarea
          id="flag-reason"
          class="textarea"
          rows="4"
          [maxlength]="maxReason"
          [placeholder]="t.reasonPlaceholder"
          [class.invalid]="showValidation() && !reasonValid()"
          [ngModel]="reason()"
          (ngModelChange)="reason.set($event)"
        ></textarea>
        @if (showValidation() && !reasonValid()) {
          <p class="field-error" role="alert">{{t.errors.reason}}</p>
        }
      </div>
      <div class="field">
        <label for="flag-severity">{{t.severity}}</label>
        <select
          id="flag-severity"
          class="select"
          [ngModel]="severity()"
          (ngModelChange)="severity.set($event)"
        >
          @for (level of severities; track level) {
            <option [value]="level">{{severityLabel(level)}}</option>
          }
        </select>
      </div>
      @if (errorKey(); as key) {
        <p class="field-error" role="alert">{{i18n.translate(key)}}</p>
      }
    </div>
    <div class="modal-footer">
      <button
        type="button"
        class="button button-secondary"
        [disabled]="submitting()"
        (click)="dismiss()"
      >
        {{t.cancel}}
      </button>
      <button
        type="button"
        class="button button-primary"
        [disabled]="submitting()"
        (click)="submit()"
      >
        {{submitting() ? t.submitting : t.submit}}
      </button>
    </div>
  </div>
</div>
  `,
  styleUrls: ['../../shared/ui/ui-controls.scss', '../../shared/ui/modal.scss'],
  styles: [
    `
      .hint {
        margin: 0 0 12px;
        color: #475569;
        font-size: 13px;
      }
    `,
  ],
})
export class ReconciliationFlagModalComponent extends LocalizedComponent {
  private readonly api = inject(ReconciliationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly submissionId = input.required<string>();
  readonly flagged = output<void>();
  readonly dismissed = output<void>();

  protected readonly severities = SEVERITIES;
  protected readonly maxReason = MAX_REASON;
  protected readonly reason = signal('');
  protected readonly severity = signal<QualityFlagSeverity>('MEDIUM');
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected reasonValid(): boolean {
    const length = this.reason().trim().length;
    return length >= MIN_REASON && length <= MAX_REASON;
  }

  protected severityLabel(level: QualityFlagSeverity): string {
    return this.localization.reconciliation.flagModal.severities[level];
  }

  @HostListener('document:keydown.escape')
  protected dismiss(): void {
    if (!this.submitting()) this.dismissed.emit();
  }

  protected submit(): void {
    this.showValidation.set(true);
    if (!this.reasonValid() || this.submitting()) return;
    this.errorKey.set(null);
    this.submitting.set(true);
    this.api
      .flagQuality(this.submissionId(), { reason: this.reason().trim(), severity: this.severity() })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.flagged.emit(),
        error: (error: unknown) => {
          const key = reconciliationErrorKey(error);
          this.errorKey.set(
            key === 'reconciliation.errors.generic'
              ? 'reconciliation.flagModal.errors.failed'
              : key,
          );
        },
      });
  }
}
