import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentGroupKpi, DepartmentKpiGroup } from '../../core/department.models';
import { barWidth, formatArrow, formatValue, toneOf, TRACK_HEADROOM } from './performance-display';

/** "KPI chưa đạt kế hoạch": the KPIs whose achievement is below 100 %, lowest first. */
@Component({
  selector: 'app-perf-kpi-below-plan',
  template: `
    @let text = localization.departmentPerformance;
<section class="card is-clickable" aria-labelledby="kpi-below-title">
  <div class="card-head">
    <h2 id="kpi-below-title">{{text.kpiBelowPlan}}</h2>
    <button type="button" class="card-link" (click)="viewAll.emit()">
      {{text.view}} {{kpiCount()}} {{text.kpiUnit}}<span class="chevron" aria-hidden="true">›</span>
    </button>
  </div>
  @for (kpi of below(); track kpi.kpiDefinitionId) {
    <div class="kpi">
      <div class="kpi-line">
        <span>{{kpi.name}}</span>
        <strong class="down mono">{{percent(kpi.achievementPercent)}}</strong>
      </div>
      <div class="kpi-line">
        <span class="track" aria-hidden="true">
          <span class="fill" [style.width.%]="width(kpi.achievementPercent)"></span>
          <span class="marker" [style.left.%]="width(100)"></span>
        </span>
        @if (compareName()) {
          <small class="delta" [class]="tone(kpi)"
            >{{arrow(kpi)}} {{text.vs}} {{compareName()}}</small
          >
        }
      </div>
    </div>
  } @empty {
    <p class="empty">{{kpiCount() ? text.allKpisOnPlan : text.noKpis}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .kpi {
      margin-bottom: 14px;
    }
    .kpi-line {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      color: #0f172a;
      font-size: 14px;
    }
    .track {
      position: relative;
      flex: 1;
      height: 8px;
      margin-top: 6px;
      border-radius: 4px;
      background: #eef2f7;
    }
    .fill {
      position: absolute;
      inset: 0 auto 0 0;
      border-radius: 4px;
      background: #e11d48;
    }
    .marker {
      position: absolute;
      top: -4px;
      width: 2px;
      height: 16px;
      background: #0f172a;
    }
    .delta {
      min-width: 88px;
      text-align: right;
      font-size: 12px;
    }
  `,
})
export class PerfKpiBelowPlanComponent extends LocalizedComponent {
  readonly groups = input.required<DepartmentKpiGroup[]>();
  /** `comparePeriod.name`; null hides the comparison. */
  readonly compareName = input<string | null>(null);
  readonly viewAll = output<void>();

  protected readonly kpiCount = computed(() =>
    this.groups().reduce((sum, group) => sum + group.kpis.length, 0),
  );
  protected readonly below = computed(() =>
    this.groups()
      .flatMap((group) => group.kpis)
      .filter((kpi) => kpi.achievementPercent != null && kpi.achievementPercent < 100)
      .sort((a, b) => (a.achievementPercent ?? 0) - (b.achievementPercent ?? 0)),
  );
  /** Track scale: 100 % plus visual headroom, so the 100 % marker sits inside the track. */
  private readonly scale = 100 * TRACK_HEADROOM;

  protected width(value: number | null): number {
    return barWidth(value, this.scale);
  }

  protected percent(value: number | null): string {
    return value == null
      ? formatValue(this.locale, value)
      : `${formatValue(this.locale, value, 0)}%`;
  }

  protected arrow(kpi: DepartmentGroupKpi): string {
    return formatArrow(this.locale, kpi.achievementChange, 0);
  }

  protected tone(kpi: DepartmentGroupKpi): string {
    return toneOf(kpi.achievementChange) ?? '';
  }

  private get locale(): string {
    return this.i18n.currentLanguage();
  }
}
