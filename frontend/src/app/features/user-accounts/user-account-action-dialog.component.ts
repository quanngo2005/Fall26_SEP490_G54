import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, HostListener, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiResponse } from '../../core/api-response.model';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { UserAccountDetail } from '../../core/user-account.models';
import { UserAccountService } from '../../core/user-account.service';

export type AccountAction = 'reset' | 'lock' | 'unlock';

/** Confirmation (and, for a password reset, the one-time result) for the drawer's account actions. */
@Component({
  selector: 'app-user-account-action-dialog',
  templateUrl: './user-account-action-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './user-account-action-dialog.component.scss',
  ],
})
export class UserAccountActionDialogComponent extends LocalizedComponent {
  private readonly api = inject(UserAccountService);
  private readonly destroyRef = inject(DestroyRef);

  readonly account = input.required<UserAccountDetail>();
  readonly kind = input.required<AccountAction>();
  readonly closed = output<void>();
  /** Lock/unlock hand back the updated account; a reset has nothing to hand back. */
  readonly done = output<UserAccountDetail | null>();

  protected readonly saving = signal(false);
  protected readonly error = signal<'cannotLockSelf' | 'actionError' | null>(null);
  protected readonly temporaryPassword = signal<string | null>(null);
  protected readonly copied = signal(false);

  protected readonly copy = computed(() => {
    const t = this.localization.userAccounts;
    const username = this.account().username;
    switch (this.kind()) {
      case 'reset':
        return {
          title: t.resetTitle.replace('{username}', username),
          message: t.resetConfirm,
          submit: t.resetSubmit,
        };
      case 'lock':
        return {
          title: t.lockTitle.replace('{username}', username),
          message: t.lockConfirm,
          submit: t.lockSubmit,
        };
      case 'unlock':
        return {
          title: t.unlockTitle.replace('{username}', username),
          message: t.unlockConfirm,
          submit: t.unlockSubmit,
        };
    }
  });

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  /** Once a temporary password is on screen it can only be dismissed with "Done" so it is not lost by accident. */
  protected close(): void {
    if (this.saving() || this.temporaryPassword()) return;
    this.closed.emit();
  }

  protected finish(): void {
    this.done.emit(null);
  }

  protected confirm(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    const id = this.account().id;

    switch (this.kind()) {
      case 'reset':
        this.api
          .resetPassword(id)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (result) => {
              this.saving.set(false);
              this.temporaryPassword.set(result.temporaryPassword);
            },
            error: (error: unknown) => this.fail(error),
          });
        return;
      case 'lock':
      case 'unlock':
        (this.kind() === 'lock' ? this.api.lock(id) : this.api.unlock(id))
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (updated) => {
              this.saving.set(false);
              this.done.emit(updated);
            },
            error: (error: unknown) => this.fail(error),
          });
    }
  }

  protected copyPassword(): void {
    const password = this.temporaryPassword();
    if (!password) return;
    navigator.clipboard?.writeText(password).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  private fail(error: unknown): void {
    this.saving.set(false);
    const body = error instanceof HttpErrorResponse ? (error.error as ApiResponse<null>) : null;
    this.error.set(body?.errorCode === 'CANNOT_LOCK_SELF' ? 'cannotLockSelf' : 'actionError');
  }
}
