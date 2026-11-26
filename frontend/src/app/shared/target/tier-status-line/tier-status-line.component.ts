import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { TierStatusCounts, TierStatusFilter } from '../target-shared.models';

/** "<tier label> · N / M KPI matched · x mismatched · y not split" plus All/Mismatched/Not split filter chips. */
@Component({
  selector: 'app-tier-status-line',
  standalone: true,
  templateUrl: './tier-status-line.component.html',
  styleUrl: './tier-status-line.component.scss',
})
export class TierStatusLineComponent extends LocalizedComponent {
  readonly tierLabel = input.required<string>();
  readonly counts = input.required<TierStatusCounts>();
  readonly activeFilter = input<TierStatusFilter>('ALL');

  readonly filterChange = output<TierStatusFilter>();

  protected readonly text = computed(() => this.localization.targetShared.tierStatusLine);

  protected setFilter(filter: TierStatusFilter): void {
    this.filterChange.emit(filter);
  }
}
