import { Component, input, output } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EmployeeListItem } from '../../core/employee-management.models';

@Component({
  selector: 'app-employee-detail-drawer',
  standalone: true,
  templateUrl: './employee-detail-drawer.component.html',
  styleUrls: ['./employee-detail-drawer.component.scss'],
  host: {
    '(window:keydown)': 'onKeydown($event)',
  },
})
export class EmployeeDetailDrawerComponent extends LocalizedComponent {
  readonly employee = input.required<EmployeeListItem>();
  readonly closeDrawer = output<void>();

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closeDrawer.emit();
    }
  }

  getEvaluationLockedText(period: string, tier: string): string {
    return this.i18n.translate('employeeList.evaluationLocked', { period, tier });
  }

  getLatestImportText(batch: string, time: string, status: string): string {
    return this.i18n.translate('employeeList.latestImportDetails', { batch, time, status });
  }
}
