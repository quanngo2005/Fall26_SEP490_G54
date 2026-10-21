import { HttpErrorResponse } from '@angular/common/http';
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
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { INVALID_CURRENT_PASSWORD } from '../../core/profile.models';

const MIN_PASSWORD_LENGTH = 12;
const MAX_PASSWORD_LENGTH = 128;
/** Shorter local parts would reject far too many otherwise good passwords (same threshold as the backend). */
const MIN_EMAIL_NAME_LENGTH = 3;

/** Popup to change the signed-in user's own password. Every other session is signed out by the backend. */
@Component({
  selector: 'app-change-password-dialog',
  imports: [FormsModule],
  templateUrl: './change-password-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './profile-dialog.scss',
  ],
})
export class ChangePasswordDialogComponent extends LocalizedComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  /** The element that opened the popup; focus goes back to it when the popup closes. */
  private readonly opener = document.activeElement as HTMLElement | null;
  private dismissed = false;

  /** The user's email; a new password may not contain its name part. */
  readonly email = input('');
  /** Emitted after the password was changed; the parent closes the popup. */
  readonly changed = output<void>();
  /** Emitted when the user dismissed the popup without changing anything. */
  readonly closed = output<void>();

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmPassword = '';
  protected readonly showPasswords = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  ngOnInit(): void {
    afterNextRender(() => document.getElementById('current-password')?.focus(), {
      injector: this.injector,
    });
  }

  protected get ruleLength(): boolean {
    return (
      this.newPassword.length >= MIN_PASSWORD_LENGTH &&
      this.newPassword.length <= MAX_PASSWORD_LENGTH
    );
  }

  protected get ruleMix(): boolean {
    return /\p{L}/u.test(this.newPassword) && /\p{Nd}/u.test(this.newPassword);
  }

  protected get ruleDifferent(): boolean {
    return this.newPassword !== '' && this.newPassword !== this.currentPassword;
  }

  protected get ruleEmail(): boolean {
    const name = this.email().split('@')[0];
    return (
      this.newPassword !== '' &&
      (name.length < MIN_EMAIL_NAME_LENGTH ||
        !this.newPassword.toLowerCase().includes(name.toLowerCase()))
    );
  }

  protected get confirmMismatch(): boolean {
    return this.confirmPassword !== '' && this.confirmPassword !== this.newPassword;
  }

  protected get canSubmit(): boolean {
    return (
      this.currentPassword !== '' &&
      this.ruleLength &&
      this.ruleMix &&
      this.ruleDifferent &&
      this.ruleEmail &&
      this.confirmPassword === this.newPassword &&
      !this.saving()
    );
  }

  /** User-initiated close (Cancel, ×, backdrop, Escape): ignored while the request is in flight. */
  @HostListener('document:keydown.escape')
  protected close(): void {
    if (this.saving() || this.dismissed) return;
    this.dismissed = true;
    // Typed passwords must not outlive the popup.
    this.currentPassword = '';
    this.newPassword = '';
    this.confirmPassword = '';
    this.closed.emit();
    this.opener?.focus();
  }

  protected submit(): void {
    if (!this.canSubmit) return;

    this.saving.set(true);
    this.errorText.set(null);
    this.auth
      .changePassword({ currentPassword: this.currentPassword, newPassword: this.newPassword })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.currentPassword = '';
          this.newPassword = '';
          this.confirmPassword = '';
          this.changed.emit();
          this.opener?.focus();
        },
        error: (error: unknown) => this.errorText.set(this.errorMessage(error)),
      });
  }

  private errorMessage(error: unknown): string {
    const text = this.localization.profile;
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) return text.accountLocked;
      if (error.status === 400) {
        return error.error?.errorCode === INVALID_CURRENT_PASSWORD
          ? text.invalidCurrent
          : text.passwordPolicyError;
      }
    }
    return text.passwordGenericError;
  }
}
