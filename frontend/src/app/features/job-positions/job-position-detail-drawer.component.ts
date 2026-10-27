import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionService } from '../../core/job-position.service';
import { JobPositionDetail } from '../../core/job-position.models';
import { displayNumber } from '../../shared/ui/display';

/** SCR-ORG-02-D1: evaluation criteria, work types and KPI items of the job position selected in the list. */
@Component({
  selector: 'app-job-position-detail-drawer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './job-position-detail-drawer.component.html',
  styleUrls: ['./job-position-detail-drawer.component.scss'],
})
export class JobPositionDetailDrawerComponent extends LocalizedComponent {
  private readonly api = inject(JobPositionService);
  private request: Subscription | null = null;

  readonly positionId = input.required<string>();
  readonly closeDrawer = output<void>();

  protected readonly detail = signal<JobPositionDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  constructor() {
    super();
    // Reload whenever another position is selected; the previous request is cancelled with the effect.
    effect((onCleanup) => {
      const id = this.positionId();
      untracked(() => this.loadDetail(id));
      onCleanup(() => this.request?.unsubscribe());
    });
  }

  loadDetail(id: string): void {
    this.request?.unsubscribe();
    this.detail.set(null);
    this.loading.set(true);
    this.failed.set(false);
    this.request = this.api.detail(id).subscribe({
      next: (res) => {
        this.detail.set(res);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  formatEffectiveDate(value: string | undefined): string {
    return this.i18n.formatDate(value, 'DD/MM/YYYY');
  }

  number(val: number | null | undefined): string {
    return displayNumber(
      val,
      this.i18n.currentLanguage(),
      this.localization.department.notAvailable,
    );
  }
}
