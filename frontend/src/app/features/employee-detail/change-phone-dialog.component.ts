import {
  afterNextRender,
  Component,
  HostListener,
  inject,
  Injector,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { PHONE_PATTERN } from '../../core/profile.models';
import { ProfileService } from '../../core/profile.service';

/** Popup to change the signed-in user's own phone number (exactly 10 digits, starting with 0). */
@Component({
  selector: 'app-change-phone-dialog',
  imports: [FormsModule],
  templateUrl: './change-phone-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './profile-dialog.scss',
  ],
})
export class ChangePhoneDialogComponent extends LocalizedComponent implements OnInit {
  private readonly profiles = inject(ProfileService);
  private readonly injector = inject(Injector);
  /** The element that opened the popup; focus goes back to it when the popup closes. */
  private readonly opener = document.activeElement as HTMLElement | null;
  private dismissed = false;

  /** The phone number currently stored, used to prefill the field. */
  readonly current = input<string | null>(null);
  /** Emitted after the new number was saved; the parent closes the popup. */
  readonly saved = output<void>();
  /** Emitted when the user dismissed the popup without saving. */
  readonly closed = output<void>();

  protected phone = '';
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  ngOnInit(): void {
    this.phone = this.current() ?? '';
    afterNextRender(() => document.getElementById('profile-phone')?.focus(), {
      injector: this.injector,
    });
  }

  protected get valid(): boolean {
    const value = this.phone.trim();
    return value === '' || PHONE_PATTERN.test(value);
  }

  protected get changed(): boolean {
    return this.phone.trim() !== (this.current() ?? '');
  }

  /** User-initiated close (Cancel, ×, backdrop, Escape): ignored while the request is in flight. */
  @HostListener('document:keydown.escape')
  protected close(): void {
    if (this.saving() || this.dismissed) return;
    this.dismissed = true;
    this.closed.emit();
    this.opener?.focus();
  }

  protected save(): void {
    if (!this.valid || !this.changed || this.saving()) return;

    this.saving.set(true);
    this.errorText.set(null);
    this.profiles
      .updatePhone(this.phone.trim() === '' ? null : this.phone.trim())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.saved.emit();
          this.opener?.focus();
        },
        error: () => this.errorText.set(this.localization.profile.phoneSaveError),
      });
  }
}
