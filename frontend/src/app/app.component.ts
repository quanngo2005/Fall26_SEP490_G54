import { Component, effect, inject, untracked } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';
import { ChangePasswordDialogComponent } from './features/employee-detail/change-password-dialog.component';
import { NotificationToastsComponent } from './shared/layout/notification-toasts.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ChangePasswordDialogComponent, NotificationToastsComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  constructor() {
    // The live notification channel follows the session: opened at sign-in, closed at sign-out.
    effect(() => {
      const user = this.auth.currentUser();
      untracked(() => (user ? this.notifications.activate() : this.notifications.deactivate()));
    });
  }
}
