import { Component, input } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { AppNumberPipe } from '../../shared/pipes';
import { TargetCell } from './target-overview.utils';

/** One target value: a formatted number, "—" when not applicable, or a "not allocated" marker. */
@Component({
  selector: 'app-target-cell',
  imports: [AppNumberPipe],
  templateUrl: './target-cell.component.html',
  styles: `
    .muted {
      color: #94a3b8;
    }
    .unallocated {
      color: #b45309;
      font-weight: 600;
    }
  `,
})
export class TargetCellComponent extends LocalizedComponent {
  readonly value = input.required<TargetCell>();
  /** The number is shown divided by this (1e9 for tỷ VND, 0.01 for a fraction shown as %). */
  readonly divisor = input(1);

  /** The value in the column's unit, rounded to 2 decimals (also clears float noise such as 0.4 / 0.01). */
  protected shown(value: number): number {
    return Math.round((value / this.divisor()) * 100) / 100;
  }
}
