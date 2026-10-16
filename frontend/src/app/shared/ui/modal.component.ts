import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';

let modalSequence = 0;

@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Backdrop clicks land on the <dialog> itself; Esc/close button provide keyboard access.
  host: { '(click)': 'onDialogClick($event)' },
  template: `
    <dialog
      #dialog
      class="modal"
      [class]="'modal modal--' + size()"
      [attr.aria-labelledby]="titleId"
      (cancel)="onCancel($event)"
    >
      <header class="modal__header">
        <h2 class="modal__title" [id]="titleId">{{ title() }}</h2>
        <button type="button" class="modal__close" aria-label="Đóng" (click)="closed.emit()">
          ×
        </button>
      </header>
      <div class="modal__body"><ng-content /></div>
      <footer class="modal__footer"><ng-content select="[modal-footer]" /></footer>
    </dialog>
  `,
  styleUrl: './modal.component.scss',
})
export class ModalComponent {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly closed = output<void>();

  protected readonly titleId = `app-modal-title-${++modalSequence}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const element = this.dialog().nativeElement;
      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }

  protected onCancel(event: Event): void {
    // Parent owns the open state; keep the dialog open until it flips `open`.
    event.preventDefault();
    this.closed.emit();
  }

  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.closed.emit();
    }
  }
}
