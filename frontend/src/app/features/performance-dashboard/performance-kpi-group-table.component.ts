import { Component, input } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentGroupKpi, DepartmentKpiGroup } from '../../core/department.models';
import { barWidth, formatArrow, formatValue, toneOf, TRACK_HEADROOM } from './performance-display';

/**
 * "KPI theo nhóm đánh giá": KPIs under the root groups of the evaluation profiles, with the weight of each group per
 * job position. Group names, weights, KPIs and units all come from the API.
 */
@Component({
  selector: 'app-perf-kpi-group-table',
  templateUrl: './performance-kpi-group-table.component.html',
  styleUrls: ['./performance-card.scss', './performance-kpi-group-table.component.scss'],
})
export class PerfKpiGroupTableComponent extends LocalizedComponent {
  readonly groups = input.required<DepartmentKpiGroup[]>();
  /** `comparePeriod.name`; null hides the ± column. */
  readonly compareName = input<string | null>(null);
  /** SCR-PRF-03 shows the campaign part of each actual (BR-26). */
  readonly showCampaign = input(false);

  protected value(value: number | null, unit: string, decimals = 1): string {
    const text = formatValue(this.i18n.currentLanguage(), value, decimals);
    return value == null || !unit ? text : `${text} ${unit}`;
  }

  protected target(kpi: DepartmentGroupKpi): string {
    const text = this.value(kpi.targetValue, kpi.unit);
    return kpi.direction === 'LOWER_BETTER' ? `≤ ${text}` : text;
  }

  protected percent(value: number | null): string {
    return value == null
      ? formatValue(this.i18n.currentLanguage(), value)
      : `${formatValue(this.i18n.currentLanguage(), value, 0)}%`;
  }

  protected width(value: number | null): number {
    return barWidth(value, 100 * TRACK_HEADROOM);
  }

  protected marker(): number {
    return barWidth(100, 100 * TRACK_HEADROOM);
  }

  protected change(kpi: DepartmentGroupKpi): string {
    return formatArrow(this.i18n.currentLanguage(), kpi.achievementChange, 0);
  }

  protected tone(kpi: DepartmentGroupKpi): string {
    return toneOf(kpi.achievementChange) ?? '';
  }

  protected weights(group: DepartmentKpiGroup): string {
    return group.weights
      .map(
        (w) =>
          `${w.jobPositionTitle} ${formatValue(this.i18n.currentLanguage(), w.weightPercent, 0)}%`,
      )
      .join(' · ');
  }
}
