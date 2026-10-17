import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

@Component({
  selector: 'app-home-page',
  imports: [HeaderComponent, SidebarComponent],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.scss',
})
export class HomePageComponent extends LocalizedComponent {
  protected readonly currentUser = inject(AuthService).currentUser;
}
