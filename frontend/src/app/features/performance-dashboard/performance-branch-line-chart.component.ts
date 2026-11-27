import { Component, computed, input, signal } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentYearlyTrend } from '../../core/department.models';
import { formatValue, NOT_AVAILABLE } from './performance-display';

const WIDTH = 420;
const HEIGHT = 220;
const LEFT = 36;
const RIGHT = 14;
const TOP = 18;
const BOTTOM = 32;
const TICKS = 4;
const MONTHS_VI = ['T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10', 'T11', 'T12'];
const MONTHS_EN = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

interface ChartPoint {
  month: number;
  label: string;
  x: number;
  current: number | null;
  previous: number | null;
  currentY: number | null;
  previousY: number | null;
}

/**
 * 12-month branch performance comparison line chart (current year vs previous year).
 * The Y-axis range is derived from the non-null values across both years.
 */
@Component({
  selector: 'app-perf-branch-line-chart',
  template: `
    <section class="card" [attr.aria-label]="title()">
  <div class="card-head">
    <h2>{{title()}} · {{localization.branchPerformance.trendRange}}</h2>
    @if (trend(); as t) {
      <div class="legend" aria-hidden="true">
        <span class="legend-item">
          <span class="swatch current"></span>
          <span>{{t.currentYear}}</span>
        </span>
        <span class="legend-item">
          <span class="swatch previous"></span>
          <span>{{t.previousYear}}</span>
        </span>
      </div>
    }
  </div>

  @if (hasData()) {
    <svg
      [attr.viewBox]="'0 0 ' + width + ' ' + height"
      role="img"
      [attr.aria-label]="summary()"
      (mouseleave)="activeMonth.set(null)"
    >
      @for (tick of ticks(); track tick.value) {
        <line
          [attr.x1]="left"
          [attr.x2]="width - right"
          [attr.y1]="tick.y"
          [attr.y2]="tick.y"
          class="grid"
        />
        <text [attr.x]="left - 6" [attr.y]="tick.y + 4" class="axis" text-anchor="end">
          {{tick.label}}
        </text>
      }

      @if (hoveredPoint(); as active) {
        <line
          [attr.x1]="active.x"
          [attr.x2]="active.x"
          [attr.y1]="top"
          [attr.y2]="baseY"
          class="guide"
        />
      }

      @for (area of currentAreas(); track $index) {
        <path [attr.d]="area" class="area-current" />
      }

      @if (previousPath()) {
        <path [attr.d]="previousPath()" class="line-previous" />
      }

      @if (currentPath()) {
        <path [attr.d]="currentPath()" class="line-current" />
      }

      @for (pt of chartPoints(); track pt.month) {
        @if (pt.previousY !== null) {
          <circle
            [attr.cx]="pt.x"
            [attr.cy]="pt.previousY"
            [attr.r]="activeMonth() === pt.month ? 4.5 : 3"
            class="dot-previous"
          />
        }
        @if (pt.currentY !== null) {
          <circle
            [attr.cx]="pt.x"
            [attr.cy]="pt.currentY"
            [attr.r]="activeMonth() === pt.month ? 5 : 3.5"
            class="dot-current"
          />
        }
        <text
          [attr.x]="pt.x"
          [attr.y]="height - 10"
          class="axis"
          [class.strong]="activeMonth() === pt.month"
          text-anchor="middle"
        >
          {{pt.label}}
        </text>
        <rect
          [attr.x]="pt.x - slotWidth() / 2"
          [attr.y]="top"
          [attr.width]="slotWidth()"
          [attr.height]="baseY - top"
          class="hit-target"
          tabindex="0"
          [attr.aria-label]="pointTooltip(pt)"
          (mouseenter)="activeMonth.set(pt.month)"
          (focus)="activeMonth.set(pt.month)"
          (blur)="activeMonth.set(null)"
        >
          <title>{{pointTooltip(pt)}}</title>
        </rect>
      }
    </svg>

    @if (hoveredPoint(); as active) {
      <p class="readout" aria-live="polite">
        <strong>{{active.label}}</strong>
        <span>· {{trend()!.currentYear}}: {{formatScore(active.current)}}</span>
        <span>· {{trend()!.previousYear}}: {{formatScore(active.previous)}}</span>
      </p>
    }
  } @else {
    <p class="empty">{{localization.departmentPerformance.noTrend}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .legend {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      font:
        600 12px 'Nunito Sans',
        sans-serif;
      color: #475569;
    }
    .legend-item {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .swatch {
      display: inline-block;
      width: 16px;
      height: 0;
      border-top: 2.5px solid #2563eb;
    }
    .swatch.previous {
      border-top: 2px dashed #f59e0b;
    }
    svg {
      display: block;
      width: 100%;
      height: auto;
    }
    .grid {
      stroke: #eef2f7;
    }
    .guide {
      stroke: #cbd5e1;
      stroke-dasharray: 3 3;
    }
    .axis {
      fill: #64748b;
      font:
        500 10px 'JetBrains Mono',
        monospace;
    }
    .strong {
      fill: #0f172a;
      font-weight: 700;
    }
    .area-current {
      fill: rgb(37 99 235 / 8%);
      stroke: none;
    }
    .line-current {
      fill: none;
      stroke: #2563eb;
      stroke-width: 2.5;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .line-previous {
      fill: none;
      stroke: #f59e0b;
      stroke-width: 2;
      stroke-dasharray: 5 3;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .dot-current {
      fill: #2563eb;
      stroke: #fff;
      stroke-width: 1.5;
    }
    .dot-previous {
      fill: #f59e0b;
      stroke: #fff;
      stroke-width: 1.5;
    }
    .hit-target {
      fill: transparent;
      cursor: pointer;
      outline: none;
    }
    .readout {
      margin: 8px 0 0;
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      font-size: 12px;
      color: #475569;
    }
    .readout strong {
      color: #0f172a;
    }
  `,
})
export class PerfBranchLineChartComponent extends LocalizedComponent {
  readonly trend = input<DepartmentYearlyTrend | null | undefined>(null);
  readonly title = input.required<string>();
  protected readonly activeMonth = signal<number | null>(null);
  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly left = LEFT;
  protected readonly right = RIGHT;
  protected readonly top = TOP;
  protected readonly baseY = HEIGHT - BOTTOM;

  private readonly months = computed(() => {
    const raw = this.trend()?.months ?? [];
    const byMonth = new Map(raw.map((m) => [m.month, m]));
    return Array.from({ length: 12 }, (_, i) => {
      const month = i + 1;
      const item = byMonth.get(month);
      return {
        month,
        currentYearAverage: item?.currentYearAverage ?? null,
        previousYearAverage: item?.previousYearAverage ?? null,
      };
    });
  });

  protected readonly hasData = computed(
    () =>
      this.trend() != null &&
      this.months().some((m) => m.currentYearAverage != null || m.previousYearAverage != null),
  );

  private readonly range = computed(() => {
    const values = this.months()
      .flatMap((m) => [m.currentYearAverage, m.previousYearAverage])
      .filter((v): v is number => v != null);
    if (values.length === 0) return { min: 0, max: 100 };
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

  protected readonly slotWidth = computed(() => (WIDTH - LEFT - RIGHT) / 12);

  protected readonly chartPoints = computed<ChartPoint[]>(() => {
    const labels = this.i18n.currentLanguage() === 'vi' ? MONTHS_VI : MONTHS_EN;
    const slot = this.slotWidth();
    return this.months().map((m, i) => ({
      month: m.month,
      label: labels[i],
      x: LEFT + slot * i + slot / 2,
      current: m.currentYearAverage,
      previous: m.previousYearAverage,
      currentY: m.currentYearAverage == null ? null : this.y(m.currentYearAverage),
      previousY: m.previousYearAverage == null ? null : this.y(m.previousYearAverage),
    }));
  });

  protected readonly currentPath = computed(() =>
    this.buildLinePath(
      this.chartPoints().map((p) => (p.currentY == null ? null : { x: p.x, y: p.currentY })),
    ),
  );

  protected readonly previousPath = computed(() =>
    this.buildLinePath(
      this.chartPoints().map((p) => (p.previousY == null ? null : { x: p.x, y: p.previousY })),
    ),
  );

  protected readonly currentAreas = computed(() => {
    const segments: string[] = [];
    let currentSegment: { x: number; y: number }[] = [];
    for (const p of this.chartPoints()) {
      if (p.currentY == null) {
        if (currentSegment.length >= 2) segments.push(this.areaPath(currentSegment));
        currentSegment = [];
      } else {
        currentSegment.push({ x: p.x, y: p.currentY });
      }
    }
    if (currentSegment.length >= 2) segments.push(this.areaPath(currentSegment));
    return segments;
  });

  protected readonly hoveredPoint = computed(() => {
    const active = this.activeMonth();
    return active == null ? null : (this.chartPoints().find((p) => p.month === active) ?? null);
  });

  protected readonly summary = computed(() => {
    const t = this.trend();
    if (!t) return this.title();
    return this.chartPoints()
      .map(
        (p) =>
          `${p.label}: ${t.currentYear} ${this.formatScore(p.current)}, ${t.previousYear} ${this.formatScore(p.previous)}`,
      )
      .join('; ');
  });

  protected formatScore(value: number | null): string {
    return value == null ? NOT_AVAILABLE : formatValue(this.i18n.currentLanguage(), value);
  }

  protected pointTooltip(pt: ChartPoint): string {
    const t = this.trend();
    if (!t) return pt.label;
    return `${pt.label} · ${t.currentYear}: ${this.formatScore(pt.current)} · ${t.previousYear}: ${this.formatScore(pt.previous)}`;
  }

  private buildLinePath(coords: ({ x: number; y: number } | null)[]): string {
    const parts: string[] = [];
    let drawing = false;
    for (const pt of coords) {
      if (!pt) {
        drawing = false;
        continue;
      }
      parts.push(`${drawing ? 'L' : 'M'}${pt.x.toFixed(1)},${pt.y.toFixed(1)}`);
      drawing = true;
    }
    return parts.join(' ');
  }

  private areaPath(segment: { x: number; y: number }[]): string {
    const line = segment
      .map((pt, i) => `${i === 0 ? 'M' : 'L'}${pt.x.toFixed(1)},${pt.y.toFixed(1)}`)
      .join(' ');
    const last = segment[segment.length - 1];
    const first = segment[0];
    return `${line} L${last.x.toFixed(1)},${this.baseY.toFixed(1)} L${first.x.toFixed(1)},${this.baseY.toFixed(1)} Z`;
  }

  private y(value: number): number {
    const { min, max } = this.range();
    const usable = this.baseY - TOP;
    return this.baseY - ((value - min) / (max - min)) * usable;
  }
}
