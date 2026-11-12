import { Component, input, output } from '@angular/core';
import { Tone } from './performance-display';

/**
 * One of the four summary tiles. A tile with `clickable` gets the blue frame and the › button, behaves like a link
 * (Enter / click) and emits `activate`; the page decides where it goes.
 */
@Component({
  selector: 'app-perf-metric-card',
  template: `
    <article
  class="card metric"
  [class.is-clickable]="clickable()"
  [attr.role]="clickable() ? 'link' : null"
  [attr.tabindex]="clickable() ? 0 : null"
  [attr.aria-label]="clickable() ? label() : null"
  (click)="clickable() && activate.emit()"
  (keydown.enter)="clickable() && activate.emit()"
>
  <div class="metric-head">
    <span class="metric-label">{{label()}}</span>
    @if (clickable()) {
      <span class="chevron" aria-hidden="true">›</span>
    }
  </div>
  <div class="metric-body">
    <strong class="metric-value mono" [class]="valueTone() ?? ''">{{value()}}</strong>
    @if (delta()) {
      <span class="metric-delta" [class]="deltaTone() ?? ''">{{delta()}}</span>
    }
  </div>
</article>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .metric {
      padding: 16px 20px;
    }
    .metric.is-clickable {
      cursor: pointer;
    }
    .metric.is-clickable:focus-visible {
      outline: 2px solid #2563eb;
      outline-offset: 2px;
    }
    .metric-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      min-height: 26px;
      color: #475569;
      font-size: 13px;
    }
    .metric-body {
      display: flex;
      align-items: baseline;
      gap: 12px;
      margin-top: 10px;
    }
    .metric-value {
      color: #0f172a;
      font-size: 26px;
      font-weight: 700;
    }
    .metric-delta {
      font-size: 12px;
      font-weight: 600;
    }
  `,
})
export class PerfMetricCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly valueTone = input<Tone | 'warn' | null>(null);
  readonly delta = input<string | null>(null);
  readonly deltaTone = input<Tone | null>(null);
  readonly clickable = input(false);
  readonly activate = output<void>();
}
