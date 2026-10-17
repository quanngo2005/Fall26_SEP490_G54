import { CommonModule } from '@angular/common';
import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import {
  KpiAggregationType,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiDirection,
  KpiGroupOption,
} from '../../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';
import { UNIT_OPTIONS } from '../kpi-edit-modal/kpi-edit-modal.component';

@Component({
  selector: 'app-kpi-create-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './kpi-create-modal.component.html',
  styleUrls: ['../kpi-edit-modal/kpi-edit-modal.component.scss'],
})
export class KpiCreateModalComponent extends LocalizedComponent implements OnInit {
  private readonly kpiService = inject(KpiDefinitionService);

  readonly groups = input<KpiGroupOption[]>([]);
  readonly existingCodes = input<string[]>([]);

  readonly closed = output<void>();
  readonly created = output<KpiDefinition>();

  readonly unitOptions = UNIT_OPTIONS;

  // Form state
  readonly code = signal<string>('KPI-');
  readonly name = signal<string>('');
  readonly groupId = signal<string>('');
  readonly unit = signal<string>('VND');
  readonly direction = signal<KpiDirection>('HigherBetter');
  readonly aggregation = signal<KpiAggregationType>('Sum');
  readonly formula = signal<string>('');
  readonly description = signal<string>('');

  readonly saving = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.groupId.set(this.groups()[0]?.id ?? '');
  }

  close(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  submit(): void {
    const code = this.code().trim().toUpperCase();
    const name = this.name().trim();

    if (!code || code === 'KPI-') {
      this.error.set(this.localization.kpi.codeRequired);
      return;
    }
    if (!name) {
      this.error.set(this.localization.kpi.nameRequired);
      return;
    }
    if (this.existingCodes().some((c) => c.toUpperCase() === code)) {
      this.error.set(this.i18n.translate('kpi.codeExists', { code }));
      return;
    }

    const payload: KpiDefinitionSavePayload = {
      code,
      name,
      kpiGroupId: this.groupId() || (this.groups()[0]?.id ?? ''),
      unit: this.unit(),
      direction: this.direction(),
      aggregationType: this.aggregation(),
      formula: this.formula().trim() || null,
      description: this.description().trim() || null,
      maxAchievementPercent: 150,
    };

    this.error.set(null);
    this.saving.set(true);

    this.kpiService.createKpi(payload).subscribe({
      next: (res) => {
        this.saving.set(false);
        this.created.emit({
          ...res.data,
          kpiGroupName:
            this.groups().find((g) => g.id === payload.kpiGroupId)?.name ??
            this.localization.kpi.business,
          depositMultipliers: [],
          roleWeights: [
            { id: 'rw-c1', role: 'Teller', weight: 40 },
            { id: 'rw-c2', role: 'RBO', weight: 60 },
          ],
        });
      },
      error: () => {
        this.saving.set(false);
        this.error.set(this.localization.kpi.createFailed);
      },
    });
  }
}
