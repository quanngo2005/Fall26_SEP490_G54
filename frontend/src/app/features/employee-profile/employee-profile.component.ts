import { Component, signal } from '@angular/core';
import { EMPLOYEE_PROFILE } from './employee-profile.data';

@Component({
  selector: 'app-employee-profile',
  templateUrl: './employee-profile.component.html',
})
export class EmployeeProfileComponent {
  protected readonly data = EMPLOYEE_PROFILE;
  protected readonly activeNavigation = signal('Hồ sơ Cán bộ');
  protected readonly showMoreActions = signal(false);

  protected selectNavigation(label: string): void {
    this.activeNavigation.set(label);
  }

  protected toggleMoreActions(): void {
    this.showMoreActions.update((visible) => !visible);
  }
}
