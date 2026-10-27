import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { LayoutIconComponent } from './layout-icon.component';
import { NavigationService } from './navigation.service';

@Component({
  selector: 'app-header',
  imports: [RouterLink, LayoutIconComponent],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
})
export class HeaderComponent extends LocalizedComponent {
  readonly navigation = inject(NavigationService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly user = this.auth.currentUser;
  readonly loggingOut = signal(false);
  readonly logoutFailed = signal(false);
  get initials(): string {
    return (this.user()?.fullName ?? '')
      .trim()
      .split(/\s+/)
      .slice(-2)
      .map((s) => s[0])
      .join('')
      .toUpperCase();
  }
  logout(): void {
    if (this.loggingOut() || !window.confirm(this.localization.home.confirmLogout)) return;
    this.loggingOut.set(true);
    this.logoutFailed.set(false);
    this.auth.logout().subscribe({
      next: () => {
        this.navigation.closeMobile();
        void this.router.navigateByUrl('/login');
      },
      error: (error: HttpErrorResponse) => {
        this.loggingOut.set(false);
        if (error.status === 401) {
          this.auth.clearSession();
          void this.router.navigateByUrl('/login');
        } else this.logoutFailed.set(true);
      },
      complete: () => this.loggingOut.set(false),
    });
  }
}
