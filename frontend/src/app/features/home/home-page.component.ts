import { Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { canViewQaQueue } from '../../core/qa-queue.guard';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { QaDashboardComponent } from './qa-dashboard/qa-dashboard.component';

@Component({
  selector: 'app-home-page',
  imports: [HeaderComponent, SidebarComponent, QaDashboardComponent],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.scss',
})
export class HomePageComponent extends LocalizedComponent {
  protected readonly currentUser = inject(AuthService).currentUser;
  /** QA and VERIFIER (`quality_flag:read`, or full access) land on the QA dashboard (SCR-PRF-04); everyone else keeps the welcome card. */
  protected readonly showQaDashboard = computed(() =>
    canViewQaQueue(this.currentUser()?.permissions),
  );
}
