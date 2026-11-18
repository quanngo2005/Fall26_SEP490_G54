import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';

export type RankingTier = 'A1' | 'A2' | 'A3' | 'B' | 'C';

export interface RankingMetric {
  value: string;
  delta?: string;
  tone?: 'good' | 'bad';
}

export interface RankingStaffRow {
  stt: number;
  name: string;
  code: string;
  sla: RankingMetric;
  target: RankingMetric;
  risk: RankingMetric;
  penalty: RankingMetric;
  score: RankingMetric;
  systemTier: RankingTier;
  finalTier: RankingTier;
  reason: string | null;
  reasonRequired?: boolean;
  tierChanged?: boolean;
}

export interface RankingTierRow {
  tier: RankingTier;
  share: string;
  slots: number;
  actual: number;
}

const TIER_CLASS: Record<RankingTier, string> = {
  A1: 'tier-a',
  A2: 'tier-a',
  A3: 'tier-a',
  B: 'tier-b',
  C: 'tier-c',
};

@Component({
  selector: 'app-department-ranking',
  standalone: true,
  imports: [CommonModule, HeaderComponent, SidebarComponent, LayoutIconComponent],
  templateUrl: './department-ranking.component.html',
  styleUrls: ['./department-ranking.component.scss', '../evaluations.layout.scss'],
})
export class DepartmentRankingComponent extends LocalizedComponent {
  readonly lockReached = true;

  readonly tierRows: RankingTierRow[] = [
    { tier: 'A1', share: '5%', slots: 1, actual: 2 },
    { tier: 'A2', share: '5%', slots: 1, actual: 1 },
    { tier: 'A3', share: '10%', slots: 2, actual: 1 },
    { tier: 'B', share: '40%', slots: 8, actual: 8 },
    { tier: 'C', share: '40%', slots: 8, actual: 8 },
  ];

  readonly staffRows: RankingStaffRow[] = [
    {
      stt: 1,
      name: 'Nguyễn Văn An',
      code: 'NV001',
      sla: { value: '98%', delta: '▲ 2%', tone: 'good' },
      target: { value: '4/5' },
      risk: { value: '0' },
      penalty: { value: '0.0' },
      score: { value: '94.5', delta: '▲ 2.5' },
      systemTier: 'A1',
      finalTier: 'A1',
      reason: null,
    },
    {
      stt: 2,
      name: 'Trần Thị Bình',
      code: 'NV002',
      sla: { value: '96%', delta: '▼ 1%', tone: 'bad' },
      target: { value: '4/5', delta: '▲ 1', tone: 'good' },
      risk: { value: '0' },
      penalty: { value: '0.0' },
      score: { value: '91.2', delta: '▼ 1.4' },
      systemTier: 'A2',
      finalTier: 'A2',
      reason: null,
    },
    {
      stt: 3,
      name: 'Lê Minh Cường',
      code: 'NV014',
      sla: { value: '95%', delta: '▲ 3%', tone: 'good' },
      target: { value: '3/5', delta: '▼ 1', tone: 'bad' },
      risk: { value: '1', delta: '▲ 1', tone: 'bad' },
      penalty: { value: '-2.0', delta: '▼ 1.5' },
      score: { value: '89.8', delta: '▲ 1.8' },
      systemTier: 'A2',
      finalTier: 'A1',
      reason: 'Dẫn dắt dự án Q3',
      tierChanged: true,
    },
    {
      stt: 4,
      name: 'Phạm Quốc Dũng',
      code: 'NV007',
      sla: { value: '93%', delta: '▼ 2%', tone: 'bad' },
      target: { value: '4/5' },
      risk: { value: '0', delta: '▼ 1', tone: 'good' },
      penalty: { value: '0.0', delta: '▲ 1.0' },
      score: { value: '88.6' },
      systemTier: 'A2',
      finalTier: 'A2',
      reason: null,
    },
    {
      stt: 5,
      name: 'Hoàng Thu Hà',
      code: 'NV021',
      sla: { value: '92%' },
      target: { value: '3/5', delta: '▲ 1', tone: 'good' },
      risk: { value: '1' },
      penalty: { value: '-1.5' },
      score: { value: '84.0', delta: '▼ 1.9' },
      systemTier: 'A3',
      finalTier: 'A3',
      reason: null,
    },
    {
      stt: 6,
      name: 'Đặng Anh Khoa',
      code: 'NV009',
      sla: { value: '90%', delta: '▲ 4%', tone: 'good' },
      target: { value: '3/5' },
      risk: { value: '2', delta: '▲ 2', tone: 'bad' },
      penalty: { value: '-3.0', delta: '▼ 1.0' },
      score: { value: '81.7', delta: '▲ 1.2' },
      systemTier: 'A3',
      finalTier: 'B',
      reason: null,
      reasonRequired: true,
      tierChanged: true,
    },
    {
      stt: 7,
      name: 'Vũ Minh Tuấn',
      code: 'NV011',
      sla: { value: '88%', delta: '▼ 1%', tone: 'bad' },
      target: { value: '3/5', delta: '▲ 1', tone: 'good' },
      risk: { value: '1', delta: '▲ 1', tone: 'bad' },
      penalty: { value: '-1.0', delta: '▲ 1.0' },
      score: { value: '79.4', delta: '▼ 1.6' },
      systemTier: 'B',
      finalTier: 'B',
      reason: null,
    },
    {
      stt: 8,
      name: 'Bùi Thị Lan',
      code: 'NV017',
      sla: { value: '85%', delta: '▼ 2%', tone: 'bad' },
      target: { value: '2/5', delta: '▼ 1', tone: 'bad' },
      risk: { value: '0', delta: '▼ 1', tone: 'good' },
      penalty: { value: '0.0' },
      score: { value: '74.2', delta: '▲ 1.2' },
      systemTier: 'C',
      finalTier: 'C',
      reason: null,
    },
  ];

  readonly expandedGroups = signal<ReadonlySet<string>>(new Set(['business']));

  get totalSlots(): number {
    return this.tierRows.reduce((sum, row) => sum + row.slots, 0);
  }

  get totalActual(): number {
    return this.tierRows.reduce((sum, row) => sum + row.actual, 0);
  }

  lockNoticeText(): string {
    return this.i18n.translate('evaluations.departmentRanking.lockNotice', { date: '15/11/2026' });
  }

  staffCardMetaText(): string {
    return this.i18n.translate('evaluations.departmentRanking.staffCardMeta', { compare: 'Q2/2026' });
  }

  staffFooterText(): string {
    return this.i18n.translate('evaluations.departmentRanking.staffFooter', { total: 20 });
  }

  compareMetaText(): string {
    return this.i18n.translate('evaluations.departmentRanking.compareMeta', { period: '2026-10' });
  }

  tierClass(tier: RankingTier): string {
    return TIER_CLASS[tier];
  }

  isExpanded(group: string): boolean {
    return this.expandedGroups().has(group);
  }

  toggleGroup(group: string): void {
    this.expandedGroups.update((current) => {
      const next = new Set(current);
      if (next.has(group)) {
        next.delete(group);
      } else {
        next.add(group);
      }
      return next;
    });
  }
}
