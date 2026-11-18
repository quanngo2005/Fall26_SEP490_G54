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

const QUARTERS: readonly QuarterKey[] = ['q1', 'q2', 'q3', 'q4'];
const QUARTER_NUMBERS: readonly number[] = [1, 2, 3, 4];
const TOAST_MS = 3000;

function valuesOf(row: QuarterSplitRow): QuarterValues {
  return {
    q1: roundValue(row.q1),
    q2: roundValue(row.q2),
    q3: roundValue(row.q3),
    q4: roundValue(row.q4),
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
 * four quarters, with client-side totals, even / previous-year / file-ratio filling and a draft save.
 */
@Component({
  selector: 'app-branch-quarter-split',
  imports: [LayoutIconComponent],
  templateUrl: './branch-quarter-split.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/workspace-page.part2.scss',
    './branch-quarter-split.component.scss',
    './branch-quarter-split.component.part2.scss',
  ],
})
export class BranchQuarterSplitComponent extends LocalizedComponent implements OnChanges {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) branchYearId!: string;
  @Input({ required: true }) year!: number;
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
  private readonly draft = signal<Record<string, QuarterValues>>({});
  private readonly savedDraft = signal<Record<string, QuarterValues>>({});
  private readonly rawInputs = signal<ReadonlyMap<string, string>>(new Map());
  private readonly editedInputs = signal<ReadonlySet<string>>(new Set());
  private toastTimer?: ReturnType<typeof setTimeout>;

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
  protected readonly hasPreviousYear = computed(() =>
    (this.data()?.rows ?? []).some((row) => !row.isRatio && row.previousYear !== null),
  );
  protected readonly hasFileRatios = computed(() => this.data()?.fileRatios !== null);

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
    this.saveError.set(null);
    this.targetApi
      .getQuarterSplit(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (split) => {
          this.applyState(split);
          this.loading.set(false);
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

  protected previousYearLabel(): string {
    return this.localization.branchQuarterSplit.splitPreviousYear.replace(
      '{year}',
      `${this.year - 1}`,
    );
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

  protected inputText(view: QuarterSplitRowView, key: QuarterKey): string {
    const raw = this.rawInputs().get(`${view.row.kpiId}:${key}`);
    return raw ?? this.formatValue(view.values[key]);
  }

  protected ratioNote(row: QuarterSplitRow): string {
    return this.localization.branchQuarterSplit.ratioNote.replace(
      '{value}',
      this.formatValue(row.yearValue),
    );
  }

  protected totalText(view: QuarterSplitRowView): string {
    const text = this.localization.branchQuarterSplit;
    const sum = this.formatUnits(view.sumUnits);
    if (view.matched) {
      return text.totalMatched.replace('{sum}', sum);
    }
    if (view.diffUnits > 0n) {
      return text.totalShort
        .replace('{sum}', sum)
        .replace('{diff}', this.formatUnits(view.diffUnits));
    }
    return text.totalExcess
      .replace('{sum}', sum)
      .replace('{diff}', this.formatUnits(-view.diffUnits));
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

  protected onQuarterFocus(view: QuarterSplitRowView, key: QuarterKey): void {
    const raw = new Map(this.rawInputs());
    raw.set(`${view.row.kpiId}:${key}`, editText(view.values[key], this.numberLocale()));
    this.rawInputs.set(raw);
  }

  protected onQuarterInput(view: QuarterSplitRowView, key: QuarterKey, event: Event): void {
    const input = event.target as HTMLInputElement;
    const raw = new Map(this.rawInputs());
    raw.set(`${view.row.kpiId}:${key}`, input.value);
    this.rawInputs.set(raw);

    const edited = new Set(this.editedInputs());
    edited.add(`${view.row.kpiId}:${key}`);
    this.editedInputs.set(edited);

    const values = { ...(this.draft()[view.row.kpiId] ?? valuesOf(view.row)) };
    values[key] = parseNumber(input.value, this.numberLocale());
    this.draft.set({ ...this.draft(), [view.row.kpiId]: values });
  }

  protected onQuarterBlur(view: QuarterSplitRowView, key: QuarterKey): void {
    const raw = new Map(this.rawInputs());
    raw.delete(`${view.row.kpiId}:${key}`);
    this.rawInputs.set(raw);
  }

  protected splitEvenly(): void {
    this.fill(() => null);
  }

  protected splitByPreviousYear(): void {
    this.fill((row) =>
      row.previousYear ? QUARTERS.map((key) => unitsOf(row.previousYear?.[key] ?? 0)) : null,
    );
  }

  protected splitByFileRatios(): void {
    const ratios = this.data()?.fileRatios;
    if (!ratios) {
      return;
    }
    this.fill(() => QUARTERS.map((key) => unitsOf(ratios[key])));
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

  /**
   * Fills every non-ratio row. Locked quarters keep their value, the rest of the year target is
   * shared over the unlocked ones by `weights` (null = equal shares), rounded to the KPI precision
   * with the remainder on the last unlocked quarter.
   */
  private fill(weightsOf: (row: QuarterSplitRow) => bigint[] | null): void {
    const split = this.data();
    if (!split || this.readOnly()) {
      return;
    }

    const draft = { ...this.draft() };
    const locked = this.lockedQuarters();

    for (const row of split.rows) {
      if (row.isRatio) {
        continue;
      }

      const values = { ...(draft[row.kpiId] ?? valuesOf(row)) };
      const unlocked = QUARTER_NUMBERS.filter((quarter) => !locked.has(quarter));
      if (unlocked.length === 0) {
        continue;
      }

      const lockedUnits = QUARTERS.reduce(
        (sum, key, index) =>
          locked.has(QUARTER_NUMBERS[index]) ? sum + unitsOf(values[key]) : sum,
        0n,
      );
      const remaining = unitsOf(row.yearValue) - lockedUnits;
      const weights = weightsOf(row) ?? QUARTERS.map(() => 1n);
      const unlockedWeights = QUARTERS.map((_, index) => index)
        .filter((index) => !locked.has(QUARTER_NUMBERS[index]))
        .map((index) => weights[index]);
      const step = stepFor(row.yearValue);
      const shares = distribute(remaining, unlockedWeights, step);

      unlocked.forEach((quarter, index) => {
        values[QUARTERS[quarter - 1]] = valueOfUnits(shares[index]);
      });
      draft[row.kpiId] = values;
    }

    this.draft.set(draft);
  }

  private applyState(split: QuarterSplit): void {
    const draft: Record<string, QuarterValues> = {};
    for (const row of split.rows) {
      draft[row.kpiId] = valuesOf(row);
    }
    this.draft.set(draft);
    this.savedDraft.set(cloneValues(draft));
    this.rawInputs.set(new Map());
    this.editedInputs.set(new Set());
    this.data.set(split);
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
