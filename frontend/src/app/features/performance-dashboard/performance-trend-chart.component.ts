import { Component, computed, input } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentTrendPoint } from '../../core/department.models';
import { formatValue } from './performance-display';

const WIDTH = 340;
const HEIGHT = 220;
const LEFT = 34;
const BOTTOM = 36;
const TOP = 20;
const TICKS = 4;

/**
 * Average score of the last N periods (N = PERF_TREND_PERIOD_COUNT on the API) as SVG bars. The axis range comes from
 * the data (minimum and maximum rounded outwards), never from fixed numbers; the selected period is the last bar.
 */
@Component({
  selector: 'app-perf-trend-chart',
  template: `
    <section class="card" [attr.aria-label]="title()">
  <div class="card-head">
    <h2>
      {{title()}} · {{points().length}} {{localization.departmentPerformance.periodsUnit}}
    </h2>
  </div>
  @if (hasData()) {
    <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="summary()">
      @for (tick of ticks(); track tick.value) {
        <line
          [attr.x1]="left"
          [attr.x2]="width"
          [attr.y1]="tick.y"
          [attr.y2]="tick.y"
          class="grid"
        />
        <text [attr.x]="left - 6" [attr.y]="tick.y + 4" class="axis" text-anchor="end">
          {{tick.label}}
        </text>
      }
      @for (bar of bars(); track bar.id; let last = $last) {
        @if (bar.height !== null) {
          <rect
            [attr.x]="bar.x"
            [attr.y]="bar.y"
            [attr.width]="bar.width"
            [attr.height]="bar.height"
            rx="3"
            [class]="last ? 'bar current' : 'bar'"
          />
        }
        <text
          [attr.x]="bar.x + bar.width / 2"
          [attr.y]="bar.labelY"
          class="value"
          [class.strong]="last"
          text-anchor="middle"
        >
          {{bar.value}}
        </text>
        <text
          [attr.x]="bar.x + bar.width / 2"
          [attr.y]="height - 20"
          class="axis"
          [class.strong]="last"
          text-anchor="middle"
        >
          <tspan [attr.x]="bar.x + bar.width / 2">{{bar.nameHead}}</tspan>
          <tspan [attr.x]="bar.x + bar.width / 2" dy="12">{{bar.nameTail}}</tspan>
        </text>
      }
    </svg>
  } @else {
    <p class="empty">{{localization.departmentPerformance.noTrend}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    svg {
      display: block;
      width: 100%;
      height: auto;
    }
    .grid {
      stroke: #eef2f7;
    }
    .axis,
    .value {
      fill: #64748b;
      font:
        500 10px 'JetBrains Mono',
        monospace;
    }
    .strong {
      fill: #0f172a;
      font-weight: 700;
    }
    .bar {
      fill: #a9c8ee;
    }
    .bar.current {
      fill: #2563eb;
    }
  `,
})
export class PerfTrendChartComponent extends LocalizedComponent {
  readonly points = input.required<DepartmentTrendPoint[]>();
  readonly title = input.required<string>();
  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly left = LEFT;

  protected readonly hasData = computed(() => this.points().some((p) => p.average != null));

  /** Axis bounds from the data: one unit below the lowest and above the highest average. */
  private readonly range = computed(() => {
    const values = this.points()
      .map((p) => p.average)
      .filter((v): v is number => v != null);
    const min = Math.floor(Math.min(...values)) - 1;
    const max = Math.ceil(Math.max(...values)) + 1;
    return { min, max: max > min ? max : min + 1 };
  });

  protected readonly ticks = computed(() => {
    const { min, max } = this.range();
    return Array.from({ length: TICKS + 1 }, (_, i) => {
      const value = min + ((max - min) * i) / TICKS;
      return { value, y: this.y(value), label: formatValue(this.i18n.currentLanguage(), value, 0) };
    });
  });

  protected readonly bars = computed(() => {
    const points = this.points();
    const slot = (WIDTH - LEFT) / Math.max(points.length, 1);
    const barWidth = slot * 0.6;
    const base = HEIGHT - BOTTOM;
    return points.map((p, i) => {
      const y = p.average == null ? base : this.y(p.average);
      return {
        id: p.periodId,
        // The API period name ("Tháng 10/2026") is split on its last space to fit under the bar.
        nameHead: p.periodName.slice(0, Math.max(p.periodName.lastIndexOf(' '), 0)),
        nameTail: p.periodName.slice(p.periodName.lastIndexOf(' ') + 1),
        x: LEFT + slot * i + (slot - barWidth) / 2,
        width: barWidth,
        y,
        height: p.average == null ? null : base - y,
        labelY: y - 6,
        value: formatValue(this.i18n.currentLanguage(), p.average),
      };
    });
  });

  protected readonly summary = computed(() =>
    this.points()
      .map((p) => `${p.periodName}: ${formatValue(this.i18n.currentLanguage(), p.average)}`)
      .join(', '),
  );

  private y(value: number): number {
    const { min, max } = this.range();
    const usable = HEIGHT - BOTTOM - TOP;
    return HEIGHT - BOTTOM - ((value - min) / (max - min)) * usable;
  }
}
