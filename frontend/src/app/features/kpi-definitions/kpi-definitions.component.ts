import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  KpiAggregationType,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiDirection,
  KpiGroupOption,
  MultiplierItem,
  RoleWeightItem,
} from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { KpiCreateModalComponent } from './kpi-create-modal/kpi-create-modal.component';
import { KpiEditModalComponent } from './kpi-edit-modal/kpi-edit-modal.component';

@Component({
  selector: 'app-kpi-definitions',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    KpiCreateModalComponent,
    KpiEditModalComponent,
    HeaderComponent,
    SidebarComponent,
  ],
  templateUrl: './kpi-definitions.component.html',
  styleUrls: ['./kpi-definitions.component.scss'],
})
export class KpiDefinitionsComponent extends LocalizedComponent implements OnInit {
  private readonly kpiService = inject(KpiDefinitionService);

  // Data signals
  readonly loading = signal<boolean>(false);
  readonly saving = signal<boolean>(false);
  readonly kpis = signal<KpiDefinition[]>([]);
  readonly groups = signal<KpiGroupOption[]>([]);
  readonly selectedKpi = signal<KpiDefinition | null>(null);

  // Search & Filter
  readonly searchTerm = signal<string>('');
  readonly groupFilter = signal<string>('all');

  // Form editing state
  readonly editCode = signal<string>('');
  readonly editName = signal<string>('');
  readonly editGroupId = signal<string>('');
  readonly editDescription = signal<string>('');
  readonly editUnit = signal<string>('VND');
  readonly editDirection = signal<KpiDirection>('HigherBetter');
  readonly editAggregationType = signal<KpiAggregationType>('Sum');
  readonly editFormula = signal<string>('');
  readonly depositMultipliers = signal<MultiplierItem[]>([]);
  readonly roleWeights = signal<RoleWeightItem[]>([]);

  // Create Modal state
  readonly isCreateModalOpen = signal<boolean>(false);

  // Edit Modal state
  readonly isEditModalOpen = signal<boolean>(false);

  // Toast notification
  readonly toast = signal<{ type: 'success' | 'error' | 'info'; message: string } | null>(null);

  // Computed state
  readonly filteredKpis = computed(() => {
    const list = this.kpis();
    const query = this.searchTerm().trim().toLowerCase();
    const grp = this.groupFilter();

    return list.filter((item) => {
      const matchQuery =
        !query ||
        item.code.toLowerCase().includes(query) ||
        item.name.toLowerCase().includes(query) ||
        (item.description && item.description.toLowerCase().includes(query));

      const matchGroup = grp === 'all' || item.kpiGroupId === grp || item.kpiGroupName === grp;

      return matchQuery && matchGroup;
    });
  });

  readonly existingCodes = computed(() => this.kpis().map((k) => k.code));

  readonly totalRoleWeight = computed(() => {
    return this.roleWeights().reduce((acc, curr) => acc + (Number(curr.weight) || 0), 0);
  });

  readonly isWeightValid = computed(() => {
    const weights = this.roleWeights();
    if (weights.length === 0) {
      return true;
    }
    return Math.abs(this.totalRoleWeight() - 100) < 0.001;
  });

  readonly isDirty = computed(() => {
    const sel = this.selectedKpi();
    if (!sel) {
      return false;
    }
    return (
      this.editName() !== sel.name ||
      this.editDescription() !== (sel.description ?? '') ||
      this.editGroupId() !== sel.kpiGroupId ||
      this.editUnit() !== sel.unit ||
      this.editDirection() !== sel.direction ||
      this.editAggregationType() !== sel.aggregationType ||
      this.editFormula() !== (sel.formula ?? '')
    );
  });

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    this.kpiService.getGroups().subscribe({
      next: (groupRes) => this.groups.set(groupRes.data),
      error: () => this.showToast('error', this.localization.kpi.loadFailed),
    });

    this.kpiService.getKpis().subscribe({
      next: (res) => {
        const items = res.data.items ?? [];
        this.kpis.set(items);
        if (items.length > 0 && !this.selectedKpi()) {
          this.selectKpi(items[0]);
        }
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.showToast('error', this.localization.kpi.loadFailed);
      },
    });
  }

  selectKpi(kpi: KpiDefinition): void {
    this.selectedKpi.set(kpi);
    this.editCode.set(kpi.code);
    this.editName.set(kpi.name);
    this.editGroupId.set(kpi.kpiGroupId);
    this.editDescription.set(kpi.description ?? '');
    this.editUnit.set(kpi.unit);
    this.editDirection.set(kpi.direction);
    this.editAggregationType.set(kpi.aggregationType);
    this.editFormula.set(kpi.formula ?? '');

    // Multipliers & Weights
    this.depositMultipliers.set(kpi.depositMultipliers ? [...kpi.depositMultipliers] : []);
    this.roleWeights.set(
      kpi.roleWeights && kpi.roleWeights.length > 0
        ? [...kpi.roleWeights]
        : [
            { id: 'rw-1', role: 'Teller', weight: 40 },
            { id: 'rw-2', role: 'RBO', weight: 60 },
          ],
    );
  }

  setDirection(direction: KpiDirection): void {
    this.editDirection.set(direction);
  }

  saveConfig(): void {
    const kpi = this.selectedKpi();
    if (!kpi) {
      return;
    }

    if (!this.editName().trim()) {
      this.showToast('error', this.localization.kpi.nameRequired);
      return;
    }

    if (!this.isWeightValid()) {
      this.showToast(
        'error',
        this.i18n.translate('kpi.weightError', { weight: this.totalRoleWeight() }),
      );
      return;
    }

    this.saving.set(true);
    const payload: KpiDefinitionSavePayload = {
      kpiGroupId: this.editGroupId(),
      code: this.editCode(),
      name: this.editName().trim(),
      description: this.editDescription().trim() || null,
      formula: this.editFormula().trim() || null,
      unit: this.editUnit().trim(),
      aggregationType: this.editAggregationType(),
      direction: this.editDirection(),
      maxAchievementPercent: kpi.maxAchievementPercent,
    };

    this.kpiService.updateKpi(kpi.id, payload).subscribe({
      next: (res) => {
        const updatedKpi: KpiDefinition = {
          ...res.data,
          kpiGroupName:
            this.groups().find((g) => g.id === this.editGroupId())?.name ?? kpi.kpiGroupName,
          depositMultipliers: [...this.depositMultipliers()],
          roleWeights: [...this.roleWeights()],
        };

        // Update in list
        const updatedList = this.kpis().map((item) => (item.id === kpi.id ? updatedKpi : item));
        this.kpis.set(updatedList);
        this.selectedKpi.set(updatedKpi);
        this.saving.set(false);
        this.showToast('success', this.localization.kpi.saved);
      },
      error: () => {
        this.saving.set(false);
        this.showToast('error', this.localization.kpi.saveFailed);
      },
    });
  }

  cancelChanges(): void {
    const kpi = this.selectedKpi();
    if (kpi) {
      this.selectKpi(kpi);
      this.showToast('info', this.localization.kpi.reverted);
    }
  }

  openCreateModal(): void {
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  onKpiCreated(created: KpiDefinition): void {
    this.kpis.set([created, ...this.kpis()]);
    this.selectKpi(created);
    this.isCreateModalOpen.set(false);
    this.showToast('success', this.i18n.translate('kpi.created', { code: created.code }));
  }

  openEditModal(): void {
    if (this.selectedKpi()) {
      this.isEditModalOpen.set(true);
    }
  }

  closeEditModal(): void {
    this.isEditModalOpen.set(false);
  }

  onKpiEdited(updated: KpiDefinition): void {
    this.kpis.set(this.kpis().map((item) => (item.id === updated.id ? updated : item)));
    this.selectKpi(updated);
    this.isEditModalOpen.set(false);
    this.showToast('success', this.i18n.translate('kpi.published', { code: updated.code }));
  }

  addMultiplier(): void {
    const current = [...this.depositMultipliers()];
    current.push({
      id: 'm-' + Date.now(),
      label: this.localization.kpi.newDepositType,
      value: 1.0,
    });
    this.depositMultipliers.set(current);
  }

  removeMultiplier(index: number): void {
    const current = [...this.depositMultipliers()];
    current.splice(index, 1);
    this.depositMultipliers.set(current);
  }

  updateMultiplierValue(index: number, val: number): void {
    const current = [...this.depositMultipliers()];
    current[index] = { ...current[index], value: val };
    this.depositMultipliers.set(current);
  }

  addRoleWeight(): void {
    const current = [...this.roleWeights()];
    current.push({
      id: 'rw-' + Date.now(),
      role: this.localization.kpi.newRole,
      weight: 0,
    });
    this.roleWeights.set(current);
  }

  removeRoleWeight(index: number): void {
    const current = [...this.roleWeights()];
    current.splice(index, 1);
    this.roleWeights.set(current);
  }

  updateRoleWeightValue(index: number, val: number): void {
    const current = [...this.roleWeights()];
    current[index] = { ...current[index], weight: Number(val) || 0 };
    this.roleWeights.set(current);
  }

  private showToast(type: 'success' | 'error' | 'info', message: string): void {
    this.toast.set({ type, message });
    setTimeout(() => {
      this.toast.set(null);
    }, 4000);
  }
}
