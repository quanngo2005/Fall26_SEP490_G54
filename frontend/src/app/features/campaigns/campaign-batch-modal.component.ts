import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ALLOCATION_RULES,
  AllocationRule,
  BatchWorkResult,
  CampaignDetail,
} from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { serverMessage } from './campaign.utils';

const MAX_WORKS = 1000;

@Component({
  selector: 'app-campaign-batch-modal',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './campaign-batch-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './campaign-modal.scss',
  ],
})
export class CampaignBatchModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(CampaignService);

  readonly campaign = input.required<CampaignDetail>();
  readonly closed = output<void>();
  readonly generated = output<BatchWorkResult>();

  protected readonly rules = ALLOCATION_RULES;
  protected readonly maxWorks = MAX_WORKS;
  protected readonly selectedIds = signal<string[]>([]);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selectedCount = computed(() => this.selectedIds().length);

  protected workTypeId = '';
  protected leadPoolSource = '';
  protected totalLeadsCount = 1;
  protected allocationRule: AllocationRule = 'ROUND_ROBIN';
  protected plannedEndDate = '';
  protected targetValue: number | null = null;

  ngOnInit(): void {
    const campaign = this.campaign();
    this.workTypeId = campaign.workTypes[0]?.workTypeId ?? '';
    this.plannedEndDate = campaign.endDate;
    this.selectedIds.set(campaign.participants.map((participant) => participant.employeeId));
  }

  /** Unit of the selected work type's KPI, so the expected result adapts to the work type. */
  protected get targetUnit(): string {
    return (
      this.campaign().workTypes.find((type) => type.workTypeId === this.workTypeId)?.targetUnit ??
      ''
    );
  }

  protected get averagePerPerson(): number | null {
    return this.targetValue !== null && this.selectedCount() > 0
      ? this.targetValue / this.selectedCount()
      : null;
  }

  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected ruleLabel(rule: AllocationRule): string {
    return this.localization.campaign.batch.rules[rule];
  }

  protected isSelected(employeeId: string): boolean {
    return this.selectedIds().includes(employeeId);
  }

  protected toggle(employeeId: string): void {
    this.selectedIds.update((current) =>
      current.includes(employeeId)
        ? current.filter((id) => id !== employeeId)
        : [...current, employeeId],
    );
  }

  protected submit(): void {
    if (this.saving()) return;
    if (!this.isValid()) {
      this.error.set(this.localization.campaign.batch.invalid);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api
      .batchGenerateWork(this.campaign().id, {
        workTypeId: this.workTypeId,
        leadPoolSource: this.leadPoolSource.trim(),
        allocationRule: this.allocationRule,
        totalLeadsCount: this.totalLeadsCount,
        plannedEndDate: this.plannedEndDate || null,
        targetEmployeeIds: this.selectedIds(),
        targetValue: this.targetValue,
      })
      .subscribe({
        next: (result) => this.generated.emit(result),
        error: (error: unknown) => {
          this.saving.set(false);
          this.error.set(serverMessage(error) ?? this.localization.campaign.genericError);
        },
      });
  }

  private isValid(): boolean {
    return (
      !!this.workTypeId &&
      !!this.leadPoolSource.trim() &&
      Number.isInteger(this.totalLeadsCount) &&
      this.totalLeadsCount >= 1 &&
      this.totalLeadsCount <= MAX_WORKS &&
      this.selectedCount() > 0 &&
      (this.targetValue === null || (Number.isFinite(this.targetValue) && this.targetValue >= 0))
    );
  }
}
