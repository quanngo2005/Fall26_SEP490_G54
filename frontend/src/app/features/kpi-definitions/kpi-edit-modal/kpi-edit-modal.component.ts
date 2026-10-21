import { CommonModule } from '@angular/common';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import {
  KPI_AGGREGATIONS,
  KPI_DIRECTIONS,
  KpiAggregationType,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiDirection,
  KpiGroupOption,
  KpiMeasurementType,
  MultiplierItem,
  RoleWeightItem,
} from '../../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';

const MULTIPLIER_STEP = 0.1;

@Component({
  selector: 'app-kpi-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './kpi-edit-modal.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/modal.scss',
    './kpi-edit-modal.component.scss',
  ],
})
export class KpiEditModalComponent extends LocalizedComponent implements OnInit {
  private readonly kpiService = inject(KpiDefinitionService);

  readonly kpi = input.required<KpiDefinition>();
  readonly groups = input<KpiGroupOption[]>([]);
  readonly units = input<string[]>([]);

  readonly closed = output<void>();
  readonly saved = output<KpiDefinition>();

  readonly directions = KPI_DIRECTIONS;
  readonly aggregations = KPI_AGGREGATIONS;

  // Form state
  readonly groupId = signal<string>('');
  readonly name = signal<string>('');
  readonly description = signal<string>('');
  readonly measurementType = signal<KpiMeasurementType>('Quantitative');
  readonly unit = signal<string>('');
  readonly direction = signal<KpiDirection>('HIGHER_BETTER');
  readonly aggregationType = signal<KpiAggregationType>('SUM');
  readonly formula = signal<string>('');
  readonly maxAchievementPercent = signal<number | null>(null);
  readonly applyMultiplier = signal<boolean>(false);
  readonly multipliers = signal<MultiplierItem[]>([]);
  readonly roleWeights = signal<RoleWeightItem[]>([]);

  readonly saving = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  readonly totalWeight = computed(() =>
    this.roleWeights().reduce((acc, r) => acc + (Number(r.weight) || 0), 0),
  );

  readonly isWeightValid = computed(
    () => this.roleWeights().length === 0 || Math.abs(this.totalWeight() - 100) < 0.001,
  );

  readonly versionLabel = computed(() => this.kpi().version ?? '');

  readonly effectiveDateLabel = computed(() => {
    const raw = this.kpi().effectiveDate ?? this.kpi().updatedAt;
    if (!raw) return '';
    return this.i18n.formatDate(raw, 'DD/MM/YYYY');
  });

  ngOnInit(): void {
    const k = this.kpi();
    this.groupId.set(k.kpiGroupId);
    this.name.set(k.name);
    this.description.set(k.description ?? '');
    this.measurementType.set(k.measurementType ?? 'Quantitative');
    this.unit.set(k.unit);
    this.direction.set(k.direction);
    this.aggregationType.set(k.aggregationType);
    this.formula.set(k.formula ?? '');
    this.maxAchievementPercent.set(k.maxAchievementPercent);
    this.applyMultiplier.set(k.applyProductMultiplier ?? (k.depositMultipliers?.length ?? 0) > 0);
    this.multipliers.set((k.depositMultipliers ?? []).map((m) => ({ ...m })));
    this.roleWeights.set((k.roleWeights ?? []).map((r) => ({ ...r })));
  }

  // Product multipliers
  addMultiplier(): void {
    this.multipliers.set([...this.multipliers(), { id: 'm-' + Date.now(), label: '', value: 1.0 }]);
  }

  removeMultiplier(index: number): void {
    this.multipliers.set(this.multipliers().filter((_, i) => i !== index));
  }

  updateMultiplierLabel(index: number, label: string): void {
    this.multipliers.set(this.multipliers().map((m, i) => (i === index ? { ...m, label } : m)));
  }

  updateMultiplierValue(index: number, value: number): void {
    const v = Math.max(0, Math.round((Number(value) || 0) * 10) / 10);
    this.multipliers.set(this.multipliers().map((m, i) => (i === index ? { ...m, value: v } : m)));
  }

  stepMultiplier(index: number, delta: number): void {
    this.updateMultiplierValue(index, this.multipliers()[index].value + delta * MULTIPLIER_STEP);
  }

  // Role weights
  addRole(): void {
    this.roleWeights.set([...this.roleWeights(), { id: 'rw-' + Date.now(), role: '', weight: 0 }]);
  }

  removeRole(index: number): void {
    this.roleWeights.set(this.roleWeights().filter((_, i) => i !== index));
  }

  updateRoleName(index: number, role: string): void {
    this.roleWeights.set(this.roleWeights().map((r, i) => (i === index ? { ...r, role } : r)));
  }

  updateRoleWeight(index: number, value: number): void {
    this.roleWeights.set(
      this.roleWeights().map((r, i) => (i === index ? { ...r, weight: Number(value) || 0 } : r)),
    );
  }

  directionLabel(direction: string): string {
    const text = this.localization.kpi;
    return direction === 'HIGHER_BETTER'
      ? text.higherBetter
      : direction === 'LOWER_BETTER'
        ? text.lowerBetter
        : text.onTarget;
  }

  aggregationLabel(aggregation: string): string {
    const text = this.localization.kpi;
    switch (aggregation) {
      case 'SUM':
        return text.sum;
      case 'RATIO':
        return text.ratio;
      case 'AVG':
        return text.average;
      default:
        return text.noAggregation;
    }
  }

  close(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  save(): void {
    const k = this.kpi();
    if (!this.name().trim()) {
      this.error.set(this.localization.kpi.nameRequired);
      return;
    }
    if (!this.unit().trim()) {
      this.error.set(this.localization.kpi.unitRequired);
      return;
    }
    if (!this.isWeightValid()) {
      this.error.set(this.i18n.translate('kpi.weightError', { weight: this.totalWeight() }));
      return;
    }

    this.error.set(null);
    this.saving.set(true);

    const payload: KpiDefinitionSavePayload = {
      kpiGroupId: this.groupId(),
      code: k.code,
      name: this.name().trim(),
      description: this.description().trim() || null,
      formula: this.formula().trim() || null,
      unit: this.unit().trim(),
      aggregationType: this.aggregationType(),
      direction: this.direction(),
      maxAchievementPercent: this.maxAchievementPercent(),
    };

    this.kpiService.update(k.id, payload).subscribe({
      next: (res: KpiDefinition) => {
        this.saving.set(false);
        this.saved.emit({
          ...res,
          kpiGroupName: this.groups().find((g) => g.id === this.groupId())?.name ?? k.kpiGroupName,
          measurementType: this.measurementType(),
          applyProductMultiplier: this.applyMultiplier(),
          depositMultipliers: this.applyMultiplier() ? [...this.multipliers()] : [],
          roleWeights: [...this.roleWeights()],
        });
      },
      error: () => {
        this.saving.set(false);
        this.error.set(this.localization.kpi.saveFailed);
      },
    });
  }
}
