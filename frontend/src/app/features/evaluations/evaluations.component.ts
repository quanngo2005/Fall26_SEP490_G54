import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  OnInit,
  signal,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { EvaluationFormTargetService } from '../../core/evaluation-form-target.service';
import {
  DepartmentEvaluationDashboard,
  DepartmentEvaluationGroupKpi,
  DepartmentEvaluationKpiGroup,
  DepartmentEvaluationProfileGroupColumn,
  DepartmentEvaluationSelectOption,
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
import { ComparePeriodMode, CompareSliderComponent, UiSelectComponent } from '../../shared/ui';
import { findPreviousPeriod, findSameLastYearPeriod } from '../../core/period-options';
import { toneOf, TRACK_HEADROOM } from '../performance-dashboard/performance-display';
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

export interface KpiChartBar {
  kpiDefinitionId: string;
  name: string;
  targetY: number;
  actualY: number;
  targetHeight: number;
  actualHeight: number;
  targetLabel: string;
  actualLabel: string;
  targetText: string;
  actualText: string;
  met: boolean;
  x: number;
  center: number;
  width: number;
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
const KPI_CHART_W = 540;
const KPI_CHART_SLOT_W = 120;
const KPI_CHART_H = 216;
const KPI_CHART_LEFT = 16;
const KPI_CHART_BOTTOM = 56;
const KPI_CHART_TOP = 16;

function roundKpi(value: number): number {
  return Math.round(value * 100) / 100;
}

function sumKpiScores(scores: (number | null | undefined)[]): number | null {
  const values = scores.filter((s): s is number => s != null);
  if (values.length === 0) return null;
  return roundKpi(values.reduce((sum, s) => sum + s, 0));
}

@Component({
  selector: 'app-evaluations',
  standalone: true,
  imports: [
    CommonModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    AppNumberPipe,
    UiSelectComponent,
    CompareSliderComponent,
  ],
  templateUrl: './evaluations.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './evaluations.component.scss',
    './evaluations.charts.scss',
    './evaluations.task-chart.scss',
    './evaluations.table.scss',
    './evaluations.layout.scss',
    '../../shared/ui/metric-cards.scss',
  ],
})
export class EvaluationsComponent extends LocalizedComponent implements OnInit {
  protected readonly evaluationService = inject(EvaluationService);
  protected readonly formTarget = inject(EvaluationFormTargetService);
  protected readonly router = inject(Router);
  protected readonly route = inject(ActivatedRoute);
  protected readonly destroyRef = inject(DestroyRef);

  readonly isBranchMode = input<boolean>(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly exporting = signal(false);
  readonly exportFailed = signal(false);
  readonly dashboard = signal<DepartmentEvaluationDashboard | null>(null);
  readonly isTeam = computed(() => this.dashboard()?.department?.isTeam ?? false);
  readonly selectedDepartmentId = signal<string | null>(null);
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
  readonly kpiGroupsExpanded = signal(false);
  readonly hoveredKpiBar = signal<KpiChartBar | null>(null);
  @ViewChild('kpiTip') kpiTip?: ElementRef<HTMLElement>;

  readonly availableDepartments = computed<DepartmentEvaluationSelectOption[]>(
    () => this.dashboard()?.availableDepartments ?? [],
  );

  readonly departmentOptions = computed(() =>
    this.availableDepartments().map((dept) => ({
      value: dept.id,
      label: `${dept.code} · ${dept.name}`,
    })),
  );

  readonly periodTypes = ALLOWED_PERIOD_TYPES;
  readonly trendWidth = TREND_W;
  readonly kpiChartWidth = computed(() =>
    Math.max(KPI_CHART_W, this.allKpis().length * KPI_CHART_SLOT_W + KPI_CHART_LEFT),
  );
  readonly kpiChartHeight = KPI_CHART_H;
  readonly kpiChartBase = KPI_CHART_H - KPI_CHART_BOTTOM;
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

  readonly compareMode = signal<ComparePeriodMode>('PREVIOUS');

  readonly previousPeriod = computed(() => {
    const current = this.currentPeriod();
    return findPreviousPeriod(current, this.compareOptions());
  });

  readonly sameLastYearPeriod = computed(() => {
    const current = this.currentPeriod();
    return findSameLastYearPeriod(current, this.availablePeriods());
  });

  readonly hasPreviousPeriod = computed(() => !!this.previousPeriod());
  readonly hasSameLastYearPeriod = computed(() => !!this.sameLastYearPeriod());

  readonly compareOptions = computed<EvaluationPeriodOption[]>(() => {
    const currentType = this.currentPeriod()?.periodType ?? this.selectedPeriodType();
    return (this.dashboard()?.compareOptions ?? []).filter((p) => p.periodType === currentType);
  });

  readonly periodTypeOptions = computed(() =>
    this.periodTypes.map((type) => ({ value: type, label: this.periodTypeLabel(type) })),
  );

  readonly periodOptions = computed(() =>
    this.filteredPeriods().map((p) => ({ value: p.id, label: p.name })),
  );

  readonly compareSelectOptions = computed(() => [
    { value: '', label: this.localization.evaluations.dashboard.periodCompareNone },
    ...this.compareOptions().map((p) => ({
      value: p.id,
      label: `${this.localization.evaluations.dashboard.periodComparePrefix}${p.name}`,
    })),
  ]);

  readonly positionFilterOptions = computed(() => [
    { value: '', label: this.localization.evaluations.dashboard.allPositions },
    ...this.availablePositions().map((pos) => ({ value: pos, label: pos })),
  ]);

  readonly tierFilterOptions = computed(() => {
    const dict = this.localization.evaluations.dashboard;
    return [
      { value: '', label: dict.allTiers },
      { value: 'A1', label: dict.tierBadge.A1 },
      { value: 'A2', label: dict.tierBadge.A2 },
      { value: 'A3', label: dict.tierBadge.A3 },
      { value: 'B', label: dict.tierBadge.B },
      { value: 'C', label: dict.tierBadge.C },
      { value: 'NONE', label: dict.tierBadge.NONE },
    ];
  });

  readonly statusFilterOptions = computed(() => {
    const dict = this.localization.evaluations.dashboard;
    return [
      { value: '', label: dict.allStatuses },
      { value: 'FINAL', label: dict.status.FINAL },
      { value: 'APPEAL', label: dict.status.APPEAL },
      { value: 'PIP', label: dict.status.PIP },
      { value: 'PENDING', label: dict.status.PENDING },
    ];
  });

  readonly sortOptions = computed(() => {
    const dict = this.localization.evaluations.dashboard;
    return [
      { value: 'scoreDesc', label: dict.sortScoreDesc },
      { value: 'scoreAsc', label: dict.sortScoreAsc },
      { value: 'changeAsc', label: dict.sortChangeAsc },
      { value: 'slaAsc', label: dict.sortSlaAsc },
      { value: 'nameAsc', label: dict.sortNameAsc },
    ];
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
    return this.i18n.translate('evaluations.dashboard.cardScorePill', {
      delta: this.signedText(delta),
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

  readonly totalScore = computed(() => sumKpiScores(this.allKpis().map((k) => k.score)));

  readonly previousTotalScore = computed(() =>
    sumKpiScores(this.allKpis().map((k) => k.previousScore)),
  );

  readonly totalScoreChange = computed(() => {
    const current = this.totalScore();
    const previous = this.previousTotalScore();
    return current === null || previous === null ? null : roundKpi(current - previous);
  });

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

  readonly cardSlaDoneText = computed(() =>
    this.i18n.translate('evaluations.dashboard.cardSlaDone', {
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

  readonly kpiChartBars = computed<KpiChartBar[]>(() => {
    const kpis = this.allKpis();
    if (kpis.length === 0) return [];
    const chartWidth = this.kpiChartWidth();
    const slot = (chartWidth - KPI_CHART_LEFT) / kpis.length;
    const width = Math.min(slot * 0.32, 30);
    const base = KPI_CHART_H - KPI_CHART_BOTTOM;
    const plot = base - KPI_CHART_TOP;

    return kpis.map((kpi, index) => {
      const max = Math.max(kpi.targetValue, kpi.actualValue ?? 0, 1);
      const targetHeight = (kpi.targetValue / max) * plot;
      const actualHeight = ((kpi.actualValue ?? 0) / max) * plot;
      const center = KPI_CHART_LEFT + index * slot + slot / 2;
      return {
        kpiDefinitionId: kpi.kpiDefinitionId,
        name: kpi.name,
        targetY: base - targetHeight,
        actualY: base - actualHeight,
        targetHeight,
        actualHeight,
        targetLabel: this.i18n.formatNumber(kpi.targetValue, 1),
        actualLabel:
          kpi.actualValue === null ? this.emptyValue : this.i18n.formatNumber(kpi.actualValue, 1),
        targetText: this.targetText(kpi),
        actualText: this.actualText(kpi.actualValue, kpi.unit),
        met: (kpi.achievementPercent ?? 0) >= 100,
        x: center - width - 2,
        center,
        width,
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
    const q = this.route.snapshot.queryParamMap;
    const initialDeptId = q.get('departmentId') ?? q.get('department');
    if (initialDeptId) {
      this.selectedDepartmentId.set(initialDeptId);
    }
    this.loadDashboard(null, null, initialDeptId);
  }

  loadDashboard(
    periodId?: string | null,
    comparePeriodId?: string | null,
    departmentId?: string | null,
  ): void {
    this.loading.set(true);
    this.error.set(null);
    const deptToLoad = departmentId !== undefined ? departmentId : this.selectedDepartmentId();
    const req$ = this.isBranchMode()
      ? this.evaluationService.branchDashboard(periodId, comparePeriodId, deptToLoad)
      : this.evaluationService.departmentDashboard(periodId, comparePeriodId);

    req$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (data) => {
        this.dashboard.set(data);
        this.selectedDepartmentId.set(data.department.id);
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

  onDepartmentSelected(value: string): void {
    const deptId = value || null;
    this.selectedDepartmentId.set(deptId);
    this.loadDashboard(this.selectedPeriodId(), this.selectedComparePeriodId(), deptId);
  }

  onPeriodTypeSelected(value: string): void {
    const nextType = value as PeriodTypeFilter;
    this.selectedPeriodType.set(nextType);
    const matching = this.availablePeriods().filter((p) => p.periodType === nextType);
    const now = new Date();
    const today = [
      now.getFullYear(),
      String(now.getMonth() + 1).padStart(2, '0'),
      String(now.getDate()).padStart(2, '0'),
    ].join('-');
    const current = matching.find((p) => p.startDate <= today && today <= p.endDate);
    const targetPeriod = current ?? matching.find((p) => p.isActive) ?? matching[0];
    if (targetPeriod) {
      this.selectedPeriodId.set(targetPeriod.id);
      this.selectedComparePeriodId.set(null);
      this.loadDashboard(targetPeriod.id, null, this.selectedDepartmentId());
    }
  }

  onPeriodValueSelected(value: string): void {
    const periodId = value || null;
    this.selectedPeriodId.set(periodId);
    this.selectedComparePeriodId.set(null);
    this.loadDashboard(periodId, null, this.selectedDepartmentId());
  }

  onCompareValueSelected(value: string): void {
    const compareId = value || null;
    this.selectedComparePeriodId.set(compareId);
    this.loadDashboard(this.selectedPeriodId(), compareId, this.selectedDepartmentId());
  }

  onCompareModeChanged(mode: ComparePeriodMode): void {
    this.compareMode.set(mode);
    const targetPeriod =
      mode === 'SAME_LAST_YEAR' ? this.sameLastYearPeriod() : this.previousPeriod();
    const compareId = targetPeriod?.id ?? null;
    this.selectedComparePeriodId.set(compareId);
    this.loadDashboard(this.selectedPeriodId(), compareId, this.selectedDepartmentId());
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

  onPositionFilterSelected(value: string): void {
    this.positionFilter.set(value);
    this.staffChartPage.set(1);
  }

  onTierFilterSelected(value: string): void {
    this.tierFilter.set(value);
    this.staffChartPage.set(1);
  }

  onStatusFilterSelected(value: string): void {
    this.statusFilter.set(value);
    this.staffChartPage.set(1);
  }

  onSortSelected(value: string): void {
    this.sortBy.set(value as StaffSortKey);
    this.staffChartPage.set(1);
  }

  openDepartmentRanking(): void {
    if (this.isBranchMode()) {
      const periodId = this.selectedPeriodId();
      if (periodId) {
        void this.router.navigate(['/branch-performance/evaluations/approval'], {
          state: { periodId },
        });
      } else {
        void this.router.navigate(['/branch-performance/evaluations/approval']);
      }
    } else {
      void this.router.navigate(['/evaluations/department-ranking']);
    }
  }

  exportExcel(): void {
    const data = this.dashboard();
    if (!data || this.exporting()) return;
    this.exporting.set(true);
    this.exportFailed.set(false);
    const export$ = this.isBranchMode()
      ? this.evaluationService.exportBranchDashboard(
          this.selectedPeriodId(),
          this.selectedComparePeriodId(),
          this.selectedDepartmentId(),
        )
      : this.evaluationService.exportDepartmentDashboard(
          this.selectedPeriodId(),
          this.selectedComparePeriodId(),
        );

    export$
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
    return this.hasExpandedKpiGroup()
      ? group.name
      : `${group.name} · ${this.i18n.formatNumber(group.weightPercent, 0)}%`;
  }

  hasExpandedKpiGroup(): boolean {
    return this.kpiGroupsExpanded();
  }

  kpiNameColspan(): number {
    return this.hasExpandedKpiGroup() ? 5 : 1;
  }

  scrollToSection(id: string): void {
    document
      .getElementById(`section-${id}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  valueToneClass(value: number | null, good: number, warn: number): string {
    if (value === null) return '';
    if (value >= good) return 'tone-good';
    if (value >= warn) return 'tone-warn';
    return 'tone-bad';
  }

  toggleAllKpiGroups(): void {
    this.kpiGroupsExpanded.update((open) => !open);
  }

  weightText(weight: number | null): string {
    return weight === null ? this.emptyValue : `${this.i18n.formatNumber(weight, 1)}%`;
  }

  targetText(kpi: DepartmentEvaluationGroupKpi): string {
    return `${this.i18n.formatNumber(kpi.targetValue, 1)} ${kpi.unit}`.trim();
  }

  actualText(value: number | null, unit: string): string {
    if (value === null) return this.emptyValue;
    return `${this.i18n.formatNumber(value, 1)} ${unit}`.trim();
  }

  actualChange(kpi: DepartmentEvaluationGroupKpi): number | null {
    if (kpi.actualValue === null || kpi.previousActualValue === null) return null;
    return roundKpi(kpi.actualValue - kpi.previousActualValue);
  }

  scoreChange(kpi: DepartmentEvaluationGroupKpi): number | null {
    if (kpi.score == null || kpi.previousScore == null) return null;
    return roundKpi(kpi.score - kpi.previousScore);
  }

  groupScoreChange(group: DepartmentEvaluationKpiGroup): number | null {
    const prevKpis = group.kpis.filter((k) => k.previousAchievementPercent != null);
    if (prevKpis.length === 0 || group.averageScore == null) return null;
    const prevAvg =
      prevKpis.reduce((sum, k) => sum + k.previousAchievementPercent!, 0) / prevKpis.length;
    return roundKpi(group.averageScore - prevAvg);
  }

  showKpiTip(event: MouseEvent, bar: KpiChartBar): void {
    this.hoveredKpiBar.set(bar);
    const tip = this.kpiTip?.nativeElement;
    if (!tip) return;
    const anchor = (event.currentTarget as SVGGElement).getBoundingClientRect();
    tip.style.top = `${anchor.bottom + 6}px`;
    tip.style.left = `${anchor.left + anchor.width / 2}px`;
    tip.style.transform = 'translateX(-50%)';
  }

  hideKpiTip(): void {
    this.hoveredKpiBar.set(null);
  }

  rateNoteText(row: DepartmentEvaluationGroupKpi): string {
    const dict = this.localization.evaluations.dashboard;
    if (row.achievementPercent === null) return this.emptyValue;
    if (row.achievementPercent === 100) return dict.rateMet;
    const gap = (row.actualValue ?? 0) - row.targetValue;
    const value = `${this.i18n.formatNumber(Math.abs(gap), 1)} ${row.unit}`.trim();
    if (row.achievementPercent < 100) {
      return this.i18n.translate('evaluations.dashboard.rateBelow', { value });
    }
    return this.i18n.translate('evaluations.dashboard.rateAbove', { value });
  }

  tone(value: number | null): string {
    return toneOf(value) ?? '';
  }

  staffGroupScore(row: DepartmentEvaluationStaffRow, groupName: string): number | null {
    return row.groupScores.find((g) => g.groupName === groupName)?.score ?? null;
  }

  staffGroupScoreChange(row: DepartmentEvaluationStaffRow, groupName: string): number | null {
    const group = row.groupScores.find((g) => g.groupName === groupName);
    if (!group || group.score == null || group.previousScore == null) return null;
    return Math.round((group.score - group.previousScore) * 100) / 100;
  }

  staffSlaChange(row: DepartmentEvaluationStaffRow): number | null {
    if (row.onTimeSlaPercent == null || row.previousOnTimeSlaPercent == null) return null;
    return Math.round((row.onTimeSlaPercent - row.previousOnTimeSlaPercent) * 100) / 100;
  }

  hasStaffDelta(change: number | null): boolean {
    return change != null && Math.abs(change) >= 0.05;
  }

  staffDeltaText(change: number | null): string {
    if (change == null) return '';
    return this.signedText(change);
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
