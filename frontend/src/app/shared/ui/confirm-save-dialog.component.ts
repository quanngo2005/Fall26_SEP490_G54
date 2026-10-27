import {
  Component,
  ElementRef,
  HostListener,
  afterNextRender,
  inject,
  input,
  output,
} from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { LayoutIconComponent } from '../layout/layout-icon.component';

/** Confirm-save dialog shared by both SCR-ADM-04 tabs; the diff table is projected. */
@Component({
  selector: 'app-confirm-save-dialog',
  imports: [LayoutIconComponent],
  templateUrl: './confirm-save-dialog.component.html',
  styleUrl: './confirm-save-dialog.component.scss',
})
export class ConfirmSaveDialogComponent extends LocalizedComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly heading = input.required<string>();
  readonly meta = input.required<string>();
  readonly warnTitle = input.required<string>();
  readonly warnBody = input.required<string>();
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly dismissed = output<void>();

  constructor() {
    super();
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.primary')?.focus());
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (!this.busy()) this.dismissed.emit();
  }

  protected dismiss(): void {
    if (!this.busy()) this.dismissed.emit();
  }
}
