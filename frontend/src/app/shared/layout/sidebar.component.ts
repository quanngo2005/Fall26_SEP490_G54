import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { LayoutIconComponent } from './layout-icon.component';
import { NavigationService } from './navigation.service';

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive, LayoutIconComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent extends LocalizedComponent {
  readonly navigation = inject(NavigationService);
}
