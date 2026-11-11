import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { CampaignService } from '../../core/campaign.service';
import { CampaignListOverview } from '../../core/campaign.models';
import { DepartmentService } from '../../core/department.service';
import {
  COMPARE_MODES,
  DepartmentOverview,
  DepartmentSummary,
  PerformanceCompareMode,
} from '../../core/department.models';
import { EvaluationService } from '../../core/evaluation.service';
import { BranchEvaluationPeriodItem } from '../../core/evaluation.models';
import { PerfCampaignPaceComponent } from '../performance-dashboard/performance-campaign-pace.component';
import { PerfChildUnitsComponent } from '../performance-dashboard/performance-child-units.component';
import {
  formatArrow,
  formatValue,
  NOT_AVAILABLE,
  toneOf,
} from '../performance-dashboard/performance-display';
import { PerfEvaluationPeriodComponent } from '../performance-dashboard/performance-evaluation-period.component';
import { PerfKpiBelowPlanComponent } from '../performance-dashboard/performance-kpi-below-plan.component';
import { PerfKpiGroupTableComponent } from '../performance-dashboard/performance-kpi-group-table.component';
import { PerfMetricCardComponent } from '../performance-dashboard/performance-metric-card.component';
import { kpiOnPlan, saveFile } from '../performance-dashboard/performance-page';
import { PerfTrendChartComponent } from '../performance-dashboard/performance-trend-chart.component';

/** Display size of the running-campaign card; the full list is one click away at /campaigns. */
const CAMPAIGN_CARD_SIZE = 5;

/**
 * SCR-PRF-03 Branch performance (branch manager): the branch overview (`GET /departments/{branchId}/overview`), the
 * open evaluation period (`GET /evaluations/branch-periods`) and running campaigns (`GET /campaigns`), see
 * docs/api/BRANCH_DASHBOARD_CONTRACT.md. A failing side card shows its own retry; nothing is ever filled with samples.
 */
@Component({
  selector: 'app-branch-performance',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    PerfMetricCardComponent,
    PerfChildUnitsComponent,
    PerfEvaluationPeriodComponent,
    PerfCampaignPaceComponent,
    PerfKpiBelowPlanComponent,
    PerfKpiGroupTableComponent,
    PerfTrendChartComponent,
  ],
  templateUrl: './branch-performance.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', '../departments/department-view.component.scss'],
})
export class BranchPerformanceComponent extends LocalizedComponent {
  private readonly departments = inject(DepartmentService);
  private readonly evaluations = inject(EvaluationService);
  private readonly campaignApi = inject(CampaignService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  protected readonly branches = signal<DepartmentSummary[]>([]);
  protected readonly overview = signal<DepartmentOverview | null>(null);
  protected readonly periods = signal<BranchEvaluationPeriodItem[]>([]);
  protected readonly periodsFailed = signal(false);
  protected readonly campaigns = signal<CampaignListOverview | null>(null);
  protected readonly campaignsFailed = signal(false);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly exporting = signal(false);
  protected readonly exportFailed = signal(false);
  protected readonly compareModes = COMPARE_MODES;
  protected branchId = '';
  protected periodId = '';
  protected compare: PerformanceCompareMode = 'PREVIOUS';

  protected readonly compareName = computed(() => this.overview()?.comparePeriod?.name ?? null);
  protected readonly kpiCount = computed(() => kpiOnPlan(this.overview()));

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.departments
            .overview(this.branchId, this.periodId || undefined, this.compare)
            .pipe(
              catchError(() => {
                this.failed.set(true);
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((overview) => {
        this.overview.set(overview);
        this.periodId = overview.period?.id ?? '';
        this.loadPeriods();
      });
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.departments
      .list({ levelType: 'BRANCH', pageSize: 100 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.branches.set(list.items);
          this.branchId = list.items[0]?.id ?? '';
          if (this.branchId) {
            this.refresh();
            this.loadCampaigns();
          } else {
            this.loading.set(false);
          }
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected changeBranch(): void {
    this.periodId = '';
    this.refresh();
  }

  /** Periods of the selected period's year; the card shows the newest one that is not locked. */
  protected loadPeriods(): void {
    const start = this.overview()?.period?.startDate;
    this.periodsFailed.set(false);
    this.evaluations
      .branchPeriods(start ? Number(start.slice(0, 4)) : null)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.periods.set(result.periods),
        error: () => this.periodsFailed.set(true),
      });
  }

  protected loadCampaigns(): void {
    this.campaignsFailed.set(false);
    this.campaignApi
      .list({ status: 'RUNNING', page: 1, pageSize: CAMPAIGN_CARD_SIZE })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.campaigns.set(result),
        error: () => this.campaignsFailed.set(true),
      });
  }

  protected scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected open(route: string): void {
    void this.router.navigateByUrl(route);
  }

  protected exportExcel(data: DepartmentOverview): void {
    this.exporting.set(true);
    this.exportFailed.set(false);
    this.departments
      .exportOverview(this.branchId, data.period?.id, this.compare)
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) =>
          saveFile(blob, `hieu-suat-${data.department.code}-${data.period?.code ?? 'na'}.xlsx`),
        error: () => this.exportFailed.set(true),
      });
  }

  protected score(value: number | null | undefined): string {
    return formatValue(this.i18n.currentLanguage(), value);
  }

  protected delta(value: number | null): string | null {
    const name = this.compareName();
    return name == null || value == null
      ? null
      : `${formatArrow(this.i18n.currentLanguage(), value)} ${this.localization.departmentPerformance.vs} ${name}`;
  }

  protected tone(value: number | null) {
    return toneOf(value);
  }

  protected ratio(part: number | null | undefined, whole: number | null | undefined): string {
    return part == null || whole == null || whole === 0 ? NOT_AVAILABLE : `${part}/${whole}`;
  }

  protected compareLabel(mode: PerformanceCompareMode): string {
    const text = this.localization.departmentPerformance;
    return mode === 'SAME_LAST_YEAR' ? text.compareSameLastYear : text.comparePrevious;
  }
}
