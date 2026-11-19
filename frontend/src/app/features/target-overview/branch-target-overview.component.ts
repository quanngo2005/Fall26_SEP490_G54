import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of, switchMap } from 'rxjs';
import { ApiResponse } from '../../core/api-response.model';
import { AuthService } from '../../core/auth.service';
import { DepartmentService } from '../../core/department.service';
import { DepartmentSummary } from '../../core/department.models';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { normalizePermission } from '../../core/organization.guard';
import { TargetAssignment, TargetSetup } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { RightDrawerComponent } from '../../shared/target/right-drawer/right-drawer.component';
import { ImportBranchTargetModalComponent } from './import-branch-target-modal/import-branch-target-modal.component';
import { KpiQuarterModalComponent } from './kpi-quarter-modal/kpi-quarter-modal.component';
import { AppNumberPipe } from '../../shared/pipes';
import { TargetCellComponent } from './target-cell.component';
import { TargetOverviewApiService } from './target-overview-api.service';
import {
  availableYears,
  buildAllocationHeatmap,
  buildBranchQuarterRows,
  buildDepartmentQuarterRows,
  ColumnFormat,
  columnFormat,
  fetchAllPages,
  findQuarterPeriod,
  findYearPeriod,
  groupKpiRows,
  HeatmapData,
  includesText,
  KpiQuarterRow,
} from './target-overview.utils';
import { SuggestButtonComponent } from '../../shared/target/suggest-button/suggest-button.component';

/** Grants "Thiết lập mục tiêu năm" (same permission the backend checks: target:manage). */
const TARGET_MANAGE_PERMISSION = 'TARGET_MANAGE';
const FULL_ACCESS_PERMISSION = 'FULL_ACCESS';

/**
 * W2/W8 strings. Pending merge into `core/i18n/localization.json` (`docs/plans/ta-v3-bm.pending.json`, section
 * `targetBm`) - cast narrowly here so the rest of the app keeps compiling before the merge; the heatmap tab and
 * drawer are the only things that read it, and they render on demand (tab click / row click), not on load.
 */
interface TargetBmOverviewText {
  readonly tabKpi: string;
  readonly tabHeatmap: string;
  readonly heatmapNote: string;
  readonly heatmapColDepartment: string;
  readonly heatmapLegendMatched: string;
  readonly heatmapLegendMismatch: string;
  readonly heatmapLegendNotEligible: string;
  readonly heatmapBelowThreshold: string;
  readonly heatmapLoading: string;
  readonly heatmapFailed: string;
  readonly heatmapRetry: string;
  readonly heatmapEmpty: string;
  readonly drawerYearCol: string;
  readonly drawerStatusCol: string;
  readonly drawerViewPerformance: string;
  readonly drawerRemindManager: string;
  readonly drawerRemindSending: string;
  readonly drawerRemindSent: string;
  readonly drawerEmpty: string;
}

const QUARTERS = [1, 2, 3, 4] as const;
type ViewMode = 'BRANCH' | 'DEPARTMENT';
/** The "Chia quý" filter: `MISMATCH` covers both `SHORT` and `OVER` row states. */
type SplitFilter = '' | 'MATCHED' | 'MISMATCH' | 'NOT_SPLIT' | 'FIXED';
/** W1 "Theo KPI" / W2 "Theo phòng (heatmap)" tab. */
type OverviewTab = 'KPI' | 'HEATMAP';
type ReminderState = 'IDLE' | 'SENDING' | 'SENT' | 'ERROR';

@Component({
  selector: 'app-branch-target-overview',
  imports: [
    SuggestButtonComponent,
    FormsModule,
    TargetCellComponent,
    ImportBranchTargetModalComponent,
    KpiQuarterModalComponent,
    AppNumberPipe,
    RightDrawerComponent,
  ],
  templateUrl: './branch-target-overview.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/target/target-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './target-overview.scss',
  ],
})
export class BranchTargetOverviewComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly overviewApi = inject(TargetOverviewApiService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly departmentApi = inject(DepartmentService);
  private readonly authApi = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly quarters = QUARTERS;

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  /** Branch view only: the selected year has no imported branch-year yet. */
  protected readonly notImported = signal(false);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  private readonly kpis = signal<KpiDefinition[]>([]);
  private readonly departments = signal<DepartmentSummary[]>([]);
  private readonly rows = signal<KpiQuarterRow[]>([]);

  protected readonly year = signal<number | null>(null);
  protected readonly viewMode = signal<ViewMode>('BRANCH');
  protected readonly selectedDepartmentId = signal('');
  /** Filters the KPI rows by name or code. */
  protected readonly kpiSearch = signal('');
  protected readonly groupFilter = signal('');
  protected readonly splitFilter = signal<SplitFilter>('');
  protected readonly showImportModal = signal(false);
  /** KPI whose quarter split is open in SCR-STRAT-03.1-M1. */
  protected readonly quarterKpiId = signal<string | null>(null);
  /** G19: what "Thiết lập mục tiêu năm" does for the selected year (null = not loaded or no access). */
  protected readonly setup = signal<TargetSetup | null>(null);

  /** W1/W2 "Theo KPI" / "Theo phòng (heatmap)" toggle; the heatmap only applies to the branch scope. */
  protected readonly overviewTab = signal<OverviewTab>('KPI');
  protected readonly heatmapLoading = signal(false);
  protected readonly heatmapFailed = signal(false);
  protected readonly heatmap = signal<HeatmapData | null>(null);
  /** W8: department whose read-only drawer is open, or null when closed. */
  protected readonly drawerDepartmentId = signal<string | null>(null);
  protected readonly reminderState = signal<ReminderState>('IDLE');
  protected readonly reminderError = signal<string | null>(null);
  /** See `TargetBmOverviewText`: pending i18n keys, read only by the heatmap/drawer templates. */
  protected readonly bmText = computed(
    () => (this.localization as unknown as { targetBm: TargetBmOverviewText }).targetBm,
  );

  protected readonly branchId = computed(
    () => this.departments().find((d) => d.levelType === 'BRANCH')?.id ?? null,
  );
  protected readonly branchName = computed(
    () => this.departments().find((d) => d.levelType === 'BRANCH')?.name ?? '',
  );
  protected readonly canManageTargets = computed(() => {
    const permissions = this.authApi.currentUser()?.permissions ?? [];
    return permissions.some((p) => {
      const code = normalizePermission(p);
      return code === TARGET_MANAGE_PERMISSION || code === FULL_ACCESS_PERMISSION;
    });
  });

  protected readonly years = computed(() => availableYears(this.periods()));
  protected readonly departmentOptions = computed(() => {
    const branch = this.branchId();
    return this.departments().filter((d) => branch && d.parentDepartmentId === branch);
  });
  protected readonly selectedDepartmentName = computed(
    () => this.departmentOptions().find((d) => d.id === this.selectedDepartmentId())?.name ?? '',
  );
  protected readonly quarterKpi = computed(
    () => this.kpis().find((k) => k.id === this.quarterKpiId()) ?? null,
  );
  protected readonly groupOptions = computed(() => [
    ...new Set(this.rows().map((row) => row.groupName)),
  ]);
  protected readonly filteredRows = computed(() => {
    const query = this.kpiSearch().trim().toLowerCase();
    const group = this.groupFilter();
    const split = this.splitFilter();
    return this.rows().filter((row) => {
      const matchesSplit =
        !split ||
        (split === 'MISMATCH'
          ? row.state === 'SHORT' || row.state === 'OVER'
          : row.state === split);
      return (
        (!query || includesText(row.kpiName, query) || includesText(row.kpiCode, query)) &&
        (!group || row.groupName === group) &&
        matchesSplit
      );
    });
  });
  protected readonly groups = computed(() => groupKpiRows(this.filteredRows()));
  /** W8 drawer content: the open department's row from the current heatmap, or null. */
  protected readonly drawerDepartment = computed(() => {
    const id = this.drawerDepartmentId();
    if (!id) {
      return null;
    }
    return this.heatmap()?.rows.find((row) => row.departmentId === id) ?? null;
  });
  protected readonly heatmapColumns = computed(() => this.heatmap()?.columns ?? []);
  protected readonly heatmapRows = computed(() => this.heatmap()?.rows ?? []);
  protected readonly heatmapBelowThresholdTotal = computed(() =>
    this.heatmapRows().reduce((sum, row) => sum + row.mismatchCount, 0),
  );
  /** Counts from the current scope's rows, before the search / group / split filters. */
  protected readonly summary = computed(() => {
    const rows = this.rows();
    return {
      total: rows.length,
      matched: rows.filter((r) => r.state === 'MATCHED').length,
      mismatch: rows.filter((r) => r.state === 'SHORT' || r.state === 'OVER').length,
      notSplit: rows.filter((r) => r.state === 'NOT_SPLIT').length,
      fixed: rows.filter((r) => r.state === 'FIXED').length,
    };
  });
  protected readonly subtitle = computed(() => {
    const text = this.localization.targetOverview;
    const scope =
      this.viewMode() === 'BRANCH' ? text.summaryScopeBranch : this.selectedDepartmentName();
    const s = this.summary();
    return text.summaryLine
      .replace('{scope}', scope)
      .replace('{year}', `${this.year() ?? ''}`)
      .replace('{count}', `${s.total}`)
      .replace('{matched}', `${s.matched}`)
      .replace('{mismatch}', `${s.mismatch}`)
      .replace('{notSplit}', `${s.notSplit}`)
      .replace('{fixed}', `${s.fixed}`);
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({
      periods: this.evaluationApi.periods(),
      kpis: fetchAllPages((page, pageSize) => this.kpiApi.list({ page, pageSize })),
      departments: fetchAllPages((page, pageSize) => this.departmentApi.list({ page, pageSize })),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ periods, kpis, departments }) => {
          this.periods.set(periods);
          this.kpis.set(kpis);
          this.departments.set(departments);
          this.year.set(this.year() ?? this.years()[0] ?? null);
          this.reload();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** G19: nothing imported yet -> import modal; a draft -> continue in the wizard; published -> no button. */
  protected openSetup(): void {
    const year = this.year();
    if (!this.canManageTargets() || !this.branchId() || year === null) {
      return;
    }
    if (this.setup()?.status === 'DRAFT') {
      void this.router.navigate(['/target-setup', year]);
      return;
    }
    this.showImportModal.set(true);
  }

  /** Always-visible "Nhập chỉ tiêu từ Excel": same modal, available until the year is published. */
  protected openImport(): void {
    if (this.canManageTargets() && this.branchId() && this.year() !== null) {
      this.showImportModal.set(true);
    }
  }

  protected closeImportModal(): void {
    this.showImportModal.set(false);
  }

  protected onImportCommitted(): void {
    this.showImportModal.set(false);
    const year = this.year();
    if (year !== null) {
      void this.router.navigate(['/target-setup', year]);
    }
  }

  /** "✦ Gợi ý chia quý": only for a target manager while the year's setup is still DRAFT. */
  protected goToQuarterSuggestion(): void {
    const year = this.year();
    if (this.canManageTargets() && year !== null) {
      void this.router.navigate(['/target-setup', year], {
        queryParams: { step: 2, suggest: 1 },
      });
    }
  }

  protected selectYear(year: number): void {
    this.year.set(year);
    this.reload();
  }

  protected selectViewMode(mode: ViewMode): void {
    if (this.viewMode() === mode) {
      return;
    }
    this.viewMode.set(mode);
    this.loading.set(true);
    this.failed.set(false);
    if (mode === 'DEPARTMENT') {
      this.notImported.set(false);
      if (!this.selectedDepartmentId()) {
        this.selectedDepartmentId.set(this.departmentOptions()[0]?.id ?? '');
      }
      this.loadDepartmentTable();
    } else {
      this.loadBranchSetupAndTable();
    }
  }

  protected selectDepartment(id: string): void {
    this.selectedDepartmentId.set(id);
    this.loading.set(true);
    this.failed.set(false);
    this.loadDepartmentTable();
  }

  /** W1/W2 tab: loads the heatmap data once, the first time "Theo phòng (heatmap)" is opened for this year. */
  protected selectOverviewTab(tab: OverviewTab): void {
    if (this.overviewTab() === tab) {
      return;
    }
    this.overviewTab.set(tab);
    if (tab === 'HEATMAP' && !this.heatmap() && !this.heatmapLoading()) {
      this.loadHeatmap();
    }
  }

  private loadHeatmap(): void {
    const branchYearId = this.setup()?.branchYearId;
    if (!branchYearId) {
      this.heatmap.set({ columns: [], rows: [] });
      return;
    }
    this.heatmapLoading.set(true);
    this.heatmapFailed.set(false);
    this.targetApi
      .getAllAllocations(branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (allocations) => {
          this.heatmap.set(
            buildAllocationHeatmap(allocations, this.rows(), this.departmentOptions()),
          );
          this.heatmapLoading.set(false);
        },
        error: () => {
          this.heatmapFailed.set(true);
          this.heatmapLoading.set(false);
        },
      });
  }

  /** W8: opens the read-only department drawer from a heatmap row. */
  protected openDepartmentDrawer(departmentId: string): void {
    this.drawerDepartmentId.set(departmentId);
    this.reminderState.set('IDLE');
    this.reminderError.set(null);
  }

  protected closeDepartmentDrawer(): void {
    this.drawerDepartmentId.set(null);
  }

  /** W8 "Xem hiệu suất phòng": switches to the department view for the drawer's department. */
  protected viewDepartmentPerformance(): void {
    const departmentId = this.drawerDepartmentId();
    if (!departmentId) {
      return;
    }
    this.closeDepartmentDrawer();
    this.selectedDepartmentId.set(departmentId);
    this.selectViewMode('DEPARTMENT');
  }

  /**
   * W8 "Nhắc Trưởng phòng". The endpoint is not in `TARGET_ALLOCATION_CONTRACT.md` yet (see
   * `TargetOverviewApiService`); shows the real success/error response, never a fabricated success.
   */
  protected remindDepartmentManager(): void {
    const departmentId = this.drawerDepartmentId();
    const branchYearId = this.setup()?.branchYearId;
    if (!departmentId || !branchYearId || this.reminderState() === 'SENDING') {
      return;
    }
    this.reminderState.set('SENDING');
    this.reminderError.set(null);
    this.overviewApi
      .remindDepartmentManager(branchYearId, departmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.reminderState.set('SENT'),
        error: (error: HttpErrorResponse) => {
          this.reminderState.set('ERROR');
          const body = error.error as ApiResponse<unknown> | undefined;
          const traceId = body?.traceId ? ` (TraceID: ${body.traceId})` : '';
          this.reminderError.set(
            (body?.message || this.localization.targetOverview.loadFailed) + traceId,
          );
        },
      });
  }

  /** The row's own unit + divisor, from its year, quarter and last-year values. */
  protected rowFormat(row: KpiQuarterRow): ColumnFormat {
    const labels = {
      billionVnd: this.localization.targetOverview.unitBillionVnd,
      millionVnd: this.localization.targetOverview.unitMillionVnd,
    };
    return columnFormat(row.unit, [row.year, ...row.quarters, row.previousYear ?? 'NONE'], labels);
  }

  /** `row.diff` shown in the row's own display unit (tỷ/triệu VND, % ...). */
  protected diffAmount(row: KpiQuarterRow, format: ColumnFormat): number {
    return Math.round((row.diff / format.divisor) * 100) / 100;
  }

  /** A percentage rounded to 1 decimal. */
  protected pct(value: number): number {
    return Math.round(value * 10) / 10;
  }

  private reload(): void {
    this.loading.set(true);
    this.failed.set(false);
    if (this.viewMode() === 'DEPARTMENT') {
      this.loadDepartmentTable();
    } else {
      this.loadBranchSetupAndTable();
    }
  }

  /** Reuses the one `getSetup` call for both the header state and, in the branch view, the table rows. */
  private loadBranchSetupAndTable(): void {
    const branchId = this.branchId();
    const year = this.year();
    this.setup.set(null);
    this.notImported.set(false);
    if (!branchId || year === null) {
      this.rows.set([]);
      this.loading.set(false);
      return;
    }
    this.targetApi
      .getSetup(branchId, year)
      .pipe(
        switchMap((setup) => {
          if (this.year() !== year) {
            return of(null);
          }
          this.setup.set(setup);
          if (!setup.branchYearId) {
            this.notImported.set(true);
            return of(null);
          }
          return this.targetApi.getQuarterSplit(setup.branchYearId);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (split) => {
          this.rows.set(split ? buildBranchQuarterRows(split.rows) : []);
          this.loading.set(false);
        },
        error: () => {
          this.setup.set(null);
          this.failed.set(true);
          this.rows.set([]);
          this.loading.set(false);
        },
      });
  }

  private loadDepartmentTable(): void {
    const year = this.year();
    const departmentId = this.selectedDepartmentId();
    this.loadHeaderSetup();
    if (year === null || !departmentId) {
      this.rows.set([]);
      this.loading.set(false);
      return;
    }
    const yearPeriod = findYearPeriod(this.periods(), year);
    const quarterPeriods = QUARTERS.map((q) => findQuarterPeriod(this.periods(), year, q));
    const previousYearPeriod = findYearPeriod(this.periods(), year - 1);
    const list = (period: EvaluationPeriodOption | undefined) =>
      period
        ? this.targetApi.list({ departmentId, evaluationPeriodId: period.id })
        : of([] as TargetAssignment[]);

    forkJoin({
      yearRows: list(yearPeriod),
      quarterRows: forkJoin(quarterPeriods.map((p) => list(p))),
      previousYearRows: list(previousYearPeriod),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ yearRows, quarterRows, previousYearRows }) => {
          this.rows.set(
            buildDepartmentQuarterRows(
              this.kpis(),
              departmentId,
              yearRows,
              quarterRows,
              previousYearRows,
            ),
          );
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.rows.set([]);
          this.loading.set(false);
        },
      });
  }

  /** Department view: `setup` is only needed for the "Gợi ý chia quý" / setup header actions. */
  private loadHeaderSetup(): void {
    const branchId = this.branchId();
    const year = this.year();
    this.setup.set(null);
    if (!this.canManageTargets() || !branchId || year === null) {
      return;
    }
    this.targetApi
      .getSetup(branchId, year)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (setup) => {
          if (this.year() === year) {
            this.setup.set(setup);
          }
        },
        error: () => this.setup.set(null),
      });
  }
}
