import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentChildUnit, PerformanceCompareMode } from '../../core/department.models';
import { barWidth, formatArrow, formatValue, toneOf } from './performance-display';

/** Visual only: room right of the longest bar for its value label. */
const LABEL_HEADROOM = 1.25;

/**
 * "Phòng ban · <compare> và <period>": each child unit's average in the compare period and the selected one; a row
 * opens SCR-PRF-02 for that unit with the same period and comparison.
 */
@Component({
  selector: 'app-perf-child-units',
  imports: [RouterLink],
  template: `
    @let text = localization.branchPerformance;
<section class="card is-clickable" id="child-units" aria-labelledby="child-units-title">
  <div class="card-head">
    <h2 id="child-units-title">
      {{text.childUnitsTitle}} ·
      @if (compareName()) {
        {{compareName()}} {{text.and}}
      }
      {{periodName()}}
    </h2>
    <a class="card-link" routerLink="/department-performance"
      >{{text.openDepartments}}<span class="chevron" aria-hidden="true">›</span></a
    >
  </div>
  @if (compareName()) {
    <p class="legend">
      <i class="prev"></i>{{compareName()}} <i class="cur"></i>{{periodName()}}
    </p>
  }
  @for (unit of units(); track unit.id) {
    <a
      class="unit row-link"
      routerLink="/department-performance"
      [queryParams]="{ department: unit.code, periodId: periodId(), compare: compare() }"
    >
      <span class="name"
        ><b>{{unit.name}}</b
        ><small
          >{{unit.evaluatedEmployeeCount}}/{{unit.employeeCount}} {{text.evaluated}}</small
        ></span
      >
      <span class="bars" aria-hidden="true">
        @if (compareName()) {
          <span class="bar prev" [style.width.%]="width(unit.previousAverage)"
            ><em>{{score(unit.previousAverage)}}</em></span
          >
        }
        <span class="bar cur" [style.width.%]="width(unit.average)"
          ><em>{{score(unit.average)}}</em></span
        >
      </span>
      <span class="change mono" [class]="tone(unit.changeFromPrevious)">{{arrow(unit.changeFromPrevious)}}</span>
      <span class="chev" aria-hidden="true">›</span>
    </a>
  } @empty {
    <p class="empty">{{text.noChildUnits}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .legend {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: -6px 0 10px;
      color: #475569;
      font-size: 12px;
    }
    .legend i {
      width: 12px;
      height: 12px;
      margin-left: 8px;
      border-radius: 2px;
    }
    .prev {
      background: #a9c8ee;
    }
    .cur {
      background: #2563eb;
    }
    .unit {
      display: grid;
      grid-template-columns: minmax(110px, 150px) 1fr 64px 14px;
      align-items: center;
      gap: 12px;
      padding: 8px 0;
      border-bottom: 1px solid #f1f5f9;
      color: #0f172a;
      text-decoration: none;
    }
    .name small {
      display: block;
      color: #64748b;
      font-size: 12px;
    }
    .bars {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .bar {
      position: relative;
      height: 16px;
      border-radius: 2px;
    }
    .bar em {
      position: absolute;
      left: calc(100% + 6px);
      font:
        500 11px 'JetBrains Mono',
        monospace;
      font-style: normal;
      white-space: nowrap;
    }
    .bar.cur em {
      font-weight: 700;
    }
    .change {
      text-align: right;
      font-size: 13px;
    }
    .chev {
      color: #94a3b8;
    }
  `,
})
export class PerfChildUnitsComponent extends LocalizedComponent {
  readonly units = input.required<DepartmentChildUnit[]>();
  readonly periodName = input<string | null>(null);
  readonly periodId = input<string | null>(null);
  readonly compareName = input<string | null>(null);
  readonly compare = input<PerformanceCompareMode>('PREVIOUS');

  /** Bars span the highest average shown plus room for the label. */
  private readonly max = computed(
    () =>
      Math.max(0, ...this.units().flatMap((u) => [u.average ?? 0, u.previousAverage ?? 0])) *
      LABEL_HEADROOM,
  );

  protected width(value: number | null): number {
    return barWidth(value, this.max());
  }

  protected score(value: number | null): string {
    return formatValue(this.i18n.currentLanguage(), value);
  }

  protected arrow(value: number | null): string {
    return formatArrow(this.i18n.currentLanguage(), value);
  }

  protected tone(value: number | null): string {
    return toneOf(value) ?? '';
  }
}
