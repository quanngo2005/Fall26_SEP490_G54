import { Component, input, output } from '@angular/core';
import { LayoutIconComponent } from '../../layout/layout-icon.component';

/** The "Gợi ý" button every allocation screen uses: secondary button with the sparkle icon and a text label. */
@Component({
  selector: 'app-suggest-button',
  imports: [LayoutIconComponent],
  styleUrls: ['../../ui/ui-controls.scss'],
  styles: [
    ':host { display: inline-flex; } .button { gap: 6px; } app-layout-icon { width: 16px; height: 16px; }',
  ],
  template: `
    <button
  type="button"
  class="button button-secondary"
  [disabled]="disabled()"
  (click)="pressed.emit()"
>
  <app-layout-icon name="sparkle" />
  {{label()}}
</button>
  `,
})
export class SuggestButtonComponent {
  readonly label = input.required<string>();
  readonly disabled = input(false);
  readonly pressed = output<void>();
}
