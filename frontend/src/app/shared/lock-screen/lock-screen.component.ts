import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IdleTimeoutService } from '../../core/idle-timeout.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';

@Component({
  selector: 'app-lock-screen',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './lock-screen.component.html',
  styleUrl: './lock-screen.component.scss',
})
export class LockScreenComponent extends LocalizedComponent {
  readonly idleService = inject(IdleTimeoutService);

  readonly password = signal<string>('');
  readonly errorMessage = signal<string | null>(null);
  readonly isSubmitting = signal<boolean>(false);

  readonly isLocked = this.idleService.isLocked;
  readonly isWarning = this.idleService.isWarning;
  readonly isSensitive = this.idleService.isSensitive;
  readonly currentUser = this.idleService.currentUser;

  readonly warningMessage = computed(() => {
    const raw = this.localization.sessionLock.warningMessage;
    const seconds = this.idleService.remainingSeconds().toString();
    return raw.replace('{seconds}', seconds);
  });

  readonly userInitials = computed(() => {
    const name = this.currentUser()?.fullName ?? '';
    if (!name) return 'U';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].charAt(0).toUpperCase();
    return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
  });

  onPasswordInput(value: string): void {
    this.password.set(value);
    if (this.errorMessage()) {
      this.errorMessage.set(null);
    }
  }

  unlock(): void {
    const pwd = this.password().trim();
    if (!pwd) {
      this.errorMessage.set(this.localization.sessionLock.passwordError);
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    this.idleService.unlock(pwd).subscribe({
      next: () => {
        this.password.set('');
        this.isSubmitting.set(false);
      },
      error: () => {
        this.errorMessage.set(this.localization.sessionLock.invalidPassword);
        this.isSubmitting.set(false);
      },
    });
  }

  stayLoggedIn(): void {
    this.idleService.stayLoggedIn();
  }

  lockNow(): void {
    this.idleService.lock();
  }

  logout(): void {
    this.password.set('');
    this.errorMessage.set(null);
    this.idleService.logout();
  }
}
