import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { EvaluationFormTargetService } from '../../core/evaluation-form-target.service';
import {
  DepartmentEvaluationDashboard,
  DepartmentEvaluationGroupKpi,
  DepartmentEvaluationKpiGroup,
  DepartmentEvaluationProfileGroupColumn,
  DepartmentEvaluationStaffRow,
  DepartmentEvaluationStaffRowStatus,
  DepartmentEvaluationTierItem,
  EvaluationPeriodOption,
} from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppNumberPipe } from '../../shared/pipes';
import { barWidth, toneOf, TRACK_HEADROOM } from '../performance-dashboard/performance-display';
import { saveFile } from '../performance-dashboard/performance-page';

export type Tier = 'A1' | 'A2' | 'A3' | 'B' | 'C' | 'D' | 'A';
export type StaffStatus = DepartmentEvaluationStaffRowStatus;
export type StaffSortKey = 'scoreDesc' | 'scoreAsc' | 'changeAsc' | 'slaAsc' | 'nameAsc';
export type PeriodTypeFilter = 'YEAR' | 'QUARTER' | 'MONTH';
export type TaskViewMode = 'dept' | 'staff';

export interface TrendBarPoint {
  periodId: string;
  nameHead: string;
  nameTail: string;
  average: number | null;
  valueLabel: string;
  x: number;
  y: number;
  width: number;
  height: number;
  center: number;
  isCurrent: boolean;
}

export interface ChartTick {
  value: number;
  y: number;
  label: string;
}

export interface StaffColumnBar {
  employeeId: string;
  employeeCode: string;
  shortName: string;
  score: number;
  scoreLabel: string;
  tierCode: string | null;
  x: number;
  y: number;
  width: number;
  height: number;
  center: number;
}

export interface TaskDistributionSegment {
  key: 'onTime' | 'inProgress' | 'overdue';
  label: string;
  count: number;
  sharePercent: number;
}

export interface StaffTaskStackedSegment {
  key: 'onTime' | 'inProgress' | 'overdue';
  count: number;
  y: number;
  height: number;
}

export interface StaffTaskStackedBar {
  employeeId: string;
  employeeCode: string;
  shortName: string;
  totalTasks: number;
  slaLabel: string;
  x: number;
  width: number;
  center: number;
  topY: number;
  segments: StaffTaskStackedSegment[];
}

const ALLOWED_PERIOD_TYPES: readonly PeriodTypeFilter[] = ['YEAR', 'QUARTER', 'MONTH'];

const TREND_W = 540;
const TREND_H = 148;
const TREND_LEFT = 36;
const TREND_BOTTOM = 32;
const TREND_TOP = 20;
const TREND_TICKS = 3;

const STAFF_CHART_W = 460;
const STAFF_CHART_H = 114;
const STAFF_CHART_LEFT = 30;
const STAFF_CHART_BOTTOM = 24;
const STAFF_CHART_TOP = 16;
const STAFF_CHART_TICKS = 3;
const STAFF_CHART_PAGE_SIZE = 6;

@Component({
  selector: 'app-evaluations',
  standalone: true,
  imports: [CommonModule, HeaderComponent, SidebarComponent, LayoutIconComponent, AppNumberPipe],
  templateUrl: './evaluations.component.html',
  styleUrls: [
    './evaluations.component.scss',
    './evaluations.task-chart.scss',
    './evaluations.table.scss',
    './evaluations.layout.scss',
  ],
})
export class EvaluationsComponent extends LocalizedComponent implements OnInit {
  private readonly evaluationService = inject(EvaluationService);
  private readonly formTarget = inject(EvaluationFormTargetService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly exporting = signal(false);
  readonly exportFailed = signal(false);
  readonly dashboard = signal<DepartmentEvaluationDashboard | null>(null);
  readonly selectedPeriodType = signal<PeriodTypeFilter>('MONTH');
  readonly selectedPeriodId = signal<string | null>(null);
  readonly selectedComparePeriodId = signal<string | null>(null);
  readonly staffChartPage = signal(1);
  readonly taskViewMode = signal<TaskViewMode>('dept');
  readonly searchKeyword = signal('');
  readonly positionFilter = signal('');
  readonly tierFilter = signal('');
  readonly statusFilter = signal('');
  readonly sortBy = signal<StaffSortKey>('scoreDesc');

  readonly periodTypes = ALLOWED_PERIOD_TYPES;
  readonly trendWidth = TREND_W;
  readonly trendHeight = TREND_H;
  readonly trendLeft = TREND_LEFT;
  readonly staffChartWidth = STAFF_CHART_W;
  readonly staffChartHeight = STAFF_CHART_H;
  readonly staffChartLeft = STAFF_CHART_LEFT;
  readonly markerPosition = 100 / TRACK_HEADROOM;
  readonly toneOf = toneOf;

  readonly departmentName = computed(
    () =>
      this.dashboard()?.department.name ?? this.localization.evaluations.dashboard.breadcrumbDept,
  );

  readonly currentPeriod = computed<EvaluationPeriodOption | null>(
    () => this.dashboard()?.period ?? null,
  );

  readonly comparePeriod = computed<EvaluationPeriodOption | null>(
    () => this.dashboard()?.comparePeriod ?? null,
  );

  readonly availablePeriods = computed<EvaluationPeriodOption[]>(() =>
    (this.dashboard()?.availablePeriods ?? []).filter((p) =>
      ALLOWED_PERIOD_TYPES.includes(p.periodType as PeriodTypeFilter),
    ),
  );

  readonly filteredPeriods = computed<EvaluationPeriodOption[]>(() => {
    const type = this.selectedPeriodType();
    const matching = this.availablePeriods().filter((p) => p.periodType === type);
    return matching.length > 0 ? matching : this.availablePeriods();
  });

  readonly compareOptions = computed<EvaluationPeriodOption[]>(() => {
    const currentType = this.currentPeriod()?.periodType ?? this.selectedPeriodType();
    return (this.dashboard()?.compareOptions ?? []).filter((p) => p.periodType === currentType);
  });

  readonly pageSubtitleText = computed(() =>
    this.i18n.translate('evaluations.dashboard.pageSubtitle', {
      period: this.currentPeriod()?.name ?? '—',
    }),
  );

  readonly departmentScore = computed<number | null>(
    () => this.dashboard()?.performance.average ?? null,
  );

  readonly previousDepartmentScore = computed<number | null>(
    () => this.dashboard()?.performance.previousAverage ?? null,
  );

  readonly departmentScoreDelta = computed<number | null>(
    () => this.dashboard()?.performance.changeFromPrevious ?? null,
  );

  readonly cardScorePillText = computed(() => {
    const delta = this.departmentScoreDelta();
    const compare = this.comparePeriod()?.code ?? this.comparePeriod()?.name;
    if (delta === null || !compare) {
      return '—';
    }
    const arrow = delta > 0 ? '▲ ' : delta < 0 ? '▼ ' : '';
    return this.i18n.translate('evaluations.dashboard.cardScorePill', {
      delta: `${arrow}${this.signedText(delta)}`,
      compare,
    });
  });

  readonly cardScoreCompareNoteText = computed(() => {
    const prev = this.previousDepartmentScore();
    const compare = this.comparePeriod()?.name;
    if (prev === null || !compare) {
      return this.localization.evaluations.dashboard.cardScoreNoCompare;
    }
    return this.i18n.translate('evaluations.dashboard.cardScoreCompareNote', {
      compare,
      prev: this.i18n.formatNumber(prev, 1),
    });
  });

  readonly kpiGroups = computed<DepartmentEvaluationKpiGroup[]>(
    () => this.dashboard()?.performance.kpiGroups ?? [],
  );

  readonly allKpis = computed(() => this.kpiGroups().flatMap((g) => g.kpis));

  readonly metCount = computed(
    () => this.allKpis().filter((k) => (k.achievementPercent ?? 0) >= 100).length,
  );

  readonly totalKpiCount = computed(() => this.allKpis().length);

  readonly kpiRate = computed<number | null>(() => {
    const total = this.totalKpiCount();
    if (total === 0) return null;
    return Math.round((this.metCount() * 1000) / total) / 10;
  });

  readonly maxKpiChange = computed(() =>
    Math.max(
      ...this.allKpis()
        .map((k) => k.changeFromPreviousPercent)
        .filter((v): v is number => v !== null)
        .map((v) => Math.abs(v)),
      1,
    ),
  );

  readonly cardKpiPillText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardKpiPill', {
      met: this.metCount(),
      total: this.totalKpiCount(),
    }),
  );

  readonly cardKpiCountText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardKpiCount', {
      met: this.metCount(),
      total: this.totalKpiCount(),
    }),
  );

  readonly cardKpiNoticeText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardKpiNotice', {
      count: Math.max(0, this.totalKpiCount() - this.metCount()),
    }),
  );

  readonly onTimeDone = computed(() => this.dashboard()?.operations.onTimeCompletedCount ?? 0);
  readonly overdueCount = computed(() => this.dashboard()?.operations.overdueCount ?? 0);
  readonly inProgressCount = computed(() => this.dashboard()?.operations.inProgressCount ?? 0);
  readonly onTimeRate = computed<number | null>(
    () => this.dashboard()?.operations.onTimeRatePercent ?? null,
  );
  readonly loadPerStaff = computed<number | null>(
    () => this.dashboard()?.operations.loadPerStaff ?? null,
  );

  readonly cardSlaCountText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardSlaCount', {
      count: this.onTimeDone(),
    }),
  );

  readonly cardSlaOverdueText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardSlaOverdue', {
      count: this.overdueCount(),
    }),
  );

  readonly opsOnTimeRateText = computed(() =>
    this.i18n.translate('evaluations.dashboard.opsOnTimeRate', {
      rate:
        this.onTimeRate() !== null
          ? this.i18n.formatNumber(this.onTimeRate()!, 1)
          : this.emptyValue,
    }),
  );

  readonly loadText = computed(() =>
    this.i18n.translate('evaluations.dashboard.loadPerStaff', {
      load:
        this.loadPerStaff() !== null
          ? this.i18n.formatNumber(this.loadPerStaff()!, 1)
          : this.emptyValue,
    }),
  );

  readonly evaluatedStaffCount = computed(
    () => this.dashboard()?.performance.evaluatedEmployeeCount ?? 0,
  );
  readonly totalStaffCount = computed(() => this.dashboard()?.performance.totalEmployeeCount ?? 0);
  readonly unscoredCount = computed(() => this.dashboard()?.performance.unscoredEmployeeCount ?? 0);
  readonly pipCount = computed(() => this.dashboard()?.performance.pipCount ?? 0);
  readonly appealCount = computed(() => this.dashboard()?.performance.appealCount ?? 0);

  readonly cardStaffPillText = computed(() => {
    const unscored = this.unscoredCount();
    if (unscored === 0) {
      return this.localization.evaluations.dashboard.cardStaffAllScored;
    }
    return this.i18n.translate('evaluations.dashboard.cardStaffPill', { count: unscored });
  });

  readonly cardStaffPipText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardStaffPip', { count: this.pipCount() }),
  );

  readonly cardStaffAppealText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardStaffAppeal', { count: this.appealCount() }),
  );

  readonly cardStaffUnscoredText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardStaffUnscored', {
      count: this.unscoredCount(),
    }),
  );

  readonly kpiPanelSubtitleText = computed(() =>
    this.i18n.translate('evaluations.dashboard.kpiPanelSubtitle', {
      groups: this.kpiGroups().length,
      kpis: this.totalKpiCount(),
      period: this.currentPeriod()?.name ?? '—',
    }),
  );

  readonly opsPanelSubtitleText = computed(() =>
    this.i18n.translate('evaluations.dashboard.opsPanelSubtitle', {
      period: this.currentPeriod()?.name ?? '—',
    }),
  );

  readonly staffChartSubtitleText = computed(() =>
    this.i18n.translate('evaluations.dashboard.staffChartSubtitle', {
      period: this.currentPeriod()?.name ?? '—',
    }),
  );

  readonly taskChartSubtitleText = computed(() =>
    this.i18n.translate('evaluations.dashboard.taskChartSubtitle', {
      period: this.currentPeriod()?.name ?? '—',
    }),
  );

  readonly tierItems = computed<DepartmentEvaluationTierItem[]>(
    () => this.dashboard()?.tierDistribution.tiers ?? [],
  );

  readonly tierACapPercent = computed(
    () => this.dashboard()?.tierDistribution.tierACapPercent ?? 20,
  );

  readonly tierLegendNoteText = computed(() =>
    this.i18n.translate('evaluations.dashboard.tierLegendNote', {
      scored: this.evaluatedStaffCount(),
      total: this.totalStaffCount(),
      cap: this.tierACapPercent(),
    }),
  );

  readonly profileGroups = computed<DepartmentEvaluationProfileGroupColumn[]>(
    () => this.dashboard()?.profileGroups ?? [],
  );

  readonly staff = computed<DepartmentEvaluationStaffRow[]>(
    () => this.dashboard()?.staffRows ?? [],
  );

  readonly scoredStaff = computed(() => this.staff().filter((item) => item.totalScore !== null));

  readonly sortedScoredStaff = computed(() =>
    [...this.scoredStaff()].sort((a, b) => (b.totalScore ?? 0) - (a.totalScore ?? 0)),
  );

  readonly availablePositions = computed<string[]>(() =>
    Array.from(
      new Set(
        this.staff()
          .map((row) => row.jobPositionTitle)
          .filter((title): title is string => Boolean(title)),
      ),
    ),
  );

  readonly maxStaffChange = computed(() =>
    Math.max(
      ...this.staff()
        .map((row) => row.scoreDelta)
        .filter((v): v is number => v !== null)
        .map((v) => Math.abs(v)),
      1,
    ),
  );

  readonly filteredStaff = computed(() => {
    const keyword = this.searchKeyword().trim().toLowerCase();
    const pos = this.positionFilter();
    const tier = this.tierFilter();
    const status = this.statusFilter();
    const sort = this.sortBy();

    const filtered = this.staff().filter((item) => {
      if (
        keyword &&
        !item.employeeCode.toLowerCase().includes(keyword) &&
        !item.fullName.toLowerCase().includes(keyword)
      ) {
        return false;
      }
      if (pos && item.jobPositionTitle !== pos) return false;
      if (tier) {
        if (tier === 'NONE' && item.tierCode !== null) return false;
        if (tier !== 'NONE' && item.tierCode !== tier) return false;
      }
      if (status && item.status !== status) return false;
      return true;
    });

    return [...filtered].sort((a, b) => this.compareStaff(a, b, sort));
  });

  readonly staffChartTotalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredStaff().length / STAFF_CHART_PAGE_SIZE)),
  );

  readonly pagedFilteredStaff = computed(() => {
    const page = Math.min(Math.max(1, this.staffChartPage()), this.staffChartTotalPages());
    const start = (page - 1) * STAFF_CHART_PAGE_SIZE;
    return this.filteredStaff().slice(start, start + STAFF_CHART_PAGE_SIZE);
  });

  readonly pagedScoredStaff = computed(() => {
    const scoredInPage = this.pagedFilteredStaff().filter((item) => item.totalScore !== null);
    if (scoredInPage.length > 0) return scoredInPage;
    const page = Math.min(Math.max(1, this.staffChartPage()), this.staffChartTotalPages());
    const start = (page - 1) * STAFF_CHART_PAGE_SIZE;
    return this.sortedScoredStaff().slice(start, start + STAFF_CHART_PAGE_SIZE);
  });

  readonly staffChartPageText = computed(() =>
    this.i18n.translate('evaluations.dashboard.staffChartPage', {
      page: Math.min(this.staffChartPage(), this.staffChartTotalPages()),
      total: this.staffChartTotalPages(),
    }),
  );

  readonly totalTrackedTasks = computed(
    () => this.onTimeDone() + this.inProgressCount() + this.overdueCount(),
  );

  readonly taskDistributionSegments = computed<TaskDistributionSegment[]>(() => {
    const total = this.totalTrackedTasks();
    const dict = this.localization.evaluations.dashboard;
    const raw: { key: TaskDistributionSegment['key']; label: string; count: number }[] = [
      { key: 'onTime', label: dict.opsOnTime, count: this.onTimeDone() },
      { key: 'inProgress', label: dict.opsInProgress, count: this.inProgressCount() },
      { key: 'overdue', label: dict.opsOverdue, count: this.overdueCount() },
    ];
    return raw.map((item) => ({
      ...item,
      sharePercent: total > 0 ? Math.round((item.count * 1000) / total) / 10 : 0,
    }));
  });

  readonly paginationText = computed(() =>
    this.i18n.translate('evaluations.dashboard.showing', {
      shown: this.pagedFilteredStaff().length,
      total: this.filteredStaff().length,
    }),
  );

  private readonly trendRange = computed(() => {
    const values = (this.dashboard()?.performance.trend ?? [])
      .map((p) => p.average)
      .filter((v): v is number => v !== null);
    if (!values.length) return { min: 0, max: 100 };
    const min = Math.max(0, Math.floor(Math.min(...values)) - 2);
    const max = Math.ceil(Math.max(...values)) + 1;
    return { min, max: max > min ? max : min + 1 };
  });

  readonly trendTicks = computed<ChartTick[]>(() => {
    const { min, max } = this.trendRange();
    return Array.from({ length: TREND_TICKS + 1 }, (_, i) => {
      const value = min + ((max - min) * i) / TREND_TICKS;
      return {
        value,
        y: this.scaleY(value, min, max, TREND_H, TREND_TOP, TREND_BOTTOM),
        label: this.i18n.formatNumber(value, 0),
      };
    });
  });

  readonly trendBars = computed<TrendBarPoint[]>(() => {
    const raw = this.dashboard()?.performance.trend ?? [];
    if (raw.length === 0) return [];
    const { min, max } = this.trendRange();
    const slot = (TREND_W - TREND_LEFT) / Math.max(raw.length, 1);
    const width = Math.min(slot * 0.56, 46);
    const base = TREND_H - TREND_BOTTOM;

    return raw.map((p, index) => {
      const y =
        p.average === null
          ? base
          : this.scaleY(p.average, min, max, TREND_H, TREND_TOP, TREND_BOTTOM);
      const center = TREND_LEFT + index * slot + slot / 2;
      const spaceIdx = p.periodName.lastIndexOf(' ');
      return {
        periodId: p.periodId,
        nameHead: spaceIdx > 0 ? p.periodName.slice(0, spaceIdx) : '',
        nameTail: spaceIdx > 0 ? p.periodName.slice(spaceIdx + 1) : p.periodCode || p.periodName,
        average: p.average,
        valueLabel: p.average === null ? this.emptyValue : this.i18n.formatNumber(p.average, 1),
        x: center - width / 2,
        y,
        width,
        height: Math.max(base - y, 2),
        center,
        isCurrent: index === raw.length - 1,
      };
    });
  });

  private readonly staffChartRange = computed(() => {
    const values = this.scoredStaff()
      .map((s) => s.totalScore)
      .filter((v): v is number => v !== null);
    if (!values.length) return { min: 0, max: 100 };
    const min = Math.max(0, Math.floor(Math.min(...values) / 5) * 5 - 5);
    const max = Math.ceil(Math.max(...values) / 5) * 5 + 2;
    return { min, max: max > min ? max : min + 10 };
  });

  readonly staffChartTicks = computed<ChartTick[]>(() => {
    const { min, max } = this.staffChartRange();
    return Array.from({ length: STAFF_CHART_TICKS + 1 }, (_, i) => {
      const value = min + ((max - min) * i) / STAFF_CHART_TICKS;
      return {
        value,
        y: this.scaleY(value, min, max, STAFF_CHART_H, STAFF_CHART_TOP, STAFF_CHART_BOTTOM),
        label: this.i18n.formatNumber(value, 0),
      };
    });
  });

  readonly staffChartBars = computed<StaffColumnBar[]>(() => {
    const rows = this.pagedScoredStaff();
    if (rows.length === 0) return [];
    const { min, max } = this.staffChartRange();
    const slotCount = Math.max(rows.length, STAFF_CHART_PAGE_SIZE);
    const slot = (STAFF_CHART_W - STAFF_CHART_LEFT) / slotCount;
    const width = Math.min(slot * 0.46, 28);
    const base = STAFF_CHART_H - STAFF_CHART_BOTTOM;

    return rows.map((row, index) => {
      const score = row.totalScore ?? 0;
      const y = this.scaleY(score, min, max, STAFF_CHART_H, STAFF_CHART_TOP, STAFF_CHART_BOTTOM);
      const center = STAFF_CHART_LEFT + index * slot + slot / 2;
      const parts = row.fullName.trim().split(/\s+/);
      const shortName = parts[parts.length - 1] || row.employeeCode;
      return {
        employeeId: row.employeeId,
        employeeCode: row.employeeCode,
        shortName,
        score,
        scoreLabel: this.i18n.formatNumber(score, 1),
        tierCode: row.tierCode,
        x: center - width / 2,
        y,
        width,
        height: Math.max(base - y, 3),
        center,
      };
    });
  });

  private readonly staffTaskChartMax = computed(() => {
    const totals = this.staff().map(
      (s) => (s.onTimeCompletedCount ?? 0) + (s.inProgressCount ?? 0) + (s.overdueCount ?? 0),
    );
    return Math.max(...totals, 3);
  });

  readonly staffTaskChartTicks = computed<ChartTick[]>(() => {
    const max = this.staffTaskChartMax();
    return Array.from({ length: STAFF_CHART_TICKS + 1 }, (_, i) => {
      const value = Math.round((max * i) / STAFF_CHART_TICKS);
      return {
        value,
        y: this.scaleY(value, 0, max, STAFF_CHART_H, STAFF_CHART_TOP, STAFF_CHART_BOTTOM),
        label: this.i18n.formatNumber(value, 0),
      };
    });
  });

  readonly staffTaskStackedBars = computed<StaffTaskStackedBar[]>(() => {
    const rows = this.pagedFilteredStaff();
    if (rows.length === 0) return [];
    const max = this.staffTaskChartMax();
    const slotCount = Math.max(rows.length, STAFF_CHART_PAGE_SIZE);
    const slot = (STAFF_CHART_W - STAFF_CHART_LEFT) / slotCount;
    const width = Math.min(slot * 0.46, 28);
    const base = STAFF_CHART_H - STAFF_CHART_BOTTOM;
    const plotHeight = base - STAFF_CHART_TOP;

    return rows.map((row, index) => {
      const onTime = row.onTimeCompletedCount ?? 0;
      const inProgress = row.inProgressCount ?? 0;
      const overdue = row.overdueCount ?? 0;
      const totalTasks = onTime + inProgress + overdue;
      const center = STAFF_CHART_LEFT + index * slot + slot / 2;
      const x = center - width / 2;
      const parts = row.fullName.trim().split(/\s+/);
      const shortName = parts[parts.length - 1] || row.employeeCode;

      let cursorY = base;
      const rawSegments: { key: StaffTaskStackedSegment['key']; count: number }[] = [
        { key: 'onTime', count: onTime },
        { key: 'inProgress', count: inProgress },
        { key: 'overdue', count: overdue },
      ];
      const segments: StaffTaskStackedSegment[] = [];
      for (const seg of rawSegments) {
        if (seg.count <= 0) continue;
        const height = Math.max((seg.count / Math.max(max, 1)) * plotHeight, 2);
        cursorY -= height;
        segments.push({
          key: seg.key,
          count: seg.count,
          y: cursorY,
          height,
        });
      }

      const slaLabel =
        row.onTimeSlaPercent === null
          ? `${totalTasks}`
          : `${totalTasks} (${this.i18n.formatNumber(row.onTimeSlaPercent, 0)}%)`;

      return {
        employeeId: row.employeeId,
        employeeCode: row.employeeCode,
        shortName,
        totalTasks,
        slaLabel,
        x,
        width,
        center,
        topY: cursorY,
        segments,
      };
    });
  });

  get emptyValue(): string {
    return this.localization.evaluations.emptyValue;
  }

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(periodId?: string | null, comparePeriodId?: string | null): void {
    this.loading.set(true);
    this.error.set(null);
    this.evaluationService
      .departmentDashboard(periodId, comparePeriodId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.dashboard.set(data);
          const resolvedType = (data.period?.periodType ?? 'MONTH') as PeriodTypeFilter;
          if (ALLOWED_PERIOD_TYPES.includes(resolvedType)) {
            this.selectedPeriodType.set(resolvedType);
          }
          this.selectedPeriodId.set(data.period?.id ?? null);
          this.selectedComparePeriodId.set(data.comparePeriod?.id ?? null);
          this.staffChartPage.set(1);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(this.localization.evaluations.dashboard.loadError);
          this.loading.set(false);
        },
      });
  }

  onPeriodTypeChange(event: Event): void {
    const nextType = (event.target as HTMLSelectElement).value as PeriodTypeFilter;
    this.selectedPeriodType.set(nextType);
    const firstMatching = this.availablePeriods().find((p) => p.periodType === nextType);
    if (firstMatching) {
      this.selectedPeriodId.set(firstMatching.id);
      this.selectedComparePeriodId.set(null);
      this.loadDashboard(firstMatching.id, null);
    }
  }

  onPeriodChange(event: Event): void {
    const periodId = (event.target as HTMLSelectElement).value || null;
    this.selectedPeriodId.set(periodId);
    this.selectedComparePeriodId.set(null);
    this.loadDashboard(periodId, null);
  }

  onCompareChange(event: Event): void {
    const compareId = (event.target as HTMLSelectElement).value || null;
    this.selectedComparePeriodId.set(compareId);
    this.loadDashboard(this.selectedPeriodId(), compareId);
  }

  changeStaffChartPage(delta: number): void {
    const next = Math.min(Math.max(1, this.staffChartPage() + delta), this.staffChartTotalPages());
    this.staffChartPage.set(next);
  }

  setTaskViewMode(mode: TaskViewMode): void {
    this.taskViewMode.set(mode);
  }

  periodTypeLabel(type: PeriodTypeFilter): string {
    const dict = this.localization.evaluations.dashboard.periodTypes as Record<string, string>;
    return dict[type] ?? type;
  }

  onSearchInput(event: Event): void {
    this.searchKeyword.set((event.target as HTMLInputElement).value);
    this.staffChartPage.set(1);
  }

  onPositionFilterChange(event: Event): void {
    this.positionFilter.set((event.target as HTMLSelectElement).value);
    this.staffChartPage.set(1);
  }

  onTierFilterChange(event: Event): void {
    this.tierFilter.set((event.target as HTMLSelectElement).value);
    this.staffChartPage.set(1);
  }

  onStatusFilterChange(event: Event): void {
    this.statusFilter.set((event.target as HTMLSelectElement).value);
    this.staffChartPage.set(1);
  }

  onSortChange(event: Event): void {
    this.sortBy.set((event.target as HTMLSelectElement).value as StaffSortKey);
    this.staffChartPage.set(1);
  }

  openDepartmentRanking(): void {
    void this.router.navigate(['/evaluations/department-ranking']);
  }

  exportExcel(): void {
    const data = this.dashboard();
    if (!data || this.exporting()) return;
    this.exporting.set(true);
    this.exportFailed.set(false);
    this.evaluationService
      .exportDepartmentDashboard(this.selectedPeriodId(), this.selectedComparePeriodId())
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) =>
          saveFile(
            blob,
            `danh-gia-phong-${data.department.code}-${data.period?.code ?? 'na'}.xlsx`,
          ),
        error: () => this.exportFailed.set(true),
      });
  }

  openStaffForm(row: DepartmentEvaluationStaffRow): void {
    const periodId = this.currentPeriod()?.id;
    if (!periodId) return;
    this.formTarget.save({ employeeId: row.employeeId, periodId });
    void this.router.navigate(['/evaluations/form']);
  }

  kpiGroupHeader(group: DepartmentEvaluationKpiGroup): string {
    if (!group.name) {
      return this.localization.evaluations.dashboard.group.other;
    }
    return `${group.name} · ${this.i18n.formatNumber(group.weightPercent, 0)}%`;
  }

  formatKpiActual(kpi: { actualValue: number | null; targetValue: number; unit: string }): string {
    const actualText =
      kpi.actualValue === null ? this.emptyValue : this.i18n.formatNumber(kpi.actualValue, 1);
    const targetText = this.i18n.formatNumber(kpi.targetValue, 1);
    return `${actualText} / ${targetText} ${kpi.unit}`.trim();
  }

  kpiConvertedScore(
    group: DepartmentEvaluationKpiGroup,
    kpi: DepartmentEvaluationGroupKpi,
  ): number | null {
    if (kpi.score !== undefined && kpi.score !== null) {
      return kpi.score;
    }
    if (kpi.achievementPercent === null) {
      return null;
    }
    if (group.kpis.length > 0 && group.weightPercent > 0) {
      const shareWeight = group.weightPercent / group.kpis.length;
      return Math.round((kpi.achievementPercent * shareWeight) / 10) / 10;
    }
    return kpi.achievementPercent;
  }

  kpiAchievementDelta(achievementPercent: number | null): number | null {
    if (achievementPercent === null) return null;
    return Math.round((achievementPercent - 100) * 10) / 10;
  }

  kpiAchievementBarWidth(achievementPercent: number | null): number {
    if (achievementPercent === null) return 0;
    return barWidth(Math.abs(achievementPercent - 100), 30);
  }

  kpiChangeBarWidth(change: number | null): number {
    return barWidth(change === null ? null : Math.abs(change), this.maxKpiChange());
  }

  staffChangeBarWidth(change: number | null): number {
    return barWidth(change === null ? null : Math.abs(change), this.maxStaffChange());
  }

  tone(value: number | null): string {
    return toneOf(value) ?? '';
  }

  staffGroupScore(row: DepartmentEvaluationStaffRow, groupName: string): number | null {
    return row.groupScores.find((g) => g.groupName === groupName)?.score ?? null;
  }

  signedText(change: number): string {
    const text = this.i18n.formatNumber(Math.abs(change), 1);
    if (change > 0) return `+${text}`;
    return change < 0 ? `−${text}` : text;
  }

  tierBadgeText(tierCode: string | null): string {
    if (!tierCode) return this.localization.evaluations.dashboard.tierBadge.NONE;
    const dict = this.localization.evaluations.dashboard.tierBadge as Record<string, string>;
    return dict[tierCode] ?? tierCode;
  }

  tierShortRangeText(item: DepartmentEvaluationTierItem): string {
    const min = this.i18n.formatNumber(item.minScore, 0);
    if (item.maxScore === null) {
      return `≥${min}đ`;
    }
    const max = this.i18n.formatNumber(item.maxScore, 0);
    return `${min}–${max}đ`;
  }

  statusText(status: StaffStatus): string {
    const dict = this.localization.evaluations.dashboard.status as Record<string, string>;
    return dict[status] ?? status;
  }

  staffBarClass(tierCode: string | null): string {
    if (!tierCode) return 'staff-bar-none';
    if (tierCode.startsWith('A')) return 'staff-bar-a';
    if (tierCode === 'B') return 'staff-bar-b';
    return 'staff-bar-c';
  }

  private scaleY(
    value: number,
    min: number,
    max: number,
    height: number,
    top: number,
    bottom: number,
  ): number {
    const plotHeight = height - top - bottom;
    return top + plotHeight - ((value - min) / Math.max(max - min, 1)) * plotHeight;
  }

  private compareStaff(
    a: DepartmentEvaluationStaffRow,
    b: DepartmentEvaluationStaffRow,
    sort: StaffSortKey,
  ): number {
    switch (sort) {
      case 'scoreAsc':
        return (a.totalScore ?? 9999) - (b.totalScore ?? 9999);
      case 'changeAsc':
        return (a.scoreDelta ?? 9999) - (b.scoreDelta ?? 9999);
      case 'slaAsc':
        return (a.onTimeSlaPercent ?? 9999) - (b.onTimeSlaPercent ?? 9999);
      case 'nameAsc':
        return a.fullName.localeCompare(b.fullName, this.i18n.currentLanguage());
      default:
        return (b.totalScore ?? -1) - (a.totalScore ?? -1);
    }
  }
}
