import {
  Component,
  computed,
  DestroyRef,
  EventEmitter,
  inject,
  Input,
  OnChanges,
  Output,
  signal,
  SimpleChanges,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { QuarterSplitRow, TargetSetup } from '../../../core/target-assignment.models';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import { AllocationCellComponent } from '../../../shared/target/allocation-cell/allocation-cell.component';
import { CellLegendComponent } from '../../../shared/target/cell-legend/cell-legend.component';
import { SuggestAllocationPopoverComponent } from '../../../shared/target/suggest-allocation-popover/suggest-allocation-popover.component';
import { SuggestionPreviewBannerComponent } from '../../../shared/target/suggestion-preview-banner/suggestion-preview-banner.component';
import {
  AllocationCellState,
  SuggestAllocationRequest,
  SuggestionBufferUnitOption,
  TierStatusFilter,
} from '../../../shared/target/target-shared.models';
import { TierStatusLineComponent } from '../../../shared/target/tier-status-line/tier-status-line.component';
import { QUARTERS } from '../quarter-suggestion.utils';
import { formatUnits, numberLocale } from '../target-math';
import { CELL_KEYS, CellKey, Tier2AllocationStore, Tier2KpiState } from '../tier2-allocation.store';
import { SuggestButtonComponent } from '../../../shared/target/suggest-button/suggest-button.component';

const TOAST_MS = 3000;

interface MatrixColumn {
  row: QuarterSplitRow;
  state: Tier2KpiState;
  ready: boolean;
}

interface MatrixGroup {
  groupCode: string;
  groupName: string;
  columns: MatrixColumn[];
}

interface MatrixDepartment {
  departmentId: string;
  departmentName: string;
}

function normalizeSearchText(value: string): string {
  return value.trim().toLocaleLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
}

/**
 * W6 (design/v3/w06-t2-matrix.png): tier 2 as a department x KPI matrix, one period (year or a quarter) at a
 * time, KPI columns grouped by KPI group. Cells are edited in place; a column header opens that KPI in the
 * "Theo chỉ tiêu" view. Suggestions go only through "✦ Gợi ý phân bổ" (`POST /target-suggestions/{year}`);
 * "Tiếp: Phát hành" stays disabled while any KPI is mismatched or not split.
 */
@Component({
  selector: 'app-department-allocation-matrix',
  imports: [
    SuggestButtonComponent,
    LayoutIconComponent,
    AllocationCellComponent,
    CellLegendComponent,
    SuggestAllocationPopoverComponent,
    SuggestionPreviewBannerComponent,
    TierStatusLineComponent,
  ],
  providers: [Tier2AllocationStore],
  templateUrl: './department-allocation-matrix.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/target/target-page.scss',
    '../branch-quarter-split/branch-quarter-split.component.scss',
    '../branch-quarter-split/branch-quarter-split.component.part2.scss',
    '../department-allocation/department-allocation.component.scss',
    './department-allocation-matrix.component.scss',
  ],
})
export class DepartmentAllocationMatrixComponent extends LocalizedComponent implements OnChanges {
  protected readonly store = inject(Tier2AllocationStore);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) branchYearId!: string;
  @Input({ required: true }) year!: number;
  @Input({ required: true }) set setup(value: TargetSetup) {
    this.setupState.set(value);
  }
  @Output() readonly saved = new EventEmitter<void>();
  @Output() readonly back = new EventEmitter<void>();
  @Output() readonly publish = new EventEmitter<void>();
  /** A column header was clicked: open that KPI in the "Theo chỉ tiêu" view. */
  @Output() readonly openKpi = new EventEmitter<string>();

  protected readonly periods = CELL_KEYS;
  protected readonly setupState = signal<TargetSetup | null>(null);
  protected readonly period = signal<CellKey>('year');
  protected readonly search = signal('');
  protected readonly filter = signal<TierStatusFilter>('ALL');
  protected readonly showSuggest = signal(false);
  protected readonly toast = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
  }

  isDirty(): boolean {
    return this.store.dirtyKpiIds().length > 0;
  }

  protected readonly readOnly = computed(
    () => this.setupState()?.status === 'ACTIVE' || this.store.readOnly(),
  );

  /** Every department that appears in any KPI (eligible or hidden), in first-seen order. */
  private readonly departments = computed<MatrixDepartment[]>(() => {
    const seen = new Map<string, MatrixDepartment>();
    for (const allocation of this.store.allocations()) {
      for (const d of [...allocation.departments, ...allocation.hiddenDepartments]) {
        if (!seen.has(d.departmentId)) {
          seen.set(d.departmentId, {
            departmentId: d.departmentId,
            departmentName: d.departmentName,
          });
        }
      }
    }
    return [...seen.values()];
  });

  private readonly searchTerm = computed(() => normalizeSearchText(this.search()));
  /** The search text names a department: filter rows; otherwise it filters KPI columns. */
  private readonly searchesDepartments = computed(() => {
    const term = this.searchTerm();
    return (
      term !== '' &&
      this.departments().some((d) => normalizeSearchText(d.departmentName).includes(term))
    );
  });

  protected readonly visibleDepartments = computed(() => {
    const term = this.searchTerm();
    return this.searchesDepartments()
      ? this.departments().filter((d) => normalizeSearchText(d.departmentName).includes(term))
      : this.departments();
  });

  protected readonly groups = computed<MatrixGroup[]>(() => {
    const term = this.searchesDepartments() ? '' : this.searchTerm();
    const filter = this.filter();
    const groups: MatrixGroup[] = [];
    const byCode = new Map<string, MatrixGroup>();
    for (const row of this.store.kpiRows()) {
      const state = this.store.kpiState(row.kpiId);
      const matchesFilter =
        filter === 'ALL' ||
        (filter === 'MISMATCHED' && state === 'MISMATCHED') ||
        (filter === 'NOT_SPLIT' && state === 'NOT_SPLIT');
      const matchesSearch =
        !term ||
        normalizeSearchText(row.kpiName).includes(term) ||
        normalizeSearchText(row.kpiCode).includes(term);
      if (!matchesFilter || !matchesSearch) {
        continue;
      }
      let group = byCode.get(row.groupCode);
      if (!group) {
        group = { groupCode: row.groupCode, groupName: row.groupName, columns: [] };
        byCode.set(row.groupCode, group);
        groups.push(group);
      }
      group.columns.push({ row, state, ready: this.store.allocation(row.kpiId) !== null });
    }
    return groups;
  });

  protected readonly columns = computed(() => this.groups().flatMap((group) => group.columns));

  protected readonly bufferUnitOptions = computed<SuggestionBufferUnitOption[]>(() =>
    this.departments().map((d) => ({ id: d.departmentId, label: d.departmentName })),
  );

  protected readonly canGoNext = computed(
    () =>
      this.store.allMatched() &&
      this.store.dirtyKpiIds().length === 0 &&
      this.setupState()?.canPublish === true,
  );

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['branchYearId']) {
      this.load();
    }
  }

  protected load(): void {
    if (this.branchYearId) {
      this.store.load(this.branchYearId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    }
  }

  // ---- text -------------------------------------------------------------------------------------

  protected periodLabel(key: CellKey): string {
    const text = this.localization.departmentAllocationMatrix;
    return key === 'year'
      ? text.periodYear
      : text.periodQuarter.replace('{quarter}', `${QUARTERS.indexOf(key) + 1}`);
  }

  protected columnChip(column: MatrixColumn): string {
    const text = this.localization.departmentAllocationMatrix;
    if (column.state === 'MATCHED') {
      return text.chipMatched;
    }
    if (column.state === 'NOT_SPLIT') {
      return text.chipNotSplit;
    }
    const remaining =
      this.store.check(column.row.kpiId)?.columns[this.period()].remainingUnits ?? 0n;
    return remaining === 0n
      ? text.chipMismatched
      : `${text.chipMismatched} ${this.formatUnits(remaining < 0n ? -remaining : remaining)}`;
  }

  protected headerTitle(column: MatrixColumn): string {
    return this.localization.departmentAllocationMatrix.openKpiHint.replace(
      '{name}',
      column.row.kpiName,
    );
  }

  /** `eligible` / `hidden` (not applicable for the KPI's work type) / `none` (KPI not ready yet). */
  protected cellKind(column: MatrixColumn, departmentId: string): 'eligible' | 'hidden' | 'none' {
    const allocation = this.store.allocation(column.row.kpiId);
    if (!allocation) {
      return 'none';
    }
    if (allocation.departments.some((d) => d.departmentId === departmentId)) {
      return 'eligible';
    }
    return allocation.hiddenDepartments.some((d) => d.departmentId === departmentId)
      ? 'hidden'
      : 'none';
  }

  protected cellValue(column: MatrixColumn, departmentId: string): number | null {
    const value = this.store.values(column.row.kpiId, departmentId)?.[this.period()] ?? null;
    return value === 0 && column.state === 'NOT_SPLIT' ? null : value;
  }

  protected cellState(column: MatrixColumn, departmentId: string): AllocationCellState {
    return this.store.cellState(column.row.kpiId, departmentId, this.period());
  }

  protected onCellChange(column: MatrixColumn, departmentId: string, value: number): void {
    this.store.setCell(column.row.kpiId, departmentId, this.period(), value);
  }

  protected lockedReason(): string {
    return this.localization.departmentAllocation.lockedQuarterReason;
  }

  protected totalText(column: MatrixColumn): string {
    const check = this.store.check(column.row.kpiId);
    if (!check) {
      return '—';
    }
    const cell = check.columns[this.period()];
    return `${this.formatUnits(cell.totalUnits)} / ${this.formatUnits(cell.branchUnits)}`;
  }

  protected totalMismatched(column: MatrixColumn): boolean {
    const check = this.store.check(column.row.kpiId);
    return !!check && check.columns[this.period()].remainingUnits !== 0n;
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  // ---- suggestion -------------------------------------------------------------------------------

  protected openSuggest(): void {
    if (!this.readOnly()) {
      this.store.suggestFailed.set(false);
      this.showSuggest.set(true);
    }
  }

  protected closeSuggest(): void {
    this.showSuggest.set(false);
  }

  /** UNMATCHED: every ready KPI not yet matched; any other scope: the ready KPI columns shown now. */
  protected onSuggestionCreated(request: SuggestAllocationRequest): void {
    const ready = (request.scope === 'UNMATCHED' ? this.allColumns() : this.columns()).filter(
      (column) => column.ready && (request.scope !== 'UNMATCHED' || column.state !== 'MATCHED'),
    );
    if (ready.length === 0) {
      this.showSuggest.set(false);
      return;
    }
    this.store
      .suggest(
        this.year,
        this.branchYearId,
        ready.map((column) => column.row.kpiId),
        request,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((ok) => {
        if (ok) {
          this.showSuggest.set(false);
        }
      });
  }

  protected changeSuggestionBasis(): void {
    this.store.undoSuggestion();
    this.showSuggest.set(true);
  }

  private allColumns(): MatrixColumn[] {
    return this.store.kpiRows().map((row) => ({
      row,
      state: this.store.kpiState(row.kpiId),
      ready: this.store.allocation(row.kpiId) !== null,
    }));
  }

  // ---- saving -----------------------------------------------------------------------------------

  protected save(): void {
    if (this.readOnly() || this.store.saving()) {
      return;
    }
    this.store
      .save(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (result.saved > 0) {
          this.showToast(
            this.localization.departmentAllocationMatrix.saveSuccess.replace(
              '{count}',
              `${result.saved}`,
            ),
          );
          this.saved.emit();
        }
      });
  }

  protected saveErrorText(): string | null {
    const errors = this.store.saveErrors();
    if (errors.size === 0) {
      return null;
    }
    const names = [...errors.keys()].map((kpiId) => this.store.allocation(kpiId)?.kpiName ?? kpiId);
    return this.localization.departmentAllocationMatrix.saveFailed.replace(
      '{kpis}',
      names.join(', '),
    );
  }

  protected requestBack(): void {
    this.back.emit();
  }

  protected requestPublish(): void {
    if (this.canGoNext()) {
      this.publish.emit();
    }
  }

  protected requestOpenKpi(column: MatrixColumn): void {
    if (column.ready) {
      this.openKpi.emit(column.row.kpiId);
    }
  }

  private formatUnits(units: bigint): string {
    return formatUnits(units, numberLocale(this.i18n.currentLanguage()));
  }

  private showToast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toast.set(message);
    this.toastTimer = setTimeout(() => this.toast.set(null), TOAST_MS);
  }
}
