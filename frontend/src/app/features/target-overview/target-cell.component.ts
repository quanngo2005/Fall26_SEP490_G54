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
}
