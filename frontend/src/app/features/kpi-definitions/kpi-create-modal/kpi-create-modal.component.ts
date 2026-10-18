import { Component, DestroyRef, inject, input, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { apiErrorStatus } from '../../../core/api-utils';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import {
  KPI_AGGREGATIONS,
  KPI_DIRECTIONS,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiGroupOption,
} from '../../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';

@Component({
  selector: 'app-kpi-create-modal',
  imports: [FormsModule],
  templateUrl: './kpi-create-modal.component.html',
  styleUrls: ['../../../shared/ui/ui-controls.scss', '../../../shared/ui/modal.scss'],
})
export class KpiCreateModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(KpiDefinitionService);
  private readonly destroyRef = inject(DestroyRef);

  readonly groups = input<KpiGroupOption[]>([]);
  readonly units = input<string[]>([]);
  readonly closed = output<void>();
  readonly created = output<KpiDefinition>();

  protected readonly directions = KPI_DIRECTIONS;
  protected readonly aggregations = KPI_AGGREGATIONS;
  protected readonly saving = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected code = '';
  protected name = '';
  protected kpiGroupId = '';
  protected unit = '';
  protected direction: KpiDefinition['direction'] = 'HIGHER_BETTER';
  protected aggregationType: KpiDefinition['aggregationType'] = 'SUM';
  protected formula = '';
  protected description = '';
  protected maxAchievementPercent: number | string | null = null;

  ngOnInit(): void {
    this.kpiGroupId = this.groups()[0]?.id ?? '';
  }

  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected submit(): void {
    if (this.saving()) return;
    const code = this.code.trim().toUpperCase();
    if (!code) return this.fail('kpi.codeRequired');
    if (!this.name.trim()) return this.fail('kpi.nameRequired');
    if (!this.kpiGroupId) return this.fail('kpi.groupRequired');
    if (!this.unit.trim()) return this.fail('kpi.unitRequired');

    const max =
      this.maxAchievementPercent === null || this.maxAchievementPercent === ''
        ? null
        : Number(this.maxAchievementPercent);
    const payload: KpiDefinitionSavePayload = {
      kpiGroupId: this.kpiGroupId,
      code,
      name: this.name.trim(),
      description: this.description.trim() || null,
      formula: this.formula.trim() || null,
      unit: this.unit.trim(),
      aggregationType: this.aggregationType,
      direction: this.direction,
      maxAchievementPercent: max !== null && Number.isFinite(max) ? max : null,
    };
    this.errorKey.set(null);
    this.saving.set(true);
    this.api
      .create(payload)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (kpi) => {
          this.saving.set(false);
          this.created.emit(kpi);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorKey.set(apiErrorStatus(error) === 409 ? 'kpi.codeExists' : 'kpi.createFailed');
        },
      });
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key, { code: this.code.trim().toUpperCase() }) : '';
  }

  protected directionLabel(direction: string): string {
    const text = this.localization.kpi;
    return direction === 'HIGHER_BETTER'
      ? text.higherBetter
      : direction === 'LOWER_BETTER'
        ? text.lowerBetter
        : text.onTarget;
  }

  protected aggregationLabel(aggregation: string): string {
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

  private fail(key: string): void {
    this.errorKey.set(key);
  }
}
