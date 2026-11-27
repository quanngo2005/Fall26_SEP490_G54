import { Component, computed, input, output } from '@angular/core';
import { Tone } from './performance-display';

/**
 * Summary metric tile for performance dashboards. Follows the /evaluations metric-card format:
 * uppercase header with delta pill, 30px tabular-num values, no arrow button, and hover-lift on clickable cards.
 */
@Component({
  selector: 'app-perf-metric-card',
  template: `
    <article
  class="metric-card card metric"
  [class.metric-card-link]="clickable()"
  [class.is-clickable]="clickable()"
  [attr.role]="clickable() ? 'link' : null"
  [attr.tabindex]="clickable() ? 0 : null"
  [attr.aria-label]="clickable() ? label() : null"
  (click)="clickable() && activate.emit()"
  (keydown.enter)="clickable() && activate.emit()"
>
  <div class="metric-head">
    <span class="metric-title">{{label()}}</span>
    @if (delta()) {
      <span class="metric-pill font-mono" [class]="deltaPillClass()">{{delta()}}</span>
    }
  </div>
  <div class="metric-body">
    <span class="metric-value font-mono" [class]="valueToneClass()">{{value()}}</span>
  </div>
</article>
  `,
  styleUrls: ['../../shared/ui/metric-cards.scss', './performance-card.scss'],
  styles: `
    :host {
      display: block;
      height: 100%;
    }
    .metric-card {
      height: 100%;
      min-height: 96px;
      padding: 14px 18px;
    }
    .metric-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
      min-height: 22px;
    }
    .metric-title {
      font-family: 'Nunito Sans', sans-serif !important;
      font-size: 13px !important;
      font-weight: 800 !important;
      color: #002b49 !important;
      text-transform: uppercase;
      letter-spacing: 0.025em;
      line-height: 1.3;
    }
    .metric-pill {
      display: inline-flex;
      align-items: center;
      padding: 2px 8px;
      border-radius: 999px;
      font-family: 'Nunito Sans', sans-serif !important;
      font-size: 11.5px !important;
      font-weight: 700 !important;
      white-space: nowrap;
      background: #f1f5f9;
      border: 1px solid #94a3b8;
      color: #0f172a;
    }
    .pill-success {
      background: #dcfce7;
      border-color: #86efac;
      color: #065f46;
    }
    .pill-danger {
      background: #fee2e2;
      border-color: #fca5a5;
      color: #991b1b;
    }
    .pill-warning {
      background: #fef3c7;
      border-color: #fcd34d;
      color: #854d0e;
    }
    .pill-info {
      background: #dbeafe;
      border-color: #93c5fd;
      color: #1e40af;
    }
    .metric-body {
      display: flex;
      align-items: baseline;
      gap: 8px;
      margin: 8px 0 0;
      flex-wrap: wrap;
    }
    .metric-value {
      font-family: 'Nunito Sans', sans-serif !important;
      font-size: 30px !important;
      font-weight: 800 !important;
      color: #002b49 !important;
      line-height: 1.15;
      font-variant-numeric: tabular-nums;

      &.tone-good,
      &.up {
        color: #047857 !important;
      }
      &.tone-warn,
      &.warn {
        color: #b45309 !important;
      }
      &.tone-bad,
      &.down {
        color: #be123c !important;
      }
    }
    .metric-card-link,
    .card.is-clickable {
      cursor: pointer;
      transition:
        border-color 0.15s ease,
        box-shadow 0.15s ease,
        transform 0.15s ease;

      &:hover,
      &:focus-visible {
        border-color: #60a5fa;
        box-shadow: 0 4px 12px rgb(30 64 175 / 15%);
        transform: translateY(-2px);
        outline: none;
      }
    }
  `,
})
export class PerfMetricCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly valueTone = input<Tone | 'warn' | null>(null);
  readonly delta = input<string | null>(null);
  readonly deltaTone = input<Tone | 'warn' | null>(null);
  readonly clickable = input(false);
  readonly activate = output<void>();

  protected readonly deltaPillClass = computed(() => {
    const tone = this.deltaTone();
    if (tone === 'up') return 'pill-success';
    if (tone === 'down') return 'pill-danger';
    if (tone === 'warn') return 'pill-warning';
    return 'pill-info';
  });

  protected readonly valueToneClass = computed(() => {
    const tone = this.valueTone();
    if (tone === 'up') return 'tone-good up';
    if (tone === 'down') return 'tone-bad down';
    if (tone === 'warn') return 'tone-warn warn';
    return '';
  });
}
