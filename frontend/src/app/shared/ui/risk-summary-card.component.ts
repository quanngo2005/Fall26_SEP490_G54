import { Component, input, ViewEncapsulation } from '@angular/core';
import { AppNumberPipe } from '../pipes';
import type { EvaluationRiskEvent } from '../../core/evaluation.models';

/**
 * Side card listing the risk events of an evaluation, with the total penalty row.
 * Pass an empty `events` list to show only the total.
 */
@Component({
  selector: 'app-risk-summary-card',
  imports: [AppNumberPipe],
  template: `
    <section class="side-card">
  <div class="side-card-head">
    <h2 class="side-card-title">{{title()}}</h2>
    @if (countText(); as text) {
      <span class="metric-pill pill-danger font-mono">{{text}}</span>
    }
  </div>
  @for (event of events(); track event.id) {
    <div class="risk-row">
      <div>
        <p class="risk-name">{{event.description}}</p>
        <p class="risk-meta">{{event.code}} · {{event.severity}}</p>
      </div>
      <span class="risk-points font-mono">-{{event.penaltyPoints | appNumber: 2}}</span>
    </div>
  }
  <div class="risk-total">
    <span>{{totalLabel()}}</span>
    <span class="risk-points font-mono">-{{totalPenalty() | appNumber: 2}}</span>
  </div>
</section>
  `,
  styleUrls: ['./side-cards.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class RiskSummaryCardComponent {
  readonly title = input.required<string>();
  readonly countText = input<string | null>(null);
  readonly events = input<readonly EvaluationRiskEvent[]>([]);
  readonly totalLabel = input.required<string>();
  readonly totalPenalty = input.required<number>();
}
