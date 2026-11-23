import { HttpErrorResponse } from '@angular/common/http';
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
import { ApiResponse } from '../../../core/api-response.model';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import {
  DepartmentAllocationRow,
  KpiAllocation,
  QuarterSplit,
  QuarterSplitRow,
  TargetSetup,
} from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import {
  distribute,
  editText,
  formatUnits,
  formatValue,
  numberLocale,
  parseNumber,
  roundValue,
  stepFor,
  unitsOf,
  valueOfUnits,
} from '../target-math';

type CellKey = 'year' | 'q1' | 'q2' | 'q3' | 'q4';
type QuarterKey = Exclude<CellKey, 'year'>;
type DepartmentValues = Record<CellKey, number>;

const QUARTERS: readonly QuarterKey[] = ['q1', 'q2', 'q3', 'q4'];
const CELLS: readonly CellKey[] = ['year', ...QUARTERS];
const TOAST_MS = 3000;

type KpiCardState = 'NOT_SPLIT' | 'NOT_ALLOCATED' | 'DONE' | 'MISMATCH';

interface KpiCard {
  row: QuarterSplitRow;
  state: KpiCardState;
  departments: number | null;
}

interface DepartmentRowView {
  department: DepartmentAllocationRow;
  values: DepartmentValues;
  sumUnits: bigint;
  diffUnits: bigint;
  matched: boolean;
  visible: boolean;
}

interface ColumnView {
  key: CellKey;
  totalUnits: bigint;
  branchUnits: bigint;
  remainingUnits: bigint;
  /** The column adds up to the branch target exactly. */
  matched: boolean;
}

/** What the last loaded allocation of a KPI looked like, so its card keeps a status after switching KPI. */
interface KpiSummary {
  matched: boolean;
  departments: number;
}

function valuesOf(row: DepartmentAllocationRow): DepartmentValues {
  return {
    year: roundValue(row.year),
    q1: roundValue(row.q1),
    q2: roundValue(row.q2),
    q3: roundValue(row.q3),
    q4: roundValue(row.q4),
  };
}

/**
 * SCR-STRAT-04-D1 step 3 (Figma `2928:3060`): allocates one KPI's branch year and quarter targets to the
 * eligible departments, with client-side totals, previous-year / branch-ratio filling and a draft save.
 */
@Component({
  selector: 'app-department-allocation',
  imports: [LayoutIconComponent],
  templateUrl: './department-allocation.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../branch-quarter-split/branch-quarter-split.component.scss',
    '../branch-quarter-split/branch-quarter-split.component.part2.scss',
    './department-allocation.component.scss',
  ],
})
export class DepartmentAllocationComponent extends LocalizedComponent implements OnChanges {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) branchYearId!: string;
  @Input({ required: true }) year!: number;
  @Input({ required: true }) set setup(value: TargetSetup) {
    this.setupState.set(value);
  }
  @Input() initialKpiId: string | null = null;
  @Output() readonly saved = new EventEmitter<void>();
  @Output() readonly back = new EventEmitter<void>();
  @Output() readonly publish = new EventEmitter<void>();

  protected readonly quarterKeys = QUARTERS;
  protected readonly cells = CELLS;

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly allocationLoading = signal(false);
  protected readonly allocationFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly toast = signal<string | null>(null);
  protected readonly percentMode = signal(false);
  protected readonly search = signal('');
  protected readonly split = signal<QuarterSplit | null>(null);
  protected readonly allocation = signal<KpiAllocation | null>(null);
  protected readonly selectedKpiId = signal<string | null>(null);
  protected readonly setupState = signal<TargetSetup | null>(null);
  private readonly draft = signal<Record<string, DepartmentValues>>({});
  private readonly rawInputs = signal<ReadonlyMap<string, string>>(new Map());
  /** Matched departments show their quarters as text until clicked. */
  private readonly editing = signal<ReadonlySet<string>>(new Set());
  private readonly summaries = signal<ReadonlyMap<string, KpiSummary>>(new Map());
  private toastTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
  }

  protected readonly readOnly = computed(
    () => this.setupState()?.status === 'ACTIVE' || this.split()?.status !== 'DRAFT',
  );
  protected readonly lockedQuarters = computed(
    () =>
      new Set(
        (this.split()?.quarters ?? []).filter((q) => q.locked).map((q) => QUARTERS[q.quarter - 1]),
      ),
  );
  protected readonly ratioKpis = computed(() =>
    (this.split()?.rows ?? []).filter((row) => row.isRatio),
  );

  protected readonly cards = computed<KpiCard[]>(() => {
    const blockers = new Set((this.setupState()?.blockers ?? []).map((blocker) => blocker.kpiId));
    const summaries = this.summaries();
    const selected = this.selectedKpiId();
    const current = this.allocationSummary();
    return (this.split()?.rows ?? [])
      .filter((row) => !row.isRatio)
      .map((row) => {
        if (!row.matched) {
          return { row, state: 'NOT_SPLIT', departments: null };
        }
        const summary = row.kpiId === selected && current ? current : summaries.get(row.kpiId);
        if (summary) {
          return {
            row,
            state: summary.matched ? 'DONE' : 'MISMATCH',
            departments: summary.departments,
          };
        }
        // Not loaded in this visit: the setup blockers say whether the KPI is fully allocated.
        return {
          row,
          state: blockers.has(row.kpiId) ? 'NOT_ALLOCATED' : 'DONE',
          departments: null,
        };
      });
  });

  protected readonly rows = computed<DepartmentRowView[]>(() => {
    const allocation = this.allocation();
    if (!allocation) {
      return [];
    }
    const draft = this.draft();
    const term = this.search().trim().toLocaleLowerCase();
    return allocation.departments.map((department) => {
      const values = draft[department.departmentId] ?? valuesOf(department);
      const sumUnits = QUARTERS.reduce((sum, key) => sum + unitsOf(values[key]), 0n);
      const diffUnits = unitsOf(values.year) - sumUnits;
      return {
        department,
        values,
        sumUnits,
        diffUnits,
        matched: diffUnits === 0n,
        visible: !term || department.departmentName.toLocaleLowerCase().includes(term),
      };
    });
  });

  protected readonly columns = computed<ColumnView[]>(() => {
    const allocation = this.allocation();
    if (!allocation) {
      return [];
    }
    const rows = this.rows();
    return CELLS.map((key) => {
      const totalUnits = rows.reduce((sum, view) => sum + unitsOf(view.values[key]), 0n);
      const branchUnits = unitsOf(allocation.branch[key]);
      const remainingUnits = branchUnits - totalUnits;
      return { key, totalUnits, branchUnits, remainingUnits, matched: remainingUnits === 0n };
    });
  });

  protected readonly allMatched = computed(
    () =>
      this.rows().length > 0 &&
      this.rows().every((view) => view.matched) &&
      this.columns().every((column) => column.matched),
  );

  private readonly allocationSummary = computed<KpiSummary | null>(() =>
    this.allocation() && this.isSaved()
      ? { matched: this.allMatched(), departments: this.rows().length }
      : null,
  );

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['branchYearId']) {
      this.load();
    }
  }

  protected load(): void {
    if (!this.branchYearId) {
      return;
    }
    this.loading.set(true);
    this.failed.set(false);
    this.targetApi
      .getQuarterSplit(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (split) => {
          this.split.set(split);
          this.loading.set(false);
          const selectable = split.rows.filter((row) => !row.isRatio && row.matched);
          const initial =
            selectable.find((row) => row.kpiId === this.initialKpiId) ?? selectable[0] ?? null;
          if (initial) {
            this.selectKpi(initial.kpiId);
          }
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectKpi(kpiId: string): void {
    const card = this.cards().find((item) => item.row.kpiId === kpiId);
    if (!card || card.state === 'NOT_SPLIT') {
      return;
    }
    this.rememberSummary();
    this.selectedKpiId.set(kpiId);
    this.allocation.set(null);
    this.saveError.set(null);
    this.search.set('');
    this.loadAllocation(kpiId);
  }

  protected loadAllocation(kpiId: string): void {
    this.allocationLoading.set(true);
    this.allocationFailed.set(false);
    this.targetApi
      .getKpiAllocation(this.branchYearId, kpiId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (allocation) => {
          if (this.selectedKpiId() === kpiId) {
            this.applyState(allocation);
          }
          this.allocationLoading.set(false);
        },
        error: () => {
          this.allocationFailed.set(true);
          this.allocationLoading.set(false);
        },
      });
  }

  // ---- text -------------------------------------------------------------------------------------

  protected cardStatus(card: KpiCard): string {
    const text = this.localization.departmentAllocation;
    switch (card.state) {
      case 'NOT_SPLIT':
        return text.cardNotSplit;
      case 'NOT_ALLOCATED':
        return text.cardNotAllocated;
      case 'DONE':
        return card.departments === null
          ? text.cardDone
          : text.cardDoneCount.replace('{count}', `${card.departments}`);
      case 'MISMATCH':
        return text.cardMismatch.replace('{count}', `${card.departments ?? 0}`);
    }
  }

  protected ratioNote(row: QuarterSplitRow): string {
    return this.localization.departmentAllocation.ratioNote.replace('{name}', row.kpiName);
  }

  protected unitText(): string {
    return this.localization.departmentAllocation.unit.replace(
      '{unit}',
      this.allocation()?.unit ?? '',
    );
  }

  protected previousYearLabel(): string {
    return this.localization.departmentAllocation.fillPreviousYear.replace(
      '{year}',
      `${this.year - 1}`,
    );
  }

  protected columnLabel(key: CellKey): string {
    const text = this.localization.departmentAllocation;
    return key === 'year'
      ? text.columnYear
      : text.columnQuarter.replace('{quarter}', `${QUARTERS.indexOf(key) + 1}`);
  }

  protected cellAria(view: DepartmentRowView, key: CellKey): string {
    return this.localization.departmentAllocation.cellAria
      .replace('{column}', this.columnLabel(key))
      .replace('{name}', view.department.departmentName);
  }

  protected checkText(view: DepartmentRowView): string {
    return view.matched
      ? '✓'
      : this.localization.departmentAllocation.checkMismatch.replace(
          '{diff}',
          this.formatUnits(view.diffUnits < 0n ? -view.diffUnits : view.diffUnits),
        );
  }

  protected remainingText(column: ColumnView): string {
    return column.matched ? '0 ✓' : this.formatUnits(column.remainingUnits);
  }

  protected hiddenNote(): string | null {
    const allocation = this.allocation();
    if (!allocation || allocation.hiddenDepartments.length === 0) {
      return null;
    }
    return this.localization.departmentAllocation.hiddenNote
      .replace(
        '{departments}',
        allocation.hiddenDepartments.map((d) => d.departmentName).join(', '),
      )
      .replace('{workType}', allocation.workType || this.localization.departmentAllocation.staff);
  }

  /** `Chưa khớp: …` parts for the remaining quarter/year amounts and every department off by itself. */
  protected mismatchText(): string | null {
    const allocation = this.allocation();
    if (!allocation || this.allMatched()) {
      return null;
    }
    const text = this.localization.departmentAllocation;
    const parts: string[] = [];
    for (const column of this.columns()) {
      if (column.remainingUnits === 0n) {
        continue;
      }
      const short = column.remainingUnits > 0n;
      const amount = this.formatUnits(short ? column.remainingUnits : -column.remainingUnits);
      const template =
        column.key === 'year'
          ? short
            ? text.mismatchYearShort
            : text.mismatchYearExcess
          : short
            ? text.mismatchQuarterShort
            : text.mismatchQuarterExcess;
      parts.push(
        template
          .replace('{quarter}', `${QUARTERS.indexOf(column.key as QuarterKey) + 1}`)
          .replace('{amount}', amount)
          .replace('{unit}', allocation.unit),
      );
    }
    for (const view of this.rows().filter((row) => !row.matched)) {
      parts.push(text.mismatchDepartment.replace('{name}', view.department.departmentName));
    }
    return text.mismatchPrefix + parts.join('; ') + text.mismatchSuffix;
  }

  protected formatUnits(units: bigint): string {
    return formatUnits(units, this.locale());
  }

  protected formatValue(value: number): string {
    return formatValue(value, this.locale());
  }

  // ---- cells ------------------------------------------------------------------------------------

  /** Quarter cells of a matched department render as text until the user clicks them. */
  protected showInput(view: DepartmentRowView, key: CellKey): boolean {
    if (this.readOnly()) {
      return false;
    }
    return key === 'year' || !view.matched || this.editing().has(view.department.departmentId);
  }

  protected isLocked(key: CellKey): boolean {
    return key !== 'year' && this.lockedQuarters().has(key);
  }

  protected startEditing(view: DepartmentRowView): void {
    if (this.readOnly()) {
      return;
    }
    const editing = new Set(this.editing());
    editing.add(view.department.departmentId);
    this.editing.set(editing);
  }

  protected inputText(view: DepartmentRowView, key: CellKey): string {
    const raw = this.rawInputs().get(this.cellId(view, key));
    if (raw !== undefined) {
      return raw;
    }
    if (key === 'year' && this.percentMode()) {
      return this.formatPercent(view.values.year);
    }
    return this.formatValue(view.values[key]);
  }

  protected onFocus(view: DepartmentRowView, key: CellKey): void {
    const raw = new Map(this.rawInputs());
    const value =
      key === 'year' && this.percentMode() ? this.percentOf(view.values.year) : view.values[key];
    raw.set(this.cellId(view, key), editText(value, this.locale()));
    this.rawInputs.set(raw);
  }

  protected onInput(view: DepartmentRowView, key: CellKey, event: Event): void {
    const input = event.target as HTMLInputElement;
    const raw = new Map(this.rawInputs());
    raw.set(this.cellId(view, key), input.value);
    this.rawInputs.set(raw);

    let value = parseNumber(input.value, this.locale());
    if (key === 'year' && this.percentMode()) {
      value = this.valueOfPercent(value);
    }
    this.setValue(view.department, key, value);
  }

  protected onBlur(view: DepartmentRowView, key: CellKey): void {
    const raw = new Map(this.rawInputs());
    raw.delete(this.cellId(view, key));
    this.rawInputs.set(raw);
  }

  protected setPercentMode(percent: boolean): void {
    this.percentMode.set(percent);
    this.rawInputs.set(new Map());
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  // ---- filling ----------------------------------------------------------------------------------

  protected hasPreviousYear(): boolean {
    return (this.allocation()?.departments ?? []).some((d) => d.previousYear !== null);
  }

  /** Branch year spread over the departments by last year's department targets. */
  protected fillYearByPreviousYear(): void {
    const allocation = this.allocation();
    if (!allocation || this.readOnly()) {
      return;
    }
    const departments = allocation.departments;
    const shares = distribute(
      unitsOf(allocation.branch.year),
      departments.map((d) => unitsOf(d.previousYear ?? 0)),
      stepFor(allocation.branch.year),
    );
    const draft = { ...this.draft() };
    departments.forEach((department, index) => {
      draft[department.departmentId] = {
        ...(draft[department.departmentId] ?? valuesOf(department)),
        year: valueOfUnits(shares[index]),
      };
    });
    this.draft.set(draft);
    this.rawInputs.set(new Map());
  }

  /**
   * Every department's quarters in the branch's quarter proportions. Locked quarters keep their value. Each
   * department but the last gets an exact row (its quarters add up to its year); the last one takes what is
   * left of every quarter column, so the column totals match the branch exactly.
   */
  protected fillQuartersByBranch(): void {
    const allocation = this.allocation();
    if (!allocation || this.readOnly()) {
      return;
    }
    const locked = this.lockedQuarters();
    const open = QUARTERS.filter((key) => !locked.has(key));
    if (open.length === 0) {
      return;
    }
    const departments = allocation.departments;
    const draft = { ...this.draft() };
    const step = stepFor(allocation.branch.year);
    const weights = open.map((key) => unitsOf(allocation.branch[key]));
    const assigned: Record<QuarterKey, bigint> = { q1: 0n, q2: 0n, q3: 0n, q4: 0n };

    departments.forEach((department, index) => {
      const values = { ...(draft[department.departmentId] ?? valuesOf(department)) };
      if (index < departments.length - 1) {
        const lockedUnits = QUARTERS.filter((key) => locked.has(key)).reduce(
          (sum, key) => sum + unitsOf(values[key]),
          0n,
        );
        const shares = distribute(unitsOf(values.year) - lockedUnits, weights, step);
        open.forEach((key, i) => {
          values[key] = valueOfUnits(shares[i]);
        });
      } else {
        open.forEach((key) => {
          values[key] = valueOfUnits(unitsOf(allocation.branch[key]) - assigned[key]);
        });
      }
      open.forEach((key) => {
        assigned[key] += unitsOf(values[key]);
      });
      draft[department.departmentId] = values;
    });

    this.draft.set(draft);
    this.rawInputs.set(new Map());
  }

  // ---- saving -----------------------------------------------------------------------------------

  protected save(next = false): void {
    const allocation = this.allocation();
    const kpiId = this.selectedKpiId();
    if (!allocation || !kpiId || this.readOnly() || this.saving()) {
      return;
    }
    const draft = this.draft();
    this.saving.set(true);
    this.saveError.set(null);
    this.targetApi
      .saveKpiAllocation(this.branchYearId, kpiId, {
        departments: allocation.departments.map((department) => ({
          departmentId: department.departmentId,
          ...(draft[department.departmentId] ?? valuesOf(department)),
        })),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.applyState(saved);
          this.saving.set(false);
          this.showToast(this.localization.departmentAllocation.saveSuccess);
          this.saved.emit();
          if (next) {
            this.selectNextKpi(kpiId);
          }
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          this.saveError.set(this.saveErrorText(error));
        },
      });
  }

  protected requestBack(): void {
    this.back.emit();
  }

  protected requestPublish(): void {
    this.publish.emit();
  }

  // ---- helpers ----------------------------------------------------------------------------------

  /** True when the draft equals the last loaded/saved allocation (the card status reflects saved data). */
  private isSaved(): boolean {
    const allocation = this.allocation();
    if (!allocation) {
      return false;
    }
    const draft = this.draft();
    return allocation.departments.every((department) => {
      const saved = valuesOf(department);
      const current = draft[department.departmentId] ?? saved;
      return CELLS.every((key) => unitsOf(current[key]) === unitsOf(saved[key]));
    });
  }

  private rememberSummary(): void {
    const kpiId = this.selectedKpiId();
    const summary = this.allocationSummary();
    if (kpiId && summary) {
      const summaries = new Map(this.summaries());
      summaries.set(kpiId, summary);
      this.summaries.set(summaries);
    }
  }

  private selectNextKpi(currentKpiId: string): void {
    const cards = this.cards();
    const index = cards.findIndex((card) => card.row.kpiId === currentKpiId);
    const next = cards.slice(index + 1).find((card) => card.state !== 'NOT_SPLIT');
    if (next) {
      this.selectKpi(next.row.kpiId);
    }
  }

  private setValue(department: DepartmentAllocationRow, key: CellKey, value: number): void {
    const values = { ...(this.draft()[department.departmentId] ?? valuesOf(department)) };
    values[key] = value;
    this.draft.set({ ...this.draft(), [department.departmentId]: values });
  }

  private applyState(allocation: KpiAllocation): void {
    const draft: Record<string, DepartmentValues> = {};
    for (const department of allocation.departments) {
      draft[department.departmentId] = valuesOf(department);
    }
    this.draft.set(draft);
    this.rawInputs.set(new Map());
    this.editing.set(new Set());
    this.allocation.set(allocation);
  }

  private percentOf(value: number): number {
    const branchYear = this.allocation()?.branch.year ?? 0;
    return branchYear === 0 ? 0 : Math.round((value / branchYear) * 10000) / 100;
  }

  private valueOfPercent(percent: number): number {
    const branchYear = this.allocation()?.branch.year ?? 0;
    const raw = (branchYear * percent) / 100;
    return Number.isInteger(branchYear) ? Math.round(raw) : roundValue(raw);
  }

  private formatPercent(value: number): string {
    return `${new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 2 }).format(
      this.percentOf(value),
    )}%`;
  }

  private cellId(view: DepartmentRowView, key: CellKey): string {
    return `${view.department.departmentId}:${key}`;
  }

  private saveErrorText(error: HttpErrorResponse): string {
    const code = (error.error as ApiResponse<unknown> | undefined)?.errorCode;
    const text = this.localization.departmentAllocation;
    switch (code) {
      case 'TARGET_QUARTERS_NOT_READY':
        return text.saveFailedQuartersNotReady;
      case 'TARGET_DEPARTMENT_NOT_ELIGIBLE':
        return text.saveFailedNotEligible;
      case 'PERIOD_LOCKED':
        return text.saveFailedPeriodLocked;
      case 'TARGET_INVALID_STATE':
        return text.saveFailedInvalidState;
      default:
        return text.saveFailed;
    }
  }

  private showToast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toast.set(message);
    this.toastTimer = setTimeout(() => {
      if (this.toast() === message) {
        this.toast.set(null);
      }
    }, TOAST_MS);
  }

  private locale(): string {
    return numberLocale(this.i18n.currentLanguage());
  }
}
