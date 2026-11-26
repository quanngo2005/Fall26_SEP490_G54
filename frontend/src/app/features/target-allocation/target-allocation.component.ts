import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, catchError, forkJoin, of, switchMap } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppNumberPipe } from '../../shared/pipes';
import { AllocationCellComponent } from '../../shared/target/allocation-cell/allocation-cell.component';
import { CellLegendComponent } from '../../shared/target/cell-legend/cell-legend.component';
import { KpiGroupTableComponent } from '../../shared/target/kpi-group-table/kpi-group-table.component';
import { RightDrawerComponent } from '../../shared/target/right-drawer/right-drawer.component';
import { SuggestAllocationPopoverComponent } from '../../shared/target/suggest-allocation-popover/suggest-allocation-popover.component';
import { SuggestionPreviewBannerComponent } from '../../shared/target/suggestion-preview-banner/suggestion-preview-banner.component';
import {
  AllocationCellState,
  KpiGroupTableGroup,
  KpiGroupTableRow,
  SuggestAllocationRequest,
  SuggestionBufferUnitOption,
  TierStatusCounts,
  TierStatusFilter,
} from '../../shared/target/target-shared.models';
import { TierStatusLineComponent } from '../../shared/target/tier-status-line/tier-status-line.component';
import {
  availableYears,
  fetchAllPages,
  findYearPeriod,
  includesText,
} from '../target-overview/target-overview.utils';
import { TargetAllocationService } from '../../core/target-allocation.service';
import {
  EMPLOYEE_QUARTER_KEYS,
  EmployeeAllocation,
  EmployeeAllocationPeriodValues,
  EmployeePeriodKey,
  SaveEmployeeAllocationEmployeeRequest,
  SuggestionEngineRequest,
} from '../../core/target-allocation.models';
import { rebalanceQuarters, splitByQuarterShape } from './target-allocation.utils';
import { SuggestButtonComponent } from '../../shared/target/suggest-button/suggest-button.component';

/** Draft values for one employee of one KPI, by period key. */
type DraftRow = EmployeeAllocationPeriodValues;
/** `draft[kpiId][employeeId]`. */
type Draft = Record<string, Record<string, DraftRow>>;

interface HistoryEntry {
  readonly at: string;
  readonly text: string;
}

const ZERO: DraftRow = { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 };

/**
 * W9/W10/W11 (Target Allocation v3, docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md): a department manager
 * splits each of the department's published (tier-2 ACTIVE) KPI targets across its own employees —
 * KPIs as rows, employees as columns (W10) — then fine-tunes one employee's year/Q1-Q4 split and history
 * in a right-hand drawer (W11). `docs/api/TARGET_ALLOCATION_CONTRACT.md`, Phase 3.
 */
@Component({
  selector: 'app-target-allocation',
  imports: [
    SuggestButtonComponent,
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    AppNumberPipe,
    AllocationCellComponent,
    CellLegendComponent,
    KpiGroupTableComponent,
    RightDrawerComponent,
    SuggestAllocationPopoverComponent,
    SuggestionPreviewBannerComponent,
    TierStatusLineComponent,
  ],
  templateUrl: './target-allocation.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/target/target-page.scss',
    './target-allocation.component.scss',
  ],
})
export class TargetAllocationComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly allocationApi = inject(TargetAllocationService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly noAccess = signal(false);
  protected readonly saving = signal(false);
  protected readonly notice = signal<{ kind: 'success' | 'error'; text: string } | null>(null);

  protected readonly departmentName = signal('');
  private departmentId = '';
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly year = signal<number | null>(null);
  private readonly kpis = signal<KpiDefinition[]>([]);
  /** One entry per allocatable (SUM/AVG) KPI with a published tier-2 department-year row. */
  protected readonly rows = signal<EmployeeAllocation[]>([]);
  /** Any one department-year row id: the Phase-3 bulk endpoints accept any KPI's anchor. */
  private readonly anchorDeptYearId = signal<string | null>(null);
  protected readonly status = signal<string>('DRAFT');

  protected readonly tab = signal<string>('');
  protected readonly search = signal('');
  protected readonly filter = signal<TierStatusFilter>('ALL');
  protected readonly suggestOpen = signal(false);
  protected readonly selectedEmployeeId = signal<string | null>(null);

  /** Local, unsaved edits layered over the loaded rows; also used for the drawer. */
  protected readonly draft = signal<Draft>({});
  /** `kpiId|employeeId` cells the suggestion engine just filled, not yet applied. */
  protected readonly suggested = signal<ReadonlySet<string> | null>(null);
  private readonly suggestedKpiCount = signal(0);
  private lastSuggestBasis: SuggestAllocationRequest['basis'] = 'TARGET_LAST_YEAR';
  protected readonly suggestRemainder =
    signal<SuggestAllocationRequest['rounding']['remainderMethod']>('LARGEST_REMAINDER');
  protected readonly suggestFallbackCount = signal(0);
  protected readonly suggestWarnings = signal<readonly string[]>([]);
  private beforeSuggest: Draft | null = null;
  private readonly history = signal<HistoryEntry[]>([]);
  private autosaveHandle: ReturnType<typeof setTimeout> | null = null;
  private yearLoad: Subscription | null = null;

  protected readonly years = computed(() => availableYears(this.periods()));

  /** Tab = the job position most of a KPI's employees hold; `'Khác'` when a row has none. */
  protected readonly tabs = computed(() => {
    const counts = new Map<string, { kpis: number; employees: Set<string> }>();
    for (const row of this.rows()) {
      const label = this.positionOf(row);
      const entry = counts.get(label) ?? { kpis: 0, employees: new Set<string>() };
      entry.kpis += 1;
      row.employees.forEach((e) => entry.employees.add(e.employeeId));
      counts.set(label, entry);
    }
    return [...counts].map(([label, { kpis, employees }]) => ({
      label,
      kpiCount: kpis,
      employeeCount: employees.size,
    }));
  });

  /** KPI rows of the active tab. */
  protected readonly tabRows = computed(() =>
    this.rows().filter((row) => this.positionOf(row) === (this.tab() || this.tabs()[0]?.label)),
  );

  /** Every employee appearing in the active tab's KPI rows, deduplicated, sorted by name. */
  protected readonly employeeColumns = computed(() => {
    const byId = new Map<string, { id: string; name: string; isNew: boolean }>();
    for (const row of this.tabRows()) {
      for (const e of row.employees) {
        const current = byId.get(e.employeeId);
        const isNew = e.previousYear === null && e.previousYearActual === null;
        byId.set(e.employeeId, {
          id: e.employeeId,
          name: e.employeeName,
          isNew: current ? current.isNew && isNew : isNew,
        });
      }
    }
    return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name));
  });

  protected readonly newEmployeeCount = computed(
    () => this.employeeColumns().filter((e) => e.isNew).length,
  );

  /** Grouped KPI rows (by KPI group) for the group table, filtered by search / mismatch chip. */
  protected readonly groups = computed((): KpiGroupTableGroup[] => {
    const query = this.search().trim().toLowerCase();
    const groups = new Map<string, { name: string; rows: KpiGroupTableRow[] }>();
    for (const row of this.tabRows()) {
      const kpi = this.kpiOf(row.kpiId);
      const matched = this.kpiMatched(row);
      if (this.filter() === 'MISMATCHED' && matched) continue;
      if (query && !includesText(row.kpiName, query)) continue;
      const groupId = kpi?.kpiGroupId ?? 'other';
      const groupName = kpi?.kpiGroupName ?? this.localization.targetDm.groupOther;
      const entry = groups.get(groupId) ?? { name: groupName, rows: [] as KpiGroupTableRow[] };
      entry.rows.push({ id: row.kpiId, kpiName: row.kpiName, kpiType: row.unit });
      groups.set(groupId, entry);
    }
    return [...groups].map(([id, { name, rows }]) => ({
      id,
      name,
      complete: rows.every((r) => this.kpiMatched(this.rowOf(r.id)!)),
      rows,
    }));
  });

  /** Mismatch/not-split counts across the active tab's allocatable KPIs (TierStatusLine). */
  protected readonly counts = computed((): TierStatusCounts => {
    const rows = this.tabRows();
    const matched = rows.filter((r) => this.kpiMatched(r)).length;
    const notSplit = rows.filter(
      (r) => r.employees.length === 0 && r.lockedEmployees.length === 0,
    ).length;
    return {
      matched,
      total: rows.length,
      mismatched: rows.length - matched - notSplit,
      notSplit,
    };
  });

  /** D12/D13: once a KPI's employee-year rows are ACTIVE, the grid for it is read-only here. */
  protected readonly published = computed(() => this.status() === 'ACTIVE');

  /** Every KPI of the department (both tabs) must match in all 5 periods to publish (BE publishes per department). */
  protected readonly canPublish = computed(
    () =>
      !this.published() && this.rows().length > 0 && this.rows().every((r) => this.kpiMatched(r)),
  );

  protected readonly selectedEmployee = computed(
    () => this.employeeColumns().find((e) => e.id === this.selectedEmployeeId()) ?? null,
  );

  protected readonly historyEntries = computed(() => this.history());

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.noAccess.set(false);
    this.employeeApi
      .me()
      .pipe(
        switchMap((me) =>
          forkJoin({
            periods: this.evaluationApi.periods(),
            kpis: fetchAllPages((page, pageSize) => this.kpiApi.list({ page, pageSize })),
          }).pipe(switchMap((data) => of({ ...data, me }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ periods, kpis, me }) => {
          this.departmentId = me.departmentId;
          this.departmentName.set(me.departmentName);
          this.kpis.set(kpis);
          this.periods.set(periods);
          const query = this.route.snapshot.queryParamMap;
          const today = new Date();
          this.year.set(Number(query.get('year')) || today.getFullYear());
          this.loadYear();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectYear(year: number): void {
    this.year.set(year);
    void this.router.navigate([], {
      queryParams: { year },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.loadYear();
  }

  private loadYear(): void {
    this.yearLoad?.unsubscribe();
    if (this.autosaveHandle) clearTimeout(this.autosaveHandle);
    this.loading.set(true);
    this.failed.set(false);
    this.noAccess.set(false);
    this.notice.set(null);
    this.closeDrawer();
    this.suggested.set(null);
    this.history.set([]);
    const year = this.year();
    const yearPeriod = year === null ? undefined : findYearPeriod(this.periods(), year);
    if (!yearPeriod) {
      this.rows.set([]);
      this.anchorDeptYearId.set(null);
      this.loading.set(false);
      return;
    }
    this.yearLoad = this.targetApi
      .list({
        departmentId: this.departmentId,
        evaluationPeriodId: yearPeriod.id,
        status: 'ACTIVE',
      })
      .pipe(
        switchMap((assignments) => {
          const anchor = assignments.find((a) => !a.employeeId)?.id ?? null;
          this.anchorDeptYearId.set(anchor);
          return anchor ? this.allocationApi.getEmployeeAllocations(anchor) : of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (rows) => {
          this.rows.set(rows);
          this.status.set(rows[0]?.status ?? 'DRAFT');
          this.draft.set(this.draftFrom(rows));
          this.restoreAutosave();
          const firstTab = this.tabsFrom(rows)[0];
          this.tab.set(firstTab ?? '');
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.noAccess.set(error.status === 403 || error.status === 404);
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  private tabsFrom(rows: readonly EmployeeAllocation[]): string[] {
    return [...new Set(rows.map((row) => this.positionOf(row)))];
  }

  protected selectTab(label: string): void {
    this.tab.set(label);
    this.search.set('');
    this.filter.set('ALL');
  }

  protected setFilter(filter: TierStatusFilter): void {
    this.filter.set(filter);
  }

  // ---------------------------------------------------------------------------
  // Grid editing
  // ---------------------------------------------------------------------------

  protected rowOf(kpiId: string): EmployeeAllocation | undefined {
    return this.rows().find((r) => r.kpiId === kpiId);
  }

  protected kpiOf(kpiId: string): KpiDefinition | undefined {
    return this.kpis().find((k) => k.id === kpiId);
  }

  protected isRatio(kpiId: string): boolean {
    const kpi = this.kpiOf(kpiId);
    return kpi?.aggregationType === 'RATIO' || kpi?.aggregationType === 'NO_AGGREGATION';
  }

  /** Department target shown as the row's read-only ceiling column. */
  protected departmentTarget(kpiId: string): number {
    return this.rowOf(kpiId)?.department.year ?? 0;
  }

  /** What is left of the department's year target once every employee's draft is counted. */
  protected remaining(kpiId: string): number {
    const row = this.rowOf(kpiId);
    if (!row) return 0;
    const sum = Object.values(this.draft()[kpiId] ?? {}).reduce((acc, v) => acc + v.year, 0);
    return row.department.year - sum;
  }

  protected cellYear(kpiId: string, employeeId: string): number | null {
    return this.draft()[kpiId]?.[employeeId]?.year ?? null;
  }

  protected isLockedEmployee(kpiId: string, employeeId: string): boolean {
    return this.rowOf(kpiId)?.lockedEmployees.some((l) => l.employeeId === employeeId) ?? false;
  }

  protected lockedReason(kpiId: string, employeeId: string): string | null {
    const row = this.rowOf(kpiId);
    const locked = row?.lockedEmployees.find((l) => l.employeeId === employeeId);
    return locked ? this.localization.targetDm.lockedNotResponsible : null;
  }

  protected cellState(kpiId: string, employeeId: string): AllocationCellState {
    if (this.published()) return 'LOCKED';
    if (this.isLockedEmployee(kpiId, employeeId)) return 'LOCKED';
    if (this.suggested()?.has(`${kpiId}|${employeeId}`)) return 'SUGGESTED';
    if (!this.kpiMatched(this.rowOf(kpiId)!)) return 'MISMATCH';
    return this.cellYear(kpiId, employeeId) !== null ? 'MANUAL' : 'DEFAULT';
  }

  /** Editing an employee's year cell spreads it over Q1..Q4 by the department's own quarter shape. */
  protected editYear(kpiId: string, employeeId: string, raw: number): void {
    const row = this.rowOf(kpiId);
    if (!row || this.published() || this.isLockedEmployee(kpiId, employeeId)) return;
    const year = Math.max(0, Math.floor(raw));
    const [q1, q2, q3, q4] = splitByQuarterShape(year, [
      row.department.q1,
      row.department.q2,
      row.department.q3,
      row.department.q4,
    ]);
    this.setDraftRow(kpiId, employeeId, { year, q1, q2, q3, q4 });
    this.pushHistory(
      this.i18n.translate('targetDm.historyManualEdit', {
        employee: this.nameOf(employeeId),
        kpi: row.kpiName,
        value: this.i18n.formatNumber(year),
      }),
    );
    this.suggested.update((set) => (set ? without(set, `${kpiId}|${employeeId}`) : set));
    this.scheduleAutosave();
  }

  /** Whole-year change of the department target against last year, e.g. `+9,1%` (empty without history). */
  protected departmentDelta(kpiId: string): string {
    const row = this.rowOf(kpiId);
    return row ? this.deltaText(row.department.year, row.department.previousYear ?? null) : '';
  }

  protected departmentDeltaClass(kpiId: string): string {
    const row = this.rowOf(kpiId);
    return row ? this.deltaClass(row.department.year, row.department.previousYear ?? null) : '';
  }

  protected compared(kpiId: string, employeeId: string): number | null {
    const row = this.rowOf(kpiId);
    return row?.employees.find((e) => e.employeeId === employeeId)?.previousYear ?? null;
  }

  protected deltaText(value: number | null, base: number | null): string {
    const change = this.deltaChange(value, base);
    if (change === null) return '';
    const amount = this.i18n.formatNumber(Math.abs(change), 1);
    return change === 0 ? `= ${amount}%` : `${change > 0 ? '+' : '−'}${amount}%`;
  }

  protected deltaClass(value: number | null, base: number | null): string {
    const change = this.deltaChange(value, base);
    return change === null || change === 0 ? '' : change > 0 ? 'is-up' : 'is-down';
  }

  private deltaChange(value: number | null, base: number | null): number | null {
    if (value === null || base === null || base === 0) return null;
    return Math.round(((value - base) / base) * 1000) / 10;
  }

  private positionOf(row: EmployeeAllocation): string {
    return row.employees[0]?.jobPositionName ?? this.localization.targetDm.positionOther;
  }

  /** A KPI row is "matched" when Σ employees = department for every one of the 5 periods (year, Q1-Q4). */
  private kpiMatched(row: EmployeeAllocation): boolean {
    if (this.isRatio(row.kpiId)) return true;
    const kpiDraft = this.draft()[row.kpiId] ?? {};
    const eligible = row.employees.map((e) => e.employeeId);
    if (eligible.length === 0) return row.lockedEmployees.length > 0; // nobody eligible: nothing to split
    const totals: DraftRow = eligible.reduce<DraftRow>((acc, id) => {
      const v = kpiDraft[id] ?? ZERO;
      return {
        year: acc.year + v.year,
        q1: acc.q1 + v.q1,
        q2: acc.q2 + v.q2,
        q3: acc.q3 + v.q3,
        q4: acc.q4 + v.q4,
      };
    }, ZERO);
    const quartersOk = eligible.every((id) => {
      const v = kpiDraft[id] ?? ZERO;
      return v.q1 + v.q2 + v.q3 + v.q4 === v.year;
    });
    return (
      quartersOk &&
      totals.year === row.department.year &&
      totals.q1 === row.department.q1 &&
      totals.q2 === row.department.q2 &&
      totals.q3 === row.department.q3 &&
      totals.q4 === row.department.q4
    );
  }

  // ---------------------------------------------------------------------------
  // Suggestion popover (tier 3)
  // ---------------------------------------------------------------------------

  protected openSuggest(): void {
    this.suggestOpen.set(true);
  }

  protected cancelSuggest(): void {
    this.suggestOpen.set(false);
  }

  /** Calls the suggestion engine once per allocatable KPI of the active tab and merges the results. */
  protected createSuggestion(request: SuggestAllocationRequest): void {
    this.suggestOpen.set(false);
    this.beforeSuggest = this.draft();
    this.lastSuggestBasis = request.basis;
    this.suggestRemainder.set(request.rounding.remainderMethod);
    const targets = this.tabRows().filter(
      (row) =>
        !this.isRatio(row.kpiId) &&
        !this.published() &&
        (request.scope !== 'UNMATCHED' || !this.kpiMatched(row)),
    );
    const calls = targets.map((row) => {
      const current = row.employees.map((e) => ({
        unitId: e.employeeId,
        ...this.draftCurrentValues(row.kpiId, e.employeeId),
        locked: false,
      }));
      const engineRequest: SuggestionEngineRequest = {
        tier: 3,
        basis: request.basis,
        anchorId: row.deptYearId,
        employees: request.employeeScope === 'NEW_ONLY' ? 'NEW_ONLY' : 'ALL',
        periods: 'YEAR_PLUS_4Q',
        overwrite: request.applyMode === 'OVERWRITE_UNLOCKED' ? 'UNLOCKED' : 'EMPTY_ONLY',
        currentValues: current,
        rounding: {
          moneyStep: request.rounding.moneyStep,
          countStep: request.rounding.countStep,
          mode: request.rounding.mode,
        },
        remainder: {
          method:
            request.rounding.remainderMethod === 'BUFFER_UNIT' ? 'BUFFER' : 'LARGEST_REMAINDER',
          bufferUnitId: request.rounding.bufferUnitId,
        },
      };
      return this.allocationApi.suggest(this.year() ?? new Date().getFullYear(), engineRequest);
    });
    if (calls.length === 0) return;
    forkJoin(calls)
      .pipe(
        catchError((error: HttpErrorResponse) => {
          this.notice.set({ kind: 'error', text: this.errorText(error) });
          return of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((responses) => {
        const filled = new Set<string>();
        const filledKpis = new Set<string>();
        const warnings = new Set<string>();
        let fallbackCount = 0;
        for (const response of responses) {
          response.warnings.forEach((w) => warnings.add(w));
          for (const kpiResult of response.kpis) {
            kpiResult.warnings.forEach((w) => warnings.add(w));
            fallbackCount += kpiResult.cells.some((cell) => cell.usedFallbackAverage) ? 1 : 0;
            const row = this.rowOf(kpiResult.kpiId);
            for (const cell of kpiResult.cells) {
              // Only cells the engine really filled change; fixed cells keep what the grid already has.
              if (cell.year === null || !cell.overwritten || !row) continue;
              const [q1, q2, q3, q4] =
                request.periodScope === 'YEAR_ONLY'
                  ? splitByQuarterShape(cell.year, [
                      row.department.q1,
                      row.department.q2,
                      row.department.q3,
                      row.department.q4,
                    ])
                  : [cell.q1 ?? 0, cell.q2 ?? 0, cell.q3 ?? 0, cell.q4 ?? 0];
              this.setDraftRow(kpiResult.kpiId, cell.unitId, { year: cell.year, q1, q2, q3, q4 });
              filled.add(`${kpiResult.kpiId}|${cell.unitId}`);
              filledKpis.add(kpiResult.kpiId);
            }
          }
        }
        this.suggestedKpiCount.set(filledKpis.size);
        this.suggestFallbackCount.set(fallbackCount);
        if (filled.size === 0 && responses.length > 0) {
          warnings.add(this.localization.targetDm.suggestNothingToFill);
        }
        this.suggestWarnings.set([...warnings]);
        this.suggested.set(filled);
        this.pushHistory(
          this.i18n.translate('targetDm.historySuggested', {
            count: filled.size,
            basis:
              request.basis === 'ACTUAL_LAST_YEAR'
                ? this.localization.targetShared.suggestPopover.basisActualLastYear
                : this.localization.targetShared.suggestPopover.basisTargetLastYear,
          }),
        );
        this.scheduleAutosave();
      });
  }

  protected readonly suggestedCellCount = computed(() => this.suggested()?.size ?? 0);
  protected readonly suggestedKpis = computed(() => this.suggestedKpiCount());
  protected readonly suggestBasis = computed(() => this.lastSuggestBasis);

  /** Buffer-unit choices for the popover's "remainder = buffer" mode: the active tab's employees. */
  protected readonly bufferUnitOptions = computed<SuggestionBufferUnitOption[]>(() =>
    this.employeeColumns().map((employee) => ({ id: employee.id, label: employee.name })),
  );

  protected applySuggestion(): void {
    this.suggested.set(null);
    this.beforeSuggest = null;
  }

  protected undoSuggestion(): void {
    if (this.beforeSuggest) this.draft.set(this.beforeSuggest);
    this.applySuggestion();
  }

  private draftCurrentValues(kpiId: string, employeeId: string) {
    const v = this.draft()[kpiId]?.[employeeId];
    // A cell nobody has filled yet comes back from the server as 0; send it as empty so "empty only" can fill it.
    const filled = v && (v.year !== 0 || v.q1 !== 0 || v.q2 !== 0 || v.q3 !== 0 || v.q4 !== 0);
    return v && filled
      ? { year: v.year, q1: v.q1, q2: v.q2, q3: v.q3, q4: v.q4 }
      : { year: null, q1: null, q2: null, q3: null, q4: null };
  }

  // ---------------------------------------------------------------------------
  // Drawer (W11)
  // ---------------------------------------------------------------------------

  protected openEmployee(employeeId: string): void {
    this.selectedEmployeeId.set(employeeId);
  }

  protected closeDrawer(): void {
    this.selectedEmployeeId.set(null);
  }

  protected drawerKpis(): EmployeeAllocation[] {
    const id = this.selectedEmployeeId();
    return id === null
      ? []
      : this.tabRows().filter((row) => row.employees.some((e) => e.employeeId === id));
  }

  protected drawerValue(kpiId: string, period: EmployeePeriodKey): number {
    const id = this.selectedEmployeeId();
    if (!id) return 0;
    return this.draft()[kpiId]?.[id]?.[period] ?? 0;
  }

  /** W11: editing one quarter of the open employee keeps the year fixed and rebalances the other three. */
  protected editQuarter(kpiId: string, quarterIndex: number, raw: number): void {
    const id = this.selectedEmployeeId();
    const row = this.rowOf(kpiId);
    if (!id || !row || this.published() || this.isLockedEmployee(kpiId, id)) return;
    const index = Math.min(3, Math.max(0, Math.trunc(quarterIndex))) as 0 | 1 | 2 | 3;
    const current = this.draft()[kpiId]?.[id] ?? ZERO;
    const [q1, q2, q3, q4] = rebalanceQuarters(
      current.year,
      [current.q1, current.q2, current.q3, current.q4],
      index,
      raw,
    );
    this.setDraftRow(kpiId, id, { year: current.year, q1, q2, q3, q4 });
    this.pushHistory(
      this.i18n.translate('targetDm.historyQuarterEdit', {
        employee: this.nameOf(id),
        kpi: row.kpiName,
        quarter: quarterIndex + 1,
      }),
    );
    this.scheduleAutosave();
  }

  protected nameOf(employeeId: string): string {
    return this.employeeColumns().find((e) => e.id === employeeId)?.name ?? '';
  }

  /** PUT is per-KPI and replaces the full employee set, so every affected KPI is saved in one request each. */
  protected saveEmployee(): void {
    const id = this.selectedEmployeeId();
    if (!id || this.saving()) return;
    this.saving.set(true);
    this.notice.set(null);
    const requests = this.drawerKpis().map((row) => {
      const kpiDraft = this.draft()[row.kpiId] ?? {};
      const employees: SaveEmployeeAllocationEmployeeRequest[] = row.employees.map((e) => {
        const v = kpiDraft[e.employeeId] ?? ZERO;
        return { employeeId: e.employeeId, ...v };
      });
      return this.allocationApi.saveEmployeeAllocation(row.deptYearId, { employees });
    });
    forkJoin(requests)
      .pipe(
        catchError((error: HttpErrorResponse) => {
          this.notice.set({ kind: 'error', text: this.errorText(error) });
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((results) => {
        this.saving.set(false);
        if (!results) return;
        this.notice.set({
          kind: 'success',
          text: this.i18n.translate('targetDm.savedForEmployee', { name: this.nameOf(id) }),
        });
        this.loadYear();
      });
  }

  // ---------------------------------------------------------------------------
  // Publish
  // ---------------------------------------------------------------------------

  protected publish(): void {
    const anchor = this.anchorDeptYearId();
    if (!anchor || !this.canPublish() || this.saving()) return;
    this.saving.set(true);
    this.notice.set(null);
    this.allocationApi
      .publishEmployees(anchor)
      .pipe(
        catchError((error: HttpErrorResponse) => {
          this.notice.set({ kind: 'error', text: this.errorText(error) });
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.saving.set(false);
        if (!result) return;
        this.notice.set({ kind: 'success', text: this.localization.targetDm.published });
        this.clearAutosave();
        this.loadYear();
      });
  }

  // ---------------------------------------------------------------------------
  // Draft helpers / autosave
  // ---------------------------------------------------------------------------

  private draftFrom(rows: readonly EmployeeAllocation[]): Draft {
    const draft: Draft = {};
    for (const row of rows) {
      draft[row.kpiId] = {};
      for (const e of row.employees) {
        draft[row.kpiId][e.employeeId] = { year: e.year, q1: e.q1, q2: e.q2, q3: e.q3, q4: e.q4 };
      }
    }
    return draft;
  }

  private setDraftRow(kpiId: string, employeeId: string, value: DraftRow): void {
    this.draft.update((all) => ({
      ...all,
      [kpiId]: { ...all[kpiId], [employeeId]: value },
    }));
  }

  private pushHistory(text: string): void {
    const at = new Date().toTimeString().slice(0, 5);
    this.history.update((all) => [{ at, text }, ...all].slice(0, 20));
  }

  private autosaveKey(): string {
    return `ta-v3-dm-draft:${this.departmentId}:${this.year() ?? ''}`;
  }

  private scheduleAutosave(): void {
    if (this.autosaveHandle) clearTimeout(this.autosaveHandle);
    const key = this.autosaveKey();
    this.autosaveHandle = setTimeout(() => {
      try {
        localStorage.setItem(key, JSON.stringify(this.draft()));
      } catch {
        // Storage unavailable (private mode, quota): the draft just stays in memory for this tab.
      }
    }, 600);
  }

  private restoreAutosave(): void {
    try {
      const raw = localStorage.getItem(this.autosaveKey());
      if (raw) this.draft.set({ ...this.draft(), ...(JSON.parse(raw) as Draft) });
    } catch {
      // Ignore a corrupt/unavailable draft and keep the values loaded from the server.
    }
  }

  private clearAutosave(): void {
    try {
      localStorage.removeItem(this.autosaveKey());
    } catch {
      // Nothing to clean up if storage is unavailable.
    }
  }

  private errorText(error: HttpErrorResponse): string {
    const traceId = (error.error as { traceId?: string } | null)?.traceId;
    const message =
      (error.error as { message?: string } | null)?.message ??
      this.localization.targetDm.saveFailed;
    return traceId
      ? `${message} (${this.i18n.translate('targetDm.traceId', { traceId })})`
      : message;
  }

  protected readonly quarterKeys = EMPLOYEE_QUARTER_KEYS;
}

function without<T>(set: ReadonlySet<T>, value: T): ReadonlySet<T> {
  const next = new Set(set);
  next.delete(value);
  return next;
}
