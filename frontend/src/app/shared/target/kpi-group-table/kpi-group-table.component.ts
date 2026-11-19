import { Component, computed, input, TemplateRef } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { KpiGroupTableGroup, KpiGroupTableRow } from '../target-shared.models';

export interface KpiGroupRowContext {
  $implicit: KpiGroupTableRow;
  group: KpiGroupTableGroup;
}

/**
 * Grouped KPI table (the 7 fixed KPI groups): complete groups start collapsed, groups with a problem start
 * open. The KPI name/type columns are built in; extra per-row columns (value cells, status, …) are supplied
 * by the host through `rowTemplate`, e.g.:
 * `<ng-template #cols let-row let-group="group"><td>{{ row.value }}</td></ng-template>`
 * `<app-kpi-group-table [groups]="groups" [rowTemplate]="cols" />`
 */
@Component({
  selector: 'app-kpi-group-table',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './kpi-group-table.component.html',
  styleUrl: './kpi-group-table.component.scss',
})
export class KpiGroupTableComponent extends LocalizedComponent {
  readonly groups = input.required<readonly KpiGroupTableGroup[]>();
  readonly rowTemplate = input<TemplateRef<KpiGroupRowContext> | null>(null);
  /** Number of columns `rowTemplate` renders, so the empty-group row can span the full width. */
  readonly extraColumnCount = input(0);

  protected readonly text = computed(() => this.localization.targetShared.kpiGroupTable);

  private readonly expandedOverrides = new Set<string>();

  protected isExpanded(group: KpiGroupTableGroup): boolean {
    const defaultOpen = !group.complete;
    return this.expandedOverrides.has(group.id) ? !defaultOpen : defaultOpen;
  }

  protected toggle(group: KpiGroupTableGroup): void {
    if (this.expandedOverrides.has(group.id)) {
      this.expandedOverrides.delete(group.id);
    } else {
      this.expandedOverrides.add(group.id);
    }
  }

  protected rowContext(row: KpiGroupTableRow, group: KpiGroupTableGroup): KpiGroupRowContext {
    return { $implicit: row, group };
  }
}
