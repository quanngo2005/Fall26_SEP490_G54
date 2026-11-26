import { CommonModule } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { LayoutIconComponent } from '../../../../shared/layout/layout-icon.component';

export interface RankingBoardTierRow {
  tier: string;
  share: number | null;
  headcount: number | null;
  actualCount: number;
  isFilled: boolean;
  periodQuotaMax?: number | null;
  isOverPeriodQuota?: boolean;
  isFlex?: boolean;
}

@Component({
  selector: 'app-department-ranking-tier-table',
  standalone: true,
  imports: [CommonModule, LayoutIconComponent],
  templateUrl: './department-ranking-tier-table.component.html',
  styleUrls: ['./department-ranking-tier-table.component.scss'],
})
export class DepartmentRankingTierTableComponent {
  readonly title = input<string>('Bảng phân bổ chỉ tiêu (Slot Quota)');
  readonly tierTableRows = input.required<RankingBoardTierRow[]>();
  readonly totalSlots = input<number | string>('-');
  readonly totalActual = input<number | string>('-');
  readonly allSlotsFilled = input<boolean>(false);
  readonly sendBlockers = input<string[]>([]);
  readonly canSend = input<boolean>(true);
  readonly showBlockers = input<boolean>(true);
  readonly emptyValue = input<string>('-');
  readonly editable = input<boolean>(false);

  readonly headcountChange = output<{ tier: string; headcount: number | null }>();

  formatNumber(val: number | null, decimals = 0): string {
    if (val === null || val === undefined) return this.emptyValue();
    return val.toLocaleString('vi-VN', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  }

  onHeadcountInput(tier: string, event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    const num = raw === '' ? null : Math.max(0, parseInt(raw, 10) || 0);
    this.headcountChange.emit({ tier, headcount: num });
  }

  tierClass(tier: string): string {
    const code = tier.toUpperCase();
    if (code.startsWith('A')) return 'tier-a';
    if (code.startsWith('B')) return 'tier-b';
    return 'tier-c';
  }
}
