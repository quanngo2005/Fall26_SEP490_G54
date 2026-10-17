import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';

@Component({
  selector: 'app-home-page',
  imports: [RouterLink],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.scss',
})
export class HomePageComponent extends LocalizedComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly currentUser = this.authService.currentUser;
  protected readonly loggingOut = signal(false);
  protected readonly errorMessage = signal('');

  protected logout(): void {
    if (this.loggingOut() || !window.confirm(this.localization.home.confirmLogout)) {
      return;
    }

    this.loggingOut.set(true);
    this.errorMessage.set('');
    this.authService.logout().subscribe({
      next: () => {
        void this.router.navigateByUrl('/login');
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.authService.clearSession();
          void this.router.navigateByUrl('/login');
          return;
        }

        this.errorMessage.set(this.localization.home.logoutFailed);
        this.loggingOut.set(false);
      },
      complete: () => this.loggingOut.set(false),
    });
  }
}
