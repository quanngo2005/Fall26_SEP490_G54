import { CommonModule } from '@angular/common';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiDirection,
  KpiGroupOption,
  KpiMeasurementType,
  MultiplierItem,
  RoleWeightItem,
} from '../../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';

export const PRODUCT_OPTIONS = [
  'Tiền gửi > 6 tháng',
  'Tiền gửi ≤ 6 tháng',
  'Không kỳ hạn',
  'Tiền gửi kỳ hạn dài',
  'Tiền gửi không kỳ hạn',
  'Vay thế chấp BĐS',
  'Vay tín chấp',
];

export const ROLE_OPTIONS = ['Teller', 'RBO', 'BackOffice', 'LoanOfficer', 'RelationshipMgr'];

export const UNIT_OPTIONS = ['VND', '%', 'Giao dịch', 'Hồ sơ', 'Điểm', 'Số lượng'];

const MULTIPLIER_STEP = 0.1;

@Component({
  selector: 'app-kpi-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './kpi-edit-modal.component.html',
  styleUrls: ['./kpi-edit-modal.component.scss'],
})
export class KpiEditModalComponent implements OnInit {
  private readonly kpiService = inject(KpiDefinitionService);

  readonly kpi = input.required<KpiDefinition>();
  readonly groups = input<KpiGroupOption[]>([]);

  readonly closed = output<void>();
  readonly saved = output<KpiDefinition>();

  readonly productOptions = PRODUCT_OPTIONS;
  readonly roleOptions = ROLE_OPTIONS;
  readonly unitOptions = UNIT_OPTIONS;

  // Form state
  readonly groupId = signal<string>('');
  readonly name = signal<string>('');
  readonly description = signal<string>('');
  readonly measurementType = signal<KpiMeasurementType>('Quantitative');
  readonly unit = signal<string>('VND');
  readonly direction = signal<KpiDirection>('HigherBetter');
  readonly formula = signal<string>('');
  readonly applyMultiplier = signal<boolean>(true);
  readonly multipliers = signal<MultiplierItem[]>([]);
  readonly roleWeights = signal<RoleWeightItem[]>([]);

  readonly roleDropdownOpen = signal<boolean>(false);
  readonly saving = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  readonly totalWeight = computed(() =>
    this.roleWeights().reduce((acc, r) => acc + (Number(r.weight) || 0), 0),
  );

  readonly isWeightValid = computed(
    () => this.roleWeights().length === 0 || Math.abs(this.totalWeight() - 100) < 0.001,
  );

  readonly availableRoles = computed(() => {
    const selected = new Set(this.roleWeights().map((r) => r.role));
    return this.roleOptions.filter((r) => !selected.has(r));
  });

  readonly versionLabel = computed(() => this.kpi().version ?? 'v1.0');

  readonly effectiveDateLabel = computed(() => {
    const raw = this.kpi().effectiveDate;
    if (!raw) {
      return '01/10/2026';
    }
    const d = new Date(raw);
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`;
  });

  ngOnInit(): void {
    const k = this.kpi();
    this.groupId.set(k.kpiGroupId);
    this.name.set(k.name);
    this.description.set(k.description ?? '');
    this.measurementType.set(k.measurementType ?? 'Quantitative');
    this.unit.set(k.unit);
    this.direction.set(k.direction);
    this.formula.set(k.formula ?? '');
    this.applyMultiplier.set(k.applyProductMultiplier ?? (k.depositMultipliers?.length ?? 0) > 0);
    this.multipliers.set((k.depositMultipliers ?? []).map((m) => ({ ...m })));
    this.roleWeights.set((k.roleWeights ?? []).map((r) => ({ ...r })));
  }

  // Product multipliers (BR-33)
  addMultiplier(): void {
    const used = new Set(this.multipliers().map((m) => m.label));
    const label = this.productOptions.find((p) => !used.has(p)) ?? this.productOptions[0];
    this.multipliers.set([...this.multipliers(), { id: 'm-' + Date.now(), label, value: 1.0 }]);
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

  // Role weights (BR-06, 49)
  toggleRoleDropdown(): void {
    this.roleDropdownOpen.set(!this.roleDropdownOpen());
  }

  addRole(role: string): void {
    this.roleWeights.set([...this.roleWeights(), { id: 'rw-' + Date.now(), role, weight: 0 }]);
    this.roleDropdownOpen.set(false);
  }

  removeRole(role: string): void {
    this.roleWeights.set(this.roleWeights().filter((r) => r.role !== role));
  }

  updateRoleWeight(index: number, value: number): void {
    this.roleWeights.set(
      this.roleWeights().map((r, i) => (i === index ? { ...r, weight: Number(value) || 0 } : r)),
    );
  }

  close(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  save(): void {
    const k = this.kpi();
    if (!this.name().trim()) {
      this.error.set('Tên KPI không được để trống.');
      return;
    }
    if (!this.isWeightValid()) {
      this.error.set(
        `Tổng trọng số vai trò hiện là ${this.totalWeight()}%. Bắt buộc phải bằng 100% (BR-06, 49).`,
      );
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
      unit: this.unit(),
      aggregationType: k.aggregationType,
      direction: this.direction(),
      maxAchievementPercent: k.maxAchievementPercent,
    };

    this.kpiService.updateKpi(k.id, payload).subscribe({
      next: (res) => {
        this.saving.set(false);
        this.saved.emit({
          ...res.data,
          kpiGroupName: this.groups().find((g) => g.id === this.groupId())?.name ?? k.kpiGroupName,
          measurementType: this.measurementType(),
          applyProductMultiplier: this.applyMultiplier(),
          depositMultipliers: this.applyMultiplier() ? [...this.multipliers()] : [],
          roleWeights: [...this.roleWeights()],
        });
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Lưu cấu hình thất bại. Vui lòng thử lại.');
      },
    });
  }
}
