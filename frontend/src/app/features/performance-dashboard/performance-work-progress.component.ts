import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentOverview } from '../../core/department.models';

/**
 * "Tiến độ công việc đơn vị": what needs the manager (pending review, overdue, escalated - hidden while the API has no
 * source for it), the four disjoint parts of the work in the period, and open work by type.
 */
@Component({
  selector: 'app-perf-work-progress',
  imports: [RouterLink],
  templateUrl: './performance-work-progress.component.html',
  styleUrls: ['./performance-card.scss', './performance-work-progress.component.scss'],
})
export class PerfWorkProgressComponent extends LocalizedComponent {
  readonly operational = input.required<DepartmentOverview['operational']>();
  readonly workMix = input.required<DepartmentOverview['workMix']>();
  /** Review queue route (SCR-REV). */
  readonly queueLink = input('/review-verify');
  /** Team work board route, where overdue work is handled. */
  readonly workLink = input('/my-work');

  protected readonly segments = computed(() => {
    const op = this.operational();
    const text = this.localization.departmentPerformance;
    return [
      { key: 'completed', label: text.completed, count: op.completedInPeriod ?? 0 },
      { key: 'progress', label: text.inProgress, count: op.inProgress },
      { key: 'waiting', label: text.waiting, count: op.waiting },
      { key: 'overdue', label: text.overdue, count: op.overdue },
    ];
  });

  protected readonly total = computed(() =>
    this.segments().reduce((sum, segment) => sum + segment.count, 0),
  );

  protected readonly openTotal = computed(() =>
    this.workMix().reduce((sum, item) => sum + item.count, 0),
  );

  protected share(count: number): number {
    const total = this.total();
    return total ? (count / total) * 100 : 0;
  }
}
