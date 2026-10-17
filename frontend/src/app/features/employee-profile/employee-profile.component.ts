import { Component, signal } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { SupportedLocale } from '../../core/i18n/i18n.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { I18N_PIPES } from '../../shared/pipes';
import { EMPLOYEE_PROFILE } from './employee-profile.data';

@Component({
  selector: 'app-employee-profile',
  standalone: true,
  imports: [HeaderComponent, SidebarComponent, ...I18N_PIPES],
  templateUrl: './employee-profile.component.html',
})
export class EmployeeProfileComponent extends LocalizedComponent {
  protected readonly data = EMPLOYEE_PROFILE;
  protected readonly activeNavigation = signal('employees');
  protected readonly showMoreActions = signal(false);

  readonly currentLanguage = this.i18n.currentLanguage;

  toggleLanguage(): void {
    this.i18n.toggleLanguage();
  }

  setLanguage(lang: SupportedLocale): void {
    this.i18n.setLanguage(lang);
  }

  protected selectNavigation(label: string): void {
    this.activeNavigation.set(label);
  }

  protected toggleMoreActions(): void {
    this.showMoreActions.update((visible) => !visible);
  }
}
