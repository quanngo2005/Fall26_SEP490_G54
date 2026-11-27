import { Component, computed } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';

/** Static legend for the 4 `AllocationCellComponent` states, shown above/beside an allocation table. */
@Component({
  selector: 'app-cell-legend',
  standalone: true,
  templateUrl: './cell-legend.component.html',
  styleUrl: './cell-legend.component.scss',
})
export class CellLegendComponent extends LocalizedComponent {
  protected readonly text = computed(() => this.localization.targetShared.cellLegend);
}
