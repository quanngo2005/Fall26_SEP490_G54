import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  BranchEvaluationPeriodItem,
  EvaluationPeriodDisplayStatus,
} from '../../core/evaluation.models';

/** Lifecycle of an evaluation period, in the order of the `displayStatus` enum (BRANCH_EVALUATION_PERIODS_CONTRACT.md). */
export const PERIOD_LIFECYCLE: readonly EvaluationPeriodDisplayStatus[] = [
  'IN_PROGRESS',
  'EVALUATING',
  'IN_REVIEW',
  'PUBLISHED',
  'LOCKED',
];

/**
 * The newest evaluation period of the branch that is not locked, from `GET /evaluations/branch-periods`: its status
 * in the lifecycle and how many departments submitted / were approved.
 */
@Component({
  selector: 'app-perf-evaluation-period',
  imports: [RouterLink],
  template: `
    @let text = localization.branchPerformance;
<section class="card is-clickable" aria-labelledby="period-card-title">
  <div class="card-head">
    <h2 id="period-card-title">{{text.periodTitle}} {{current()?.name}}</h2>
    <a
      class="card-link"
      routerLink="/branch-performance/evaluations"
      [attr.aria-label]="text.openPeriods"
      ><span class="chevron" aria-hidden="true">›</span></a
    >
  </div>
  @if (failed()) {
    <div class="error" role="alert">
      <p>{{text.periodLoadError}}</p>
      <button type="button" class="card-link" (click)="retry.emit()">
        {{localization.departmentPerformance.retry}}
      </button>
    </div>
  } @else if (current(); as period) {
    <span class="badge">{{statusLabel(period.displayStatus)}}</span>
    <p class="counts">
      {{period.submittedDepartmentCount}}/{{period.totalDepartmentCount}} {{text.submitted}} ·
      {{period.approvedDepartmentCount}} {{text.approved}}
    </p>
    <ol class="steps">
      @for (step of steps; track step; let i = $index) {
        <li [class.done]="i < currentIndex()" [class.current]="i === currentIndex()">
          {{statusLabel(step)}}
        </li>
      }
    </ol>
    <a class="button-dark" routerLink="/branch-performance/evaluations">{{text.openPeriods}} ›</a>
  } @else {
    <p class="empty">{{text.noOpenPeriod}}</p>
  }
</section>
  `,
  styleUrl: './perf-card.scss',
  styles: `
    .badge {
      display: inline-block;
      padding: 3px 12px;
      border: 1px solid #bfdbfe;
      border-radius: 12px;
      background: #eff6ff;
      color: #1d4ed8;
      font:
        600 11px 'JetBrains Mono',
        monospace;
      text-transform: uppercase;
    }
    .counts {
      margin: 10px 0;
      color: #334155;
      font-size: 13px;
    }
    .steps {
      margin: 0 0 14px;
      padding: 0;
      list-style: none;
    }
    .steps li {
      position: relative;
      padding: 4px 0 4px 24px;
      color: #94a3b8;
      font-size: 13px;
    }
    .steps li::before {
      content: '';
      position: absolute;
      left: 2px;
      top: 8px;
      width: 10px;
      height: 10px;
      border: 2px solid #cbd5e1;
      border-radius: 50%;
      background: #fff;
    }
    .steps li.done,
    .steps li.current {
      color: #0f172a;
    }
    .steps li.done::before {
      border-color: #0f2a44;
      background: #0f2a44;
    }
    .steps li.current {
      color: #1d4ed8;
      font-weight: 700;
    }
    .steps li.current::before {
      border-color: #2563eb;
      background: #2563eb;
    }
    .button-dark {
      display: inline-block;
      padding: 10px 18px;
      border-radius: 6px;
      background: #0f2a44;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      text-decoration: none;
    }
    .error p {
      margin: 0 0 8px;
      color: #be123c;
      font-size: 13px;
    }
  `,
})
export class PerfEvaluationPeriodComponent extends LocalizedComponent {
  readonly periods = input.required<BranchEvaluationPeriodItem[]>();
  readonly failed = input(false);
  readonly retry = output<void>();
  protected readonly steps = PERIOD_LIFECYCLE;

  /** Newest period (by start date) whose status is not LOCKED. */
  protected readonly current = computed(
    () =>
      [...this.periods()]
        .filter((p) => p.displayStatus !== 'LOCKED')
        .sort((a, b) => b.startDate.localeCompare(a.startDate))[0] ?? null,
  );

  protected readonly currentIndex = computed(() => {
    const period = this.current();
    return period ? PERIOD_LIFECYCLE.indexOf(period.displayStatus) : -1;
  });

  protected statusLabel(status: EvaluationPeriodDisplayStatus): string {
    const text = this.localization.branchPerformance;
    switch (status) {
      case 'IN_PROGRESS':
        return text.statusInProgress;
      case 'EVALUATING':
        return text.statusEvaluating;
      case 'IN_REVIEW':
        return text.statusInReview;
      case 'PUBLISHED':
        return text.statusPublished;
      default:
        return text.statusLocked;
    }
  }
}
