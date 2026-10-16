import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ModalComponent } from './modal.component';

@Component({
  selector: 'app-confirm-dialog',
  imports: [ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [open]="open()" [title]="title()" size="sm" (closed)="onCancel()">
      <p class="message">{{ message() }}</p>
      <ng-container modal-footer>
        <button type="button" class="btn" [disabled]="busy()" (click)="onCancel()">
          {{ cancelLabel() }}
        </button>
        <button
          type="button"
          class="btn btn--confirm"
          [class.btn--danger]="tone() === 'danger'"
          [disabled]="busy()"
          [attr.aria-busy]="busy()"
          (click)="confirmed.emit()"
        >
          {{ busy() ? 'Đang xử lý…' : confirmLabel() }}
        </button>
      </ng-container>
    </app-modal>
  `,
  styleUrl: './confirm-dialog.component.scss',
})
export class ConfirmDialogComponent {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input('Xác nhận');
  readonly cancelLabel = input('Hủy');
  readonly tone = input<'default' | 'danger'>('default');
  readonly busy = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  protected onCancel(): void {
    // A running operation must not be abandoned by Esc/backdrop clicks.
    if (!this.busy()) {
      this.cancelled.emit();
    }
  }
}
