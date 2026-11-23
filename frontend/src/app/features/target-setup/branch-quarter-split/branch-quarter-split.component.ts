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
import { QuarterSplit, QuarterSplitRow } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import { AllocationCellComponent } from '../../../shared/target/allocation-cell/allocation-cell.component';
import { CellLegendComponent } from '../../../shared/target/cell-legend/cell-legend.component';
import { SuggestAllocationPopoverComponent } from '../../../shared/target/suggest-allocation-popover/suggest-allocation-popover.component';
import { SuggestionPreviewBannerComponent } from '../../../shared/target/suggestion-preview-banner/suggestion-preview-banner.component';
import {
  AllocationCellState,
  DEFAULT_COUNT_STEP,
  DEFAULT_MONEY_STEP,
  SuggestAllocationRequest,
  SuggestionBasis,
  SuggestionBufferUnitOption,
  SuggestionRemainderMethod,
} from '../../../shared/target/target-shared.models';
import { columnFormat, ColumnFormat } from '../../target-overview/target-overview.utils';
import {
  formatUnits,
  formatValue,
  numberLocale,
  roundValue,
  unitsOf,
  valueOfUnits,
} from '../target-math';
import {
  SuggestionCellDto,
  SuggestTargetsRequestDto,
  SuggestTargetsResponseDto,
  TargetSuggestionService,
} from '../../../core/target-suggestion.service';
import { SuggestButtonComponent } from '../../../shared/target/suggest-button/suggest-button.component';

type QuarterKey = 'q1' | 'q2' | 'q3' | 'q4';

interface QuarterValues {
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

interface QuarterSplitRowView {
  row: QuarterSplitRow;
  values: QuarterValues;
  /** Sum of the four quarter cells; matched against `yearValue` in units of 1e-4. */
  sumUnits: bigint;
  diffUnits: bigint;
  matched: boolean;
  dirty: boolean;
  canAllocate: boolean;
  locked: boolean[];
  edited: boolean[];
}

/** Snapshot kept while a ✦ suggestion from `POST /target-suggestions/{year}` is shown but not yet applied/undone. */
interface PendingSuggestion {
  response: SuggestTargetsResponseDto;
  previousDraft: Record<string, QuarterValues>;
  touchedKeys: Set<string>;
  basis: SuggestionBasis;
  remainderMethod: SuggestionRemainderMethod;
  cellCount: number;
  kpiCount: number;
  /** KPIs the server suggested without last year's data (progressive Q1→Q4). */
  fallbackCount: number;
  warnings: readonly string[];
}

/** "✦ Gợi ý chia quý" from the overview (`?suggest=1`): last year's targets, every KPI, overwriting open quarters. */
const AUTO_SUGGEST_REQUEST: SuggestAllocationRequest = {
  basis: 'TARGET_LAST_YEAR',
  scope: 'GROUP',
  employeeScope: null,
  periodScope: null,
  applyMode: 'OVERWRITE_UNLOCKED',
  rounding: {
    moneyStep: DEFAULT_MONEY_STEP,
    countStep: DEFAULT_COUNT_STEP,
    mode: 'NEAREST',
    remainderMethod: 'LARGEST_REMAINDER',
    bufferUnitId: null,
  },
};

const QUARTERS: readonly QuarterKey[] = ['q1', 'q2', 'q3', 'q4'];
const QUARTER_NUMBERS: readonly number[] = [1, 2, 3, 4];
const TOAST_MS = 3000;

function valuesOf(row: QuarterSplitRow): QuarterValues {
  return {
    q1: roundValue(row.q1 ?? 0),
    q2: roundValue(row.q2 ?? 0),
    q3: roundValue(row.q3 ?? 0),
    q4: roundValue(row.q4 ?? 0),
  };
}

function cloneValues(values: Record<string, QuarterValues>): Record<string, QuarterValues> {
  const copy: Record<string, QuarterValues> = {};
  for (const [kpiId, row] of Object.entries(values)) {
    copy[kpiId] = { ...row };
  }
  return copy;
}

/**
 * SCR-STRAT-04 step 2 (Figma `2928:2736`): splits every yearly KPI target of the branch into the
 * four quarters, with client-side totals, ✦ suggestions from the server and a draft save.
 */
@Component({
  selector: 'app-branch-quarter-split',
  imports: [
    SuggestButtonComponent,
    LayoutIconComponent,
    AllocationCellComponent,
    CellLegendComponent,
    SuggestAllocationPopoverComponent,
    SuggestionPreviewBannerComponent,
  ],
  templateUrl: './branch-quarter-split.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/target/target-page.scss',
    '../../../shared/ui/workspace-page.part2.scss',
    './branch-quarter-split.component.scss',
    './branch-quarter-split.component.part2.scss',
  ],
})
export class BranchQuarterSplitComponent extends LocalizedComponent implements OnChanges {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly suggestionApi = inject(TargetSuggestionService);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) branchYearId!: string;
  @Input({ required: true }) year!: number;
  /** Run "✦ Gợi ý" once, right after the data loads (unsaved), when true and the year is still a draft. */
  @Input() suggest = false;
  @Output() readonly saved = new EventEmitter<void>();
  @Output() readonly allocateKpi = new EventEmitter<string>();
  @Output() readonly reimport = new EventEmitter<void>();

  protected readonly quarters = QUARTER_NUMBERS;
  protected readonly quarterKeys = QUARTERS;

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly toast = signal<string | null>(null);
  protected readonly data = signal<QuarterSplit | null>(null);
  /** POST /target-suggestions/{year} (tier 1) popover + preview banner state (D1 default basis Target). */
  protected readonly showSuggestPopover = signal(false);
  protected readonly suggestLoading = signal(false);
  protected readonly suggestError = signal<string | null>(null);
  protected readonly pendingSuggestion = signal<PendingSuggestion | null>(null);
  /** Quarter cells currently showing the applied-but-not-yet-edited suggestion (AllocationCell SUGGESTED state). */
  private readonly suggestedCells = signal<ReadonlySet<string>>(new Set());
  private readonly draft = signal<Record<string, QuarterValues>>({});
  private readonly savedDraft = signal<Record<string, QuarterValues>>({});
  private readonly editedInputs = signal<ReadonlySet<string>>(new Set());
  private toastTimer?: ReturnType<typeof setTimeout>;
  private autoSuggested = false;

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
  }

  protected readonly readOnly = computed(() => this.data()?.status !== 'DRAFT');
  protected readonly lockedQuarters = computed(() => {
    const split = this.data();
    return new Set(
      (split?.quarters ?? []).filter((quarter) => quarter.locked).map((quarter) => quarter.quarter),
    );
  });

  protected readonly rows = computed<QuarterSplitRowView[]>(() => {
    const split = this.data();
    if (!split) {
      return [];
    }

    const draft = this.draft();
    const saved = this.savedDraft();
    const edited = this.editedInputs();
    const locked = this.lockedQuarters();
    const readOnly = this.readOnly();

    return split.rows.map((row) => {
      const values = draft[row.kpiId] ?? valuesOf(row);
      const previous = saved[row.kpiId] ?? valuesOf(row);
      const sumUnits = QUARTERS.reduce((sum, key) => sum + unitsOf(values[key]), 0n);
      const diffUnits = unitsOf(row.yearValue) - sumUnits;
      const matched = diffUnits === 0n;
      const dirty = QUARTERS.some((key) => unitsOf(values[key]) !== unitsOf(previous[key]));
      return {
        row,
        values,
        sumUnits,
        diffUnits,
        matched,
        dirty,
        canAllocate: matched && !dirty && !readOnly,
        locked: QUARTER_NUMBERS.map((quarter) => locked.has(quarter)),
        edited: QUARTERS.map((key) => edited.has(`${row.kpiId}:${key}`) && !matched),
      };
    });
  });

  /** Buffer-unit choices for the popover's "remainder = buffer" mode: the branch-year's 4 quarter periods. */
  protected readonly bufferUnitOptions = computed<SuggestionBufferUnitOption[]>(() =>
    (this.data()?.quarters ?? []).map((quarter) => ({
      id: quarter.periodId,
      label: this.quarterHeader(quarter.quarter),
    })),
  );

  protected cellState(
    view: QuarterSplitRowView,
    key: QuarterKey,
    index: number,
  ): AllocationCellState {
    if (view.locked[index]) {
      return 'LOCKED';
    }
    const cellKey = `${view.row.kpiId}:${key}`;
    if (view.edited[index]) {
      return 'MISMATCH';
    }
    if (this.suggestedCells().has(cellKey)) {
      return 'SUGGESTED';
    }
    if (this.editedInputs().has(cellKey)) {
      return 'MANUAL';
    }
    return 'DEFAULT';
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['branchYearId']) {
      this.autoSuggested = false;
      this.load();
    }
  }

  protected load(): void {
    if (!this.branchYearId) {
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.saveError.set(null);
    this.targetApi
      .getQuarterSplit(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (split) => {
          this.applyState(split);
          this.loading.set(false);
          if (this.suggest && !this.autoSuggested && split.status === 'DRAFT') {
            this.autoSuggested = true;
            this.onSuggestionCreated(AUTO_SUGGEST_REQUEST);
          }
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected isDraft(): boolean {
    return this.data()?.status === 'DRAFT';
  }

  protected statusLabel(): string {
    const text = this.localization.branchQuarterSplit;
    return this.isDraft() ? text.statusDraft : text.statusActive;
  }

  protected importedText(): string | null {
    const importedAt = this.data()?.importedAt;
    if (!importedAt) {
      return null;
    }
    return this.localization.branchQuarterSplit.importedAt
      .replace('{time}', this.i18n.formatDate(importedAt, 'HH:mm:ss'))
      .replace('{date}', this.i18n.formatDate(importedAt, 'DD/MM/YYYY'));
  }

  /** VND columns/cells show tỷ/triệu by the row's own size (same rule as the overview tables); other units
   * are shown as-is. */
  protected moneyFormat(view: QuarterSplitRowView): ColumnFormat {
    return columnFormat(
      view.row.unit,
      [view.row.yearValue, view.values.q1, view.values.q2, view.values.q3, view.values.q4],
      {
        billionVnd: this.localization.targetOverview.unitBillionVnd,
        millionVnd: this.localization.targetOverview.unitMillionVnd,
      },
    );
  }

  protected yearText(view: QuarterSplitRowView): string {
    return this.moneyAmount(view.row.yearValue, this.moneyFormat(view));
  }

  /** Small muted caption under a VND quarter input (e.g. "25,5 tỷ"); `null` when not abbreviated. */
  protected quarterCaption(view: QuarterSplitRowView, key: QuarterKey): string | null {
    const format = this.moneyFormat(view);
    return format.divisor === 1 ? null : this.moneyAmount(view.values[key], format);
  }

  private moneyAmount(value: number, format: ColumnFormat): string {
    return format.divisor === 1
      ? this.formatValue(value)
      : `${this.formatValue(value / format.divisor)} ${format.unit}`;
  }

  protected helpText(): string {
    return this.localization.branchQuarterSplit.help.replace('{year}', `${this.year - 1}`);
  }

  protected quarterHeader(quarter: number): string {
    return this.localization.branchQuarterSplit.columnQuarter.replace('{quarter}', `${quarter}`);
  }

  protected quarterAria(row: QuarterSplitRow, index: number): string {
    return this.localization.branchQuarterSplit.quarterInputAria
      .replace('{quarter}', `${QUARTER_NUMBERS[index]}`)
      .replace('{name}', row.kpiName);
  }

  protected allocateAria(row: QuarterSplitRow): string {
    return this.localization.branchQuarterSplit.allocateAria.replace('{name}', row.kpiName);
  }

  /** Grouped number for read-only cells, split values and totals. */
  protected formatValue(value: number): string {
    return formatValue(value, this.numberLocale());
  }

  protected formatUnits(units: bigint): string {
    return formatUnits(units, this.numberLocale());
  }

  protected ratioNote(row: QuarterSplitRow): string {
    return this.localization.branchQuarterSplit.ratioNote.replace(
      '{value}',
      this.formatValue(row.yearValue),
    );
  }

  protected totalText(view: QuarterSplitRowView): string {
    const text = this.localization.branchQuarterSplit;
    const format = this.moneyFormat(view);
    const sum = this.moneyAmount(valueOfUnits(view.sumUnits), format);
    if (view.matched) {
      return text.totalMatched.replace('{sum}', sum);
    }
    if (view.diffUnits > 0n) {
      return text.totalShort
        .replace('{sum}', sum)
        .replace('{diff}', this.moneyAmount(valueOfUnits(view.diffUnits), format));
    }
    return text.totalExcess
      .replace('{sum}', sum)
      .replace('{diff}', this.moneyAmount(valueOfUnits(-view.diffUnits), format));
  }

  protected mismatchLines(): string[] {
    const text = this.localization.branchQuarterSplit;
    return this.rows()
      .filter((view) => !view.row.isRatio && !view.matched)
      .map((view) => {
        const missing = view.diffUnits > 0n;
        const template = missing ? text.mismatchShort : text.mismatchExcess;
        return template
          .replace('{name}', view.row.kpiName)
          .replace('{sum}', this.formatUnits(view.sumUnits))
          .replace('{year}', this.formatValue(view.row.yearValue))
          .replace('{diff}', this.formatUnits(missing ? view.diffUnits : -view.diffUnits));
      });
  }

  /** `app-allocation-cell` commits on blur/Enter with the parsed number directly. */
  protected onCellValueChange(view: QuarterSplitRowView, key: QuarterKey, value: number): void {
    const edited = new Set(this.editedInputs());
    const cellKey = `${view.row.kpiId}:${key}`;
    edited.add(cellKey);
    this.editedInputs.set(edited);

    if (this.suggestedCells().has(cellKey)) {
      const suggested = new Set(this.suggestedCells());
      suggested.delete(cellKey);
      this.suggestedCells.set(suggested);
    }

    const values = { ...(this.draft()[view.row.kpiId] ?? valuesOf(view.row)) };
    values[key] = value;
    this.draft.set({ ...this.draft(), [view.row.kpiId]: values });
  }

  // ---------------------------------------------------------------------------
  // ✦ Gợi ý phân bổ (tier 1): POST /target-suggestions/{year}, D1 default basis TARGET_LAST_YEAR.
  // ---------------------------------------------------------------------------

  protected openSuggestPopover(): void {
    if (this.readOnly()) {
      return;
    }
    this.suggestError.set(null);
    this.showSuggestPopover.set(true);
  }

  protected closeSuggestPopover(): void {
    this.showSuggestPopover.set(false);
  }

  /** Has an unapplied suggestion the user hasn't confirmed (Apply) or discarded (Undo) yet — used to confirm
   * before leaving the step (see `TargetSetupWizardComponent`). */
  isSuggestionPending(): boolean {
    return this.pendingSuggestion() !== null;
  }

  protected onSuggestionCreated(request: SuggestAllocationRequest): void {
    const split = this.data();
    if (!split || this.readOnly()) {
      return;
    }

    const locked = this.lockedQuarters();
    const dto: SuggestTargetsRequestDto = {
      tier: 1,
      basis: request.basis,
      anchorId: this.branchYearId,
      // W3 has no per-row selection; "Chưa khớp" filters to mismatched rows, every other scope suggests all.
      kpiIds:
        request.scope === 'UNMATCHED'
          ? split.rows.filter((row) => !row.isRatio && !row.matched).map((row) => row.kpiId)
          : null,
      departmentId: null,
      employees: null,
      periods: 'YEAR_PLUS_4Q',
      overwrite: request.applyMode === 'EMPTY_ONLY' ? 'EMPTY_ONLY' : 'UNLOCKED',
      currentValues: split.rows
        .filter((row) => !row.isRatio)
        .map((row) => {
          const values = this.draft()[row.kpiId] ?? valuesOf(row);
          return {
            unitId: row.kpiId,
            year: row.yearValue,
            q1: locked.has(1) ? values.q1 : null,
            q2: locked.has(2) ? values.q2 : null,
            q3: locked.has(3) ? values.q3 : null,
            q4: locked.has(4) ? values.q4 : null,
            locked: false,
          };
        }),
      rounding: {
        moneyStep: request.rounding.moneyStep,
        countStep: request.rounding.countStep,
        mode: request.rounding.mode,
      },
      remainder: {
        method: request.rounding.remainderMethod === 'BUFFER_UNIT' ? 'BUFFER' : 'LARGEST_REMAINDER',
        bufferUnitId: request.rounding.bufferUnitId,
      },
    };

    this.suggestLoading.set(true);
    this.suggestError.set(null);
    this.suggestionApi
      .suggest(this.year, dto)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.suggestLoading.set(false);
          this.showSuggestPopover.set(false);
          this.applySuggestionResponse(response, request.basis, request.rounding.remainderMethod);
        },
        error: () => {
          this.suggestLoading.set(false);
          this.suggestError.set(this.localization.branchQuarterSplit.suggestBlocked);
        },
      });
  }

  /** Fills the draft with the suggested values right away (matches the W4 mockup) and opens the preview banner;
   * nothing is saved until "Lưu nháp" is pressed, and "Hoàn tác" reverts to `previousDraft`. */
  private applySuggestionResponse(
    response: SuggestTargetsResponseDto,
    basis: SuggestionBasis,
    remainderMethod: SuggestionRemainderMethod,
  ): void {
    const previousDraft = cloneValues(this.draft());
    const draft = { ...this.draft() };
    const touchedKeys = new Set<string>();
    let cellCount = 0;
    let kpiCount = 0;
    let fallbackCount = 0;

    for (const kpi of response.kpis) {
      const cell: SuggestionCellDto | undefined = kpi.cells[0];
      if (!cell) {
        continue;
      }
      const values = { ...(draft[kpi.kpiId] ?? { q1: 0, q2: 0, q3: 0, q4: 0 }) };
      let kpiTouched = false;
      (['q1', 'q2', 'q3', 'q4'] as const).forEach((key) => {
        const value = cell[key];
        if (value === null || cell.locked) {
          return;
        }
        values[key] = value;
        touchedKeys.add(`${kpi.kpiId}:${key}`);
        kpiTouched = true;
        cellCount += 1;
      });
      if (kpiTouched) {
        draft[kpi.kpiId] = values;
        kpiCount += 1;
        fallbackCount += cell.usedFallbackAverage ? 1 : 0;
      }
    }

    this.draft.set(draft);
    const suggested = new Set(this.suggestedCells());
    touchedKeys.forEach((key) => suggested.add(key));
    this.suggestedCells.set(suggested);
    this.pendingSuggestion.set({
      response,
      previousDraft,
      touchedKeys,
      basis,
      remainderMethod,
      cellCount,
      kpiCount,
      fallbackCount,
      warnings: [...response.warnings, ...response.kpis.flatMap((kpi) => kpi.warnings)],
    });
  }

  protected applyPendingSuggestion(): void {
    this.pendingSuggestion.set(null);
  }

  protected undoPendingSuggestion(): void {
    const pending = this.pendingSuggestion();
    if (!pending) {
      return;
    }
    this.draft.set(pending.previousDraft);
    const suggested = new Set(this.suggestedCells());
    pending.touchedKeys.forEach((key) => suggested.delete(key));
    this.suggestedCells.set(suggested);
    const edited = new Set(this.editedInputs());
    pending.touchedKeys.forEach((key) => edited.delete(key));
    this.editedInputs.set(edited);
    this.pendingSuggestion.set(null);
  }

  protected changeSuggestionBasis(): void {
    this.undoPendingSuggestion();
    this.showSuggestPopover.set(true);
  }

  protected save(): void {
    const split = this.data();
    if (!split || this.readOnly() || this.saving()) {
      return;
    }

    const draft = this.draft();
    this.saving.set(true);
    this.saveError.set(null);
    this.targetApi
      .saveQuarterSplit(this.branchYearId, {
        rows: split.rows
          .filter((row) => !row.isRatio)
          .map((row) => ({
            kpiId: row.kpiId,
            ...(draft[row.kpiId] ?? valuesOf(row)),
          })),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (next) => {
          this.applyState(next);
          this.saving.set(false);
          this.showToast(this.localization.branchQuarterSplit.saveSuccess);
          this.saved.emit();
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          this.saveError.set(this.saveErrorText(error));
        },
      });
  }

  protected requestReimport(): void {
    this.reimport.emit();
  }

  protected requestAllocation(row: QuarterSplitRow): void {
    this.allocateKpi.emit(row.kpiId);
  }

  private applyState(split: QuarterSplit): void {
    const draft: Record<string, QuarterValues> = {};
    for (const row of split.rows) {
      draft[row.kpiId] = valuesOf(row);
    }
    this.draft.set(draft);
    this.savedDraft.set(cloneValues(draft));
    this.editedInputs.set(new Set());
    this.data.set(split);
    this.pendingSuggestion.set(null);
    this.suggestedCells.set(new Set());
    this.showSuggestPopover.set(false);
  }

  private saveErrorText(error: HttpErrorResponse): string {
    const code = (error.error as ApiResponse<unknown> | undefined)?.errorCode;
    const text = this.localization.branchQuarterSplit;
    if (code === 'PERIOD_LOCKED') {
      return text.saveFailedPeriodLocked;
    }
    if (code === 'TARGET_INVALID_STATE') {
      return text.saveFailedInvalidState;
    }
    return text.saveFailed;
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

  private numberLocale(): string {
    return numberLocale(this.i18n.currentLanguage());
  }
}
