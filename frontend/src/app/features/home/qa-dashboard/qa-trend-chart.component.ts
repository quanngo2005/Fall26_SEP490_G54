import { Component, computed, input } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { QaDashboardMatchedRateChart } from '../../../core/qa-dashboard.models';
import {
  currentWeekIndex,
  fill,
  formatNumber,
  niceAxisMax,
  openCount,
  previousRatedWeek,
} from './qa-dashboard-display';

const AXIS_TICKS = 3;

/**
 * "Mã giao dịch chưa khớp theo tuần": one column per week with `totalCount − matchedCount`, the axis from 0, the
 * current week dark blue, a dashed line at the average of the shown weeks and the match rate under each week label.
 * Plain HTML/CSS (like the other dashboards' hand-made charts) so labels keep their size on a phone.
 */
@Component({
  selector: 'app-qa-trend-chart',
  template: `
    <section class="qa-panel" [attr.aria-label]="text().title">
  <header class="qa-panel-head">
    <div class="head-left">
      <h2>{{text().title}}</h2>
      <span class="legend">
        <span><i class="swatch"></i>{{text().legendCount}}</span>
        <span><i class="dash"></i>{{legendAverage()}}</span>
      </span>
    </div>
    @if (summary(); as s) {
      <div class="summary">
        <strong>{{s.head}}</strong>
        <span>{{s.detail}}</span>
      </div>
    }
  </header>
  @if (hasData()) {
    <div class="chart" role="img" [attr.aria-label]="ariaSummary()">
      <div class="plot">
        @for (tick of ticks(); track tick.value) {
          <div class="grid" [class.base]="tick.value === 0" [style.bottom.%]="tick.pct">
            <span>{{tick.value}}</span>
          </div>
        }
        <div class="avg" [style.bottom.%]="averagePct()">
          <span>{{averageLabel()}}</span>
        </div>
        <div class="cols">
          @for (col of columns(); track col.key) {
            <div class="col" [class.current]="col.current">
              <span class="bar" [style.height.%]="col.pct">
                <span class="value">{{col.open}}</span>
              </span>
            </div>
          }
        </div>
      </div>
      <div class="labels" aria-hidden="true">
        @for (col of columns(); track col.key) {
          <div class="label" [class.current]="col.current">
            <span class="week">{{col.week}}</span>
            <span class="rate">{{col.rate}}</span>
          </div>
        }
      </div>
    </div>
  } @else {
    <p class="qa-empty">{{empty()}}</p>
  }
</section>
  `,
  styleUrls: ['./qa-panel.scss', './qa-trend-chart.component.scss'],
})
export class QaTrendChartComponent extends LocalizedComponent {
  readonly chart = input.required<QaDashboardMatchedRateChart>();

  protected readonly text = computed(() => this.localization.qaDashboard.trend);
  private readonly locale = computed(() => this.i18n.currentLanguage());
  private readonly weeks = computed(() => this.chart().weeks ?? []);

  protected readonly hasData = computed(() => this.weeks().some((w) => w.totalCount > 0));

  private readonly opens = computed(() => this.weeks().map(openCount));
  private readonly axisMax = computed(() => niceAxisMax(Math.max(0, ...this.opens()), AXIS_TICKS));
  private readonly average = computed(() => {
    const opens = this.opens();
    return opens.length ? opens.reduce((a, b) => a + b, 0) / opens.length : 0;
  });
  private readonly currentIndex = computed(() =>
    currentWeekIndex(this.weeks(), this.chart().currentWeekLabel),
  );

  protected readonly ticks = computed(() => {
    const max = this.axisMax();
    return Array.from({ length: AXIS_TICKS + 1 }, (_, i) => {
      const value = (max / AXIS_TICKS) * i;
      return { value, pct: (value / max) * 100 };
    });
  });

  protected readonly averagePct = computed(() => (this.average() / this.axisMax()) * 100);
  protected readonly averageLabel = computed(() =>
    fill(this.text().averageShort, { value: formatNumber(this.locale(), this.average(), 1) }),
  );
  protected readonly legendAverage = computed(() =>
    fill(this.text().legendAverage, { weeks: this.weeks().length || 8 }),
  );
  protected readonly empty = computed(() =>
    fill(this.text().empty, { weeks: this.weeks().length || 8 }),
  );

  protected readonly columns = computed(() => {
    const max = this.axisMax();
    const current = this.currentIndex();
    return this.weeks().map((w, i) => ({
      key: `${w.year}-${w.weekNumber}`,
      week: fill(this.text().weekShort, { week: w.weekNumber }),
      open: this.opens()[i],
      pct: (this.opens()[i] / max) * 100,
      current: i === current,
      rate: fill(this.text().matchShort, { rate: this.rate(w.matchRate) }),
    }));
  });

  protected readonly summary = computed(() => {
    const weeks = this.weeks();
    const index = this.currentIndex();
    const week = weeks[index];
    if (!week || week.totalCount === 0) return null;
    const head = fill(this.text().summary, {
      week: week.weekNumber,
      open: openCount(week),
      total: week.totalCount,
    });
    const parts: string[] = [];
    if (week.matchRate != null) {
      parts.push(fill(this.text().rate, { rate: this.rate(week.matchRate) }));
      const previous = previousRatedWeek(weeks, index);
      if (previous?.matchRate != null) {
        const diff = Math.round((week.matchRate - previous.matchRate) * 10) / 10;
        const template = diff > 0 ? this.text().up : diff < 0 ? this.text().down : this.text().same;
        parts.push(
          fill(template, {
            diff: formatNumber(this.locale(), Math.abs(diff), 1),
            week: previous.weekNumber,
          }),
        );
      }
    }
    return { head, detail: parts.join(' · ') };
  });

  protected readonly ariaSummary = computed(() =>
    [
      this.text().chartAria,
      ...this.weeks().map((w) =>
        fill(this.text().barAria, { week: w.weekNumber, open: openCount(w), total: w.totalCount }),
      ),
    ].join('. '),
  );

  private rate(value: number | null): string {
    return value == null ? '—' : `${formatNumber(this.locale(), value, 1)}%`;
  }
}
