import { Component, input, ViewEncapsulation } from '@angular/core';
import { NgClass } from '@angular/common';

/**
 * Summary card used on evaluation screens. The head shows a label and an optional pill;
 * the projected content fills the body. Styles come from the shared metric-card stylesheet.
 */
@Component({
  selector: 'app-metric-card',
  imports: [NgClass],
  template: `
    <article class="metric-card">
  <div class="metric-head">
    <span class="metric-title">{{label()}}</span>
    @if (pillText(); as text) {
      <span class="metric-pill font-mono" [ngClass]="pillClass()">{{text}}</span>
    }
  </div>
  <div class="metric-body">
    <ng-content />
  </div>
</article>
  `,
  styleUrls: ['./metric-cards.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class MetricCardComponent {
  readonly label = input.required<string>();
  readonly pillText = input<string | null>(null);
  readonly pillClass = input('pill-info');
}
