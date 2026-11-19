import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';

export interface MissingPanelItem {
  readonly kpiId: string;
  readonly kpiName: string;
  /** Departments that still have no saved row for the KPI. */
  readonly departments: readonly string[];
}

/** Step-3 "what is still missing" panel: the KPIs and departments blocking the publish, one click to jump to each. */
@Component({
  selector: 'app-missing-panel',
  standalone: true,
  templateUrl: './missing-panel.component.html',
  styleUrl: './missing-panel.component.scss',
})
export class MissingPanelComponent extends LocalizedComponent {
  readonly items = input<readonly MissingPanelItem[]>([]);
  /** Names of KPIs the server still blocks that are not explained by a missing department row. */
  readonly otherBlockers = input<readonly string[]>([]);
  readonly canFillZero = input(false);

  readonly selectKpi = output<string>();
  readonly fillZero = output<void>();

  protected readonly text = computed(() => this.localization.targetShared.missingPanel);
}
