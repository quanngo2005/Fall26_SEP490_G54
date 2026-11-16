import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { ChangePasswordDialogComponent } from './features/employee-detail/change-password-dialog.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ChangePasswordDialogComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
}
