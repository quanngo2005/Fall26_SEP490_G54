import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RankingBoardStaffRow, RankingTierCode } from '../../../../core/evaluation.models';
import { LocalizedComponent } from '../../../../core/i18n/localized.component';
import { StatusBadgeComponent } from '../../../../shared/ui';

export interface StaffTableRow {
  row: RankingBoardStaffRow;
  finalTier: RankingTierCode | null;
  reason: string;
  isChanged: boolean;
  isReasonMissing: boolean;
  canAdjust: boolean;
}

export const TIER_CLASS: Record<RankingTierCode, string> = {
  A1: 'tier-a',
  A2: 'tier-a',
  A3: 'tier-a',
  B: 'tier-b',
  C: 'tier-c',
};

@Component({
  selector: 'app-department-ranking-staff-table',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  templateUrl: './department-ranking-staff-table.component.html',
  styleUrls: [
    '../../../../shared/ui/ui-controls.scss',
    '../../../../shared/ui/workspace-page.scss',
    '../department-ranking.component.scss',
    '../department-ranking.popup.scss',
    '../../evaluations.layout.scss',
  ],
})
export class DepartmentRankingStaffTableComponent extends LocalizedComponent {
  readonly rows = input.required<StaffTableRow[]>();
  readonly adjusting = input<boolean>(false);
  readonly editable = input<boolean>(false);

  readonly tierChange = output<{ row: RankingBoardStaffRow; tier: RankingTierCode }>();
  readonly reasonChange = output<{ row: RankingBoardStaffRow; reason: string }>();
  readonly openEmployee = output<RankingBoardStaffRow>();

  readonly tierOptions: RankingTierCode[] = ['A1', 'A2', 'A3', 'B', 'C'];

  get emptyValue(): string {
    return this.localization.evaluations.emptyValue;
  }

  formatNumber(value: number, maxDecimals?: number): string {
    return this.i18n.formatNumber(value, maxDecimals);
  }

  onTierSelected(row: RankingBoardStaffRow, event: Event): void {
    const tier = (event.target as HTMLSelectElement).value as RankingTierCode;
    this.tierChange.emit({ row, tier });
  }

  onReasonInput(row: RankingBoardStaffRow, event: Event): void {
    const reason = (event.target as HTMLInputElement).value;
    this.reasonChange.emit({ row, reason });
  }

  onTimeText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverOnTime
      .replace('{onTime}', String(row.onTimeCompletedCount))
      .replace('{total}', String(row.completedWithDeadlineCount));
  }

  criteriaTitleText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverCriteria
      .replace('{achieved}', String(row.achievedCriteria ?? this.emptyValue))
      .replace('{total}', String(row.totalCriteria));
  }

  riskTitleText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverRiskTitle.replace(
      '{count}',
      String(row.riskEvents.length),
    );
  }

  placePopup(event: MouseEvent, popup: HTMLElement): void {
    const anchor = (event.currentTarget as HTMLElement).getBoundingClientRect();
    popup.style.top = `${anchor.bottom + 6}px`;
    popup.style.left = `${anchor.right}px`;
    popup.style.transform = 'translateX(-100%)';
  }

  deltaTone(delta: number | null, higherIsBetter: boolean): 'good' | 'bad' | null {
    if (delta === null || delta === 0) return null;
    return delta > 0 === higherIsBetter ? 'good' : 'bad';
  }

  tierClass(tier: RankingTierCode | null): string {
    return tier ? TIER_CLASS[tier] : '';
  }
}
