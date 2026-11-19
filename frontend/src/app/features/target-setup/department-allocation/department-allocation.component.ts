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
import {
  DepartmentAllocationRow,
  QuarterSplitRow,
  TargetSetup,
} from '../../../core/target-assignment.models';
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
import {
  CELL_KEYS,
  CellKey,
  Tier2AllocationStore,
  Tier2ColumnCheck,
  Tier2KpiState,
} from '../tier2-allocation.store';
import { SuggestButtonComponent } from '../../../shared/target/suggest-button/suggest-button.component';

const TOAST_MS = 3000;

interface KpiCard {
  row: QuarterSplitRow;
  state: Tier2KpiState;
}

interface KpiCardGroup {
  groupCode: string;
  groupName: string;
  cards: KpiCard[];
  mismatched: number;
  notSplit: number;
}

/** Lowercases and strips diacritics so the KPI search box matches "tien" against "Tiền". */
function normalizeSearchText(value: string): string {
  return value.trim().toLocaleLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
}

/**
 * W5 (design/v3/w05-t2-by-kpi.png): tier 2, branch -> departments, one KPI at a time. The left list shows every
 * KPI with its Khớp / Lệch / Chưa state; the table edits the departments' year and quarters. Suggestions go only
 * through "✦ Gợi ý phân bổ" (`POST /target-suggestions/{year}`, basis last year's target or actual): no equal or
 * headcount split. "Tiếp: Phát hành" stays disabled while any KPI is mismatched or not split.
 */
@Component({
  selector: 'app-department-allocation',
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
  templateUrl: './department-allocation.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/target/target-page.scss',
    '../branch-quarter-split/branch-quarter-split.component.scss',
    '../branch-quarter-split/branch-quarter-split.component.part2.scss',
    './department-allocation.component.scss',
  ],
})
export class DepartmentAllocationComponent extends LocalizedComponent implements OnChanges {
  protected readonly store = inject(Tier2AllocationStore);
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

  protected readonly cellKeys = CELL_KEYS;
  protected readonly setupState = signal<TargetSetup | null>(null);
  protected readonly selectedKpiId = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly filter = signal<TierStatusFilter>('ALL');
  protected readonly showSuggest = signal(false);
  protected readonly toast = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
  }

  /** True while the draft has unsaved edits: the wizard confirms before switching to the matrix view. */
  isDirty(): boolean {
    return this.store.dirtyKpiIds().length > 0;
  }

  protected readonly readOnly = computed(
    () => this.setupState()?.status === 'ACTIVE' || this.store.readOnly(),
  );

  protected readonly cards = computed<KpiCard[]>(() =>
    this.store.kpiRows().map((row) => ({ row, state: this.store.kpiState(row.kpiId) })),
  );

  protected readonly groups = computed<KpiCardGroup[]>(() => {
    const term = normalizeSearchText(this.search());
    const filter = this.filter();
    const groups: KpiCardGroup[] = [];
    const byCode = new Map<string, KpiCardGroup>();
    for (const card of this.cards()) {
      let group = byCode.get(card.row.groupCode);
      if (!group) {
        group = {
          groupCode: card.row.groupCode,
          groupName: card.row.groupName,
          cards: [],
          mismatched: 0,
          notSplit: 0,
        };
        byCode.set(card.row.groupCode, group);
        groups.push(group);
      }
      group.mismatched += card.state === 'MISMATCHED' ? 1 : 0;
      group.notSplit += card.state === 'NOT_SPLIT' ? 1 : 0;
      const matchesSearch =
        !term ||
        normalizeSearchText(card.row.kpiName).includes(term) ||
        normalizeSearchText(card.row.kpiCode).includes(term);
      const matchesFilter =
        filter === 'ALL' ||
        (filter === 'MISMATCHED' && card.state === 'MISMATCHED') ||
        (filter === 'NOT_SPLIT' && card.state === 'NOT_SPLIT');
      if (matchesSearch && matchesFilter) {
        group.cards.push(card);
      }
    }
    return groups;
  });

  protected readonly allocation = computed(() => {
    const kpiId = this.selectedKpiId();
    return kpiId ? this.store.allocation(kpiId) : null;
  });

  protected readonly check = computed(() => {
    const kpiId = this.selectedKpiId();
    return kpiId ? this.store.check(kpiId) : null;
  });

  /** Σ last year's actual of the shown departments, the base of the "Thực tế {year-1}" share column. */
  private readonly previousActualTotal = computed(() =>
    (this.allocation()?.departments ?? []).reduce(
      (sum, department) => sum + (department.previousYearActual ?? 0),
      0,
    ),
  );

  private readonly anyHistory = computed(() =>
    (this.allocation()?.departments ?? []).some(
      (d) => d.previousYear !== null || d.previousYearActual !== null,
    ),
  );

  protected readonly bufferUnitOptions = computed<SuggestionBufferUnitOption[]>(() =>
    (this.allocation()?.departments ?? []).map((d) => ({
      id: d.departmentId,
      label: d.departmentName,
    })),
  );

  /** "Tiếp: Phát hành": every KPI matched and saved, and the server agrees (`canPublish`). */
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
    if (!this.branchYearId) {
      return;
    }
    this.store
      .load(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const selectable = this.cards().filter((card) => this.store.allocation(card.row.kpiId));
        const initial =
          selectable.find((card) => card.row.kpiId === this.initialKpiId) ?? selectable[0] ?? null;
        this.selectedKpiId.set(initial?.row.kpiId ?? null);
      });
  }

  protected selectKpi(kpiId: string): void {
    if (this.store.allocation(kpiId)) {
      this.selectedKpiId.set(kpiId);
    }
  }

  // ---- text -------------------------------------------------------------------------------------

  protected tierLabel(): string {
    return this.localization.departmentAllocation.tierLabel;
  }

  protected stateLabel(state: Tier2KpiState): string {
    const text = this.localization.departmentAllocation;
    return state === 'MATCHED'
      ? text.stateMatched
      : state === 'MISMATCHED'
        ? text.stateMismatched
        : text.stateNotSplit;
  }

  protected groupMeta(group: KpiCardGroup): string {
    const text = this.localization.departmentAllocation;
    const parts = [`${group.cards.length}`];
    if (group.mismatched > 0) {
      parts.push(text.groupMismatched.replace('{count}', `${group.mismatched}`));
    }
    if (group.notSplit > 0) {
      parts.push(text.groupNotSplit.replace('{count}', `${group.notSplit}`));
    }
    return parts.join(' · ');
  }

  protected ratioNote(): string | null {
    const count = this.store.ratioRows().length;
    return count > 0
      ? this.localization.departmentAllocation.ratioNote.replace('{count}', `${count}`)
      : null;
  }

  protected columnLabel(key: CellKey): string {
    const text = this.localization.departmentAllocation;
    return key === 'year'
      ? text.columnYear.replace('{year}', `${this.year}`)
      : text.columnQuarter.replace('{quarter}', `${QUARTERS.indexOf(key) + 1}`);
  }

  protected actualHeader(): string {
    return this.localization.departmentAllocation.columnActual.replace(
      '{year}',
      `${this.year - 1}`,
    );
  }

  /** The department's share of last year's actual, e.g. `20,0%`; `—` without history. */
  protected actualShare(department: DepartmentAllocationRow): string {
    const total = this.previousActualTotal();
    if (department.previousYearActual === null || total <= 0) {
      return '—';
    }
    return `${new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    }).format((department.previousYearActual / total) * 100)}%`;
  }

  /** "Mới {year}" for a department without any history while others have some (D6 fallback average). */
  protected isNew(department: DepartmentAllocationRow): boolean {
    return (
      this.anyHistory() &&
      department.previousYear === null &&
      department.previousYearActual === null
    );
  }

  protected newTag(): string {
    return this.localization.departmentAllocation.tagNew.replace('{year}', `${this.year}`);
  }

  protected hiddenReason(): string {
    const text = this.localization.departmentAllocation;
    return text.hiddenReason.replace('{workType}', this.allocation()?.workType || text.staff);
  }

  protected cellState(departmentId: string, key: CellKey): AllocationCellState {
    return this.store.cellState(this.selectedKpiId()!, departmentId, key);
  }

  protected cellValue(departmentId: string, key: CellKey): number | null {
    return this.store.values(this.selectedKpiId()!, departmentId)?.[key] ?? null;
  }

  protected onCellChange(departmentId: string, key: CellKey, value: number): void {
    this.store.setCell(this.selectedKpiId()!, departmentId, key, value);
  }

  protected lockedReason(): string {
    return this.localization.departmentAllocation.lockedQuarterReason;
  }

  protected columnText(key: CellKey, field: keyof Tier2ColumnCheck): string {
    return this.formatUnits(this.check()?.columns[key][field] ?? 0n);
  }

  protected columnMatched(key: CellKey): boolean {
    return this.check()?.columns[key].remainingUnits === 0n;
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  private formatUnits(units: bigint): string {
    return formatUnits(units, this.locale());
  }

  /** `Σ phòng ≠ chi nhánh` mismatch summary for the open KPI (MSG14 wording), or null when it adds up. */
  protected mismatchText(): string | null {
    const check = this.check();
    if (!check || check.state !== 'MISMATCHED') {
      return null;
    }
    const text = this.localization.departmentAllocation;
    const parts: string[] = [];
    for (const key of CELL_KEYS) {
      const remaining = check.columns[key].remainingUnits;
      if (remaining !== 0n) {
        parts.push(
          (remaining > 0n ? text.mismatchShort : text.mismatchExcess)
            .replace('{column}', this.columnLabel(key))
            .replace('{amount}', this.formatUnits(remaining > 0n ? remaining : -remaining)),
        );
      }
    }
    for (const department of this.allocation()?.departments ?? []) {
      if (check.mismatchedDepartments.has(department.departmentId)) {
        parts.push(text.mismatchDepartment.replace('{name}', department.departmentName));
      }
    }
    return `${text.mismatchPrefix} ${parts.join('; ')}.`;
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

  protected onSuggestionCreated(request: SuggestAllocationRequest): void {
    const selected = this.selectedKpiId();
    if (!selected) {
      return;
    }
    this.store
      .suggest(this.year, this.branchYearId, this.scopeKpiIds(request, selected), request)
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

  /** CURRENT_KPI / SELECTED: the open KPI; GROUP: its KPI group; UNMATCHED: every mismatched or empty KPI. */
  private scopeKpiIds(request: SuggestAllocationRequest, selected: string): string[] {
    const ready = this.cards().filter((card) => this.store.allocation(card.row.kpiId));
    if (request.scope === 'GROUP') {
      const groupCode = ready.find((card) => card.row.kpiId === selected)?.row.groupCode;
      return ready.filter((card) => card.row.groupCode === groupCode).map((card) => card.row.kpiId);
    }
    if (request.scope === 'UNMATCHED') {
      return ready.filter((card) => card.state !== 'MATCHED').map((card) => card.row.kpiId);
    }
    return [selected];
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
          this.showToast(this.localization.departmentAllocation.saveSuccess);
          this.saved.emit();
        }
      });
  }

  protected saveErrorText(): string | null {
    const errors = this.store.saveErrors();
    if (errors.size === 0) {
      return null;
    }
    const text = this.localization.departmentAllocation;
    const names = [...errors.keys()].map((kpiId) => this.store.allocation(kpiId)?.kpiName ?? kpiId);
    const code = [...errors.values()][0];
    const reason =
      code === 'TARGET_QUARTERS_NOT_READY'
        ? text.saveFailedQuartersNotReady
        : code === 'TARGET_DEPARTMENT_NOT_ELIGIBLE'
          ? text.saveFailedNotEligible
          : code === 'PERIOD_LOCKED'
            ? text.saveFailedPeriodLocked
            : code === 'TARGET_INVALID_STATE'
              ? text.saveFailedInvalidState
              : text.saveFailed;
    return `${names.join(', ')}: ${reason}`;
  }

  protected requestBack(): void {
    this.back.emit();
  }

  protected requestPublish(): void {
    if (this.canGoNext()) {
      this.publish.emit();
    }
  }

  private showToast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toast.set(message);
    this.toastTimer = setTimeout(() => this.toast.set(null), TOAST_MS);
  }

  private locale(): string {
    return numberLocale(this.i18n.currentLanguage());
  }
}
