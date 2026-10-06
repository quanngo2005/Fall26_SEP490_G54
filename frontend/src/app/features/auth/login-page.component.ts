import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LoginBrandPanelComponent } from './login-brand-panel.component';

@Component({
  selector: 'app-login-page',
  imports: [LoginBrandPanelComponent, ReactiveFormsModule, RouterLink],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly showPasswordHelp = signal(false);
  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(255)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)],
    }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });

  protected submit(): void {
    this.errorMessage.set('');
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.authService.login(this.form.getRawValue()).subscribe({
      next: () => {
        void this.router.navigateByUrl('/home');
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage.set(
          error.status === 403
            ? 'Your account is locked. Please contact your administrator.'
            : error.status === 401
              ? 'The email or password is incorrect.'
              : 'We could not sign you in. Please try again.',
        );
        this.submitting.set(false);
      },
      complete: () => this.submitting.set(false),
    });
  }

  protected togglePasswordHelp(): void {
    this.showPasswordHelp.update((visible) => !visible);
  }
}
