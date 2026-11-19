import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { forkJoin, map, Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ApiResponse } from '../../core/api-response.model';
import { KpiAllocation, QuarterSplit } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import {
  AllocationCellState,
  SuggestAllocationRequest,
  SuggestionBasis,
  SuggestionRemainderMethod,
  TierStatusCounts,
} from '../../shared/target/target-shared.models';
import { QuarterKey, QUARTERS } from './quarter-suggestion.utils';
import { roundValue, unitsOf } from './target-math';
import {
  SuggestTargetsRequestDto,
  SuggestTargetsResponseDto,
  TargetSetupApiService,
} from './target-setup-api.service';

export type CellKey = 'year' | QuarterKey;
export type DepartmentValues = Record<CellKey, number>;
export const CELL_KEYS: readonly CellKey[] = ['year', ...QUARTERS];

/** Tier-2 state of one KPI: every column adds up and every department's quarters equal its year. */
export type Tier2KpiState = 'MATCHED' | 'MISMATCHED' | 'NOT_SPLIT';

export interface Tier2ColumnCheck {
  totalUnits: bigint;
  branchUnits: bigint;
  remainingUnits: bigint;
}

export interface Tier2KpiCheck {
  state: Tier2KpiState;
  columns: Record<CellKey, Tier2ColumnCheck>;
  /** Departments whose own quarters do not add up to their year. */
  mismatchedDepartments: ReadonlySet<string>;
}

export interface PendingTier2Suggestion {
  previousDrafts: Record<string, Record<string, DepartmentValues>>;
  touched: ReadonlySet<string>;
  basis: SuggestionBasis;
  remainderMethod: SuggestionRemainderMethod;
  cellCount: number;
  kpiCount: number;
  /** KPIs suggested without last year's data (headcount split, progressive quarters). */
  fallbackCount: number;
  warnings: readonly string[];
}

export interface Tier2SaveResult {
  saved: number;
  /** Error code (or '' when the API sent none) per KPI that failed to save. */
  errors: ReadonlyMap<string, string>;
}

function valuesOf(row: KpiAllocation['departments'][number]): DepartmentValues {
  return {
    year: roundValue(row.year),
    q1: roundValue(row.q1),
    q2: roundValue(row.q2),
    q3: roundValue(row.q3),
    q4: roundValue(row.q4),
  };
}

function isMissingRow(row: KpiAllocation['departments'][number]): boolean {
  return row.deptYearId === null && row.headcount > 0;
}

function draftsOf(allocation: KpiAllocation): Record<string, DepartmentValues> {
  const draft: Record<string, DepartmentValues> = {};
  for (const department of allocation.departments) {
    draft[department.departmentId] = valuesOf(department);
  }
  return draft;
}

export function tier2CellId(kpiId: string, departmentId: string, key: CellKey): string {
  return `${kpiId}:${departmentId}:${key}`;
}

/**
 * Shared draft state of tier 2 (branch -> departments) for the W5 "by KPI" and W6 "matrix" views: every
 * allocatable KPI from the bulk `GET /{branchYearId}/allocations`, the unsaved edits, the ✦ suggestion from
 * `POST /target-suggestions/{year}` (tier 2, the only way to suggest: no equal or headcount split) and the save.
 * Provided per component, so each view keeps its own draft.
 */
@Injectable()
export class Tier2AllocationStore {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly suggestionApi = inject(TargetSetupApiService);

  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly saving = signal(false);
  readonly suggestLoading = signal(false);
  readonly suggestFailed = signal(false);
  readonly split = signal<QuarterSplit | null>(null);
  readonly allocations = signal<readonly KpiAllocation[]>([]);
  readonly pending = signal<PendingTier2Suggestion | null>(null);
  readonly saveErrors = signal<ReadonlyMap<string, string>>(new Map());
  private readonly drafts = signal<Record<string, Record<string, DepartmentValues>>>({});
  private readonly suggested = signal<ReadonlySet<string>>(new Set());
  private readonly edited = signal<ReadonlySet<string>>(new Set());
  /** `kpiId:departmentId` of eligible departments with no saved row the user chose to save as 0. */
  private readonly confirmedEmpty = signal<ReadonlySet<string>>(new Set());

  readonly readOnly = computed(() => this.split()?.status !== 'DRAFT');
  readonly lockedQuarters = computed<ReadonlySet<QuarterKey>>(
    () =>
      new Set(
        (this.split()?.quarters ?? []).filter((q) => q.locked).map((q) => QUARTERS[q.quarter - 1]),
      ),
  );
  /** Every non-ratio KPI of the branch-year, in quarter-split order (KPI group code, then KPI code). */
  readonly kpiRows = computed(() => (this.split()?.rows ?? []).filter((row) => !row.isRatio));
  readonly ratioRows = computed(() => (this.split()?.rows ?? []).filter((row) => row.isRatio));

  private readonly allocationById = computed(
    () => new Map(this.allocations().map((allocation) => [allocation.kpiId, allocation])),
  );

  readonly checks = computed<ReadonlyMap<string, Tier2KpiCheck>>(() => {
    const drafts = this.drafts();
    const checks = new Map<string, Tier2KpiCheck>();
    for (const allocation of this.allocations()) {
      const draft = drafts[allocation.kpiId] ?? {};
      const rows = allocation.departments.map((d) => draft[d.departmentId] ?? valuesOf(d));
      const columns = {} as Record<CellKey, Tier2ColumnCheck>;
      for (const key of CELL_KEYS) {
        const totalUnits = rows.reduce((sum, values) => sum + unitsOf(values[key]), 0n);
        const branchUnits = unitsOf(allocation.branch[key]);
        columns[key] = { totalUnits, branchUnits, remainingUnits: branchUnits - totalUnits };
      }
      const mismatchedDepartments = new Set<string>();
      allocation.departments.forEach((department, index) => {
        const values = rows[index];
        const quarterUnits = QUARTERS.reduce((sum, key) => sum + unitsOf(values[key]), 0n);
        if (quarterUnits !== unitsOf(values.year)) {
          mismatchedDepartments.add(department.departmentId);
        }
      });
      const empty =
        allocation.branch.year !== 0 &&
        rows.every((values) => CELL_KEYS.every((key) => values[key] === 0));
      const matched =
        mismatchedDepartments.size === 0 &&
        CELL_KEYS.every((key) => columns[key].remainingUnits === 0n);
      checks.set(allocation.kpiId, {
        state: empty ? 'NOT_SPLIT' : matched ? 'MATCHED' : 'MISMATCHED',
        columns,
        mismatchedDepartments,
      });
    }
    return checks;
  });

  readonly counts = computed<TierStatusCounts>(() => {
    const rows = this.kpiRows();
    let matched = 0;
    let mismatched = 0;
    for (const row of rows) {
      const state = this.kpiState(row.kpiId);
      if (state === 'MATCHED') {
        matched += 1;
      } else if (state === 'MISMATCHED') {
        mismatched += 1;
      }
    }
    return {
      matched,
      total: rows.length,
      mismatched,
      notSplit: rows.length - matched - mismatched,
    };
  });

  readonly allMatched = computed(() => {
    const counts = this.counts();
    return counts.total > 0 && counts.matched === counts.total;
  });

  /**
   * KPIs where an eligible department has no saved row yet: the server needs one (even a 0) per eligible
   * department before it lets the year be published, so these block "Tiếp: Phát hành" even when the sums match.
   */
  readonly missingItems = computed(() =>
    this.allocations()
      .map((allocation) => ({
        kpiId: allocation.kpiId,
        kpiName: allocation.kpiName,
        departments: allocation.departments.filter(isMissingRow).map((d) => d.departmentName),
      }))
      .filter((item) => item.departments.length > 0),
  );

  /** Eligible departments still without a row that the user has not yet marked to save as 0. */
  readonly canConfirmMissing = computed(() =>
    this.allocations().some((allocation) =>
      allocation.departments.some(
        (d) =>
          isMissingRow(d) && !this.confirmedEmpty().has(`${allocation.kpiId}:${d.departmentId}`),
      ),
    ),
  );

  readonly dirtyKpiIds = computed<string[]>(() => {
    const drafts = this.drafts();
    const confirmed = this.confirmedEmpty();
    return this.allocations()
      .filter((allocation) => {
        const draft = drafts[allocation.kpiId];
        return (
          allocation.departments.some(
            (d) => isMissingRow(d) && confirmed.has(`${allocation.kpiId}:${d.departmentId}`),
          ) ||
          (draft !== undefined &&
            allocation.departments.some((department) => {
              const saved = valuesOf(department);
              const current = draft[department.departmentId] ?? saved;
              return CELL_KEYS.some((key) => unitsOf(current[key]) !== unitsOf(saved[key]));
            }))
        );
      })
      .map((allocation) => allocation.kpiId);
  });

  load(branchYearId: string): Observable<void> {
    this.loading.set(true);
    this.failed.set(false);
    return forkJoin([
      this.targetApi.getQuarterSplit(branchYearId),
      this.targetApi.getAllAllocations(branchYearId),
    ]).pipe(
      map(([split, allocations]) => {
        this.split.set(split);
        this.allocations.set(allocations);
        this.resetDrafts();
        this.loading.set(false);
      }),
      catchError(() => {
        this.failed.set(true);
        this.loading.set(false);
        return of(undefined);
      }),
    );
  }

  allocation(kpiId: string): KpiAllocation | null {
    return this.allocationById().get(kpiId) ?? null;
  }

  /** `NOT_SPLIT` also covers a KPI the bulk route omits because its quarter split is not ready yet. */
  kpiState(kpiId: string): Tier2KpiState {
    return this.checks().get(kpiId)?.state ?? 'NOT_SPLIT';
  }

  check(kpiId: string): Tier2KpiCheck | null {
    return this.checks().get(kpiId) ?? null;
  }

  values(kpiId: string, departmentId: string): DepartmentValues | null {
    const draft = this.drafts()[kpiId]?.[departmentId];
    if (draft) {
      return draft;
    }
    const department = this.allocation(kpiId)?.departments.find(
      (d) => d.departmentId === departmentId,
    );
    return department ? valuesOf(department) : null;
  }

  cellState(kpiId: string, departmentId: string, key: CellKey): AllocationCellState {
    if (key !== 'year' && this.lockedQuarters().has(key)) {
      return 'LOCKED';
    }
    const id = tier2CellId(kpiId, departmentId, key);
    if (this.check(kpiId)?.mismatchedDepartments.has(departmentId)) {
      return 'MISMATCH';
    }
    if (key === 'year' && this.isMissing(kpiId, departmentId)) {
      return 'MISMATCH';
    }
    if (this.suggested().has(id)) {
      return 'SUGGESTED';
    }
    return this.edited().has(id) ? 'MANUAL' : 'DEFAULT';
  }

  setCell(kpiId: string, departmentId: string, key: CellKey, value: number): void {
    const current = this.values(kpiId, departmentId);
    if (!current || this.readOnly()) {
      return;
    }
    if (unitsOf(current[key]) === unitsOf(value)) {
      // Typing the value a missing department already shows (0) still means "save this row".
      this.confirmMissing(kpiId, departmentId);
      return;
    }
    this.confirmMissing(kpiId, departmentId);
    const kpiDraft = { ...(this.drafts()[kpiId] ?? {}) };
    kpiDraft[departmentId] = { ...current, [key]: roundValue(value) };
    this.drafts.set({ ...this.drafts(), [kpiId]: kpiDraft });
    const id = tier2CellId(kpiId, departmentId, key);
    this.edited.set(new Set(this.edited()).add(id));
    const suggested = new Set(this.suggested());
    suggested.delete(id);
    this.suggested.set(suggested);
  }

  /**
   * `POST /target-suggestions/{year}` tier 2 for `kpiIds`. The grid's unsaved values are sent as
   * `currentValues` only for a single KPI (the contract keys them by department, not by KPI); for several KPIs
   * the engine reads the saved draft. The result is filled into the draft at once and kept pending until
   * Apply/Undo on the preview banner; nothing is saved until "Lưu nháp".
   */
  suggest(
    year: number,
    branchYearId: string,
    kpiIds: readonly string[],
    request: SuggestAllocationRequest,
  ): Observable<boolean> {
    const single = kpiIds.length === 1 ? this.allocation(kpiIds[0]) : null;
    const emptyAsNull = (value: number): number | null => (value === 0 ? null : value);
    const dto: SuggestTargetsRequestDto = {
      tier: 2,
      basis: request.basis,
      anchorId: branchYearId,
      kpiIds,
      departmentId: null,
      employees: null,
      periods: 'YEAR_PLUS_4Q',
      overwrite: request.applyMode === 'EMPTY_ONLY' ? 'EMPTY_ONLY' : 'UNLOCKED',
      currentValues: single
        ? single.departments.map((department) => {
            const values = this.values(single.kpiId, department.departmentId)!;
            return {
              unitId: department.departmentId,
              year: emptyAsNull(values.year),
              q1: emptyAsNull(values.q1),
              q2: emptyAsNull(values.q2),
              q3: emptyAsNull(values.q3),
              q4: emptyAsNull(values.q4),
              locked: false,
            };
          })
        : [],
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
    this.suggestFailed.set(false);
    return this.suggestionApi.suggest(year, dto).pipe(
      map((response) => {
        this.suggestLoading.set(false);
        this.applySuggestion(response, request);
        return true;
      }),
      catchError(() => {
        this.suggestLoading.set(false);
        this.suggestFailed.set(true);
        return of(false);
      }),
    );
  }

  /** True while an eligible department has no saved row for the KPI and the user has not confirmed saving it. */
  isMissing(kpiId: string, departmentId: string): boolean {
    const department = this.allocation(kpiId)?.departments.find(
      (d) => d.departmentId === departmentId,
    );
    return (
      !!department &&
      isMissingRow(department) &&
      !this.confirmedEmpty().has(`${kpiId}:${departmentId}`)
    );
  }

  /** Marks every missing department row to be saved as 0 (the next "Lưu nháp" creates them). */
  confirmAllMissing(): void {
    const next = new Set(this.confirmedEmpty());
    for (const allocation of this.allocations()) {
      for (const department of allocation.departments.filter(isMissingRow)) {
        next.add(`${allocation.kpiId}:${department.departmentId}`);
      }
    }
    this.confirmedEmpty.set(next);
  }

  private confirmMissing(kpiId: string, departmentId: string): void {
    const department = this.allocation(kpiId)?.departments.find(
      (d) => d.departmentId === departmentId,
    );
    if (department && isMissingRow(department)) {
      this.confirmedEmpty.set(new Set(this.confirmedEmpty()).add(`${kpiId}:${departmentId}`));
    }
  }

  acceptSuggestion(): void {
    this.pending.set(null);
  }

  undoSuggestion(): void {
    const pending = this.pending();
    if (!pending) {
      return;
    }
    this.drafts.set(pending.previousDrafts);
    const suggested = new Set(this.suggested());
    pending.touched.forEach((id) => suggested.delete(id));
    this.suggested.set(suggested);
    this.pending.set(null);
  }

  /** Saves every KPI with unsaved changes (one `PUT .../kpis/{kpiId}/allocation` each). */
  save(branchYearId: string): Observable<Tier2SaveResult> {
    const dirty = this.dirtyKpiIds();
    if (dirty.length === 0 || this.readOnly()) {
      return of({ saved: 0, errors: new Map() });
    }
    this.saving.set(true);
    this.saveErrors.set(new Map());
    const calls = dirty.map((kpiId) => {
      const allocation = this.allocation(kpiId)!;
      return this.targetApi
        .saveKpiAllocation(branchYearId, kpiId, {
          departments: allocation.departments.map((department) => ({
            departmentId: department.departmentId,
            ...this.values(kpiId, department.departmentId)!,
          })),
        })
        .pipe(
          map((saved) => ({ kpiId, saved, error: null as string | null })),
          catchError((error: HttpErrorResponse) =>
            of({
              kpiId,
              saved: null,
              error: (error.error as ApiResponse<unknown> | undefined)?.errorCode ?? '',
            }),
          ),
        );
    });
    return forkJoin(calls).pipe(
      map((results) => {
        const errors = new Map<string, string>();
        const savedById = new Map<string, KpiAllocation>();
        for (const result of results) {
          if (result.saved) {
            savedById.set(result.kpiId, result.saved);
          } else {
            errors.set(result.kpiId, result.error ?? '');
          }
        }
        this.allocations.set(
          this.allocations().map((allocation) => savedById.get(allocation.kpiId) ?? allocation),
        );
        const drafts = { ...this.drafts() };
        savedById.forEach((saved, kpiId) => (drafts[kpiId] = draftsOf(saved)));
        this.drafts.set(drafts);
        this.clearMarks(new Set(savedById.keys()));
        this.saveErrors.set(errors);
        this.pending.set(null);
        this.saving.set(false);
        return { saved: savedById.size, errors };
      }),
    );
  }

  private applySuggestion(
    response: SuggestTargetsResponseDto,
    request: SuggestAllocationRequest,
  ): void {
    const previousDrafts = this.drafts();
    const drafts = { ...previousDrafts };
    const touched = new Set<string>();
    const locked = this.lockedQuarters();
    let kpiCount = 0;
    let fallbackCount = 0;
    const warnings = [...response.warnings];
    for (const kpi of response.kpis) {
      warnings.push(...kpi.warnings);
      const allocation = this.allocation(kpi.kpiId);
      if (!allocation) {
        continue;
      }
      const kpiDraft = { ...(drafts[kpi.kpiId] ?? draftsOf(allocation)) };
      let kpiTouched = false;
      for (const cell of kpi.cells) {
        const current = kpiDraft[cell.unitId];
        if (!current || cell.locked) {
          continue;
        }
        const values = { ...current };
        let skippedLocked = false;
        for (const key of CELL_KEYS) {
          const value = cell[key];
          if (value === null || (key !== 'year' && locked.has(key))) {
            skippedLocked ||= value !== null;
            continue;
          }
          values[key] = roundValue(value);
          touched.add(tier2CellId(kpi.kpiId, cell.unitId, key));
          kpiTouched = true;
        }
        if (skippedLocked) {
          // Locked quarters keep their value, so the year follows the quarters to keep the row adding up.
          values.year = roundValue(QUARTERS.reduce((sum, key) => sum + values[key], 0));
        }
        kpiDraft[cell.unitId] = values;
      }
      if (kpiTouched) {
        drafts[kpi.kpiId] = kpiDraft;
        kpiCount += 1;
        fallbackCount += kpi.cells.some((cell) => cell.usedFallbackAverage) ? 1 : 0;
      }
    }
    this.drafts.set(drafts);
    const suggested = new Set(this.suggested());
    const edited = new Set(this.edited());
    touched.forEach((id) => {
      suggested.add(id);
      edited.delete(id);
    });
    this.suggested.set(suggested);
    this.edited.set(edited);
    this.pending.set({
      previousDrafts,
      touched,
      basis: request.basis,
      remainderMethod: request.rounding.remainderMethod,
      cellCount: touched.size,
      kpiCount,
      fallbackCount,
      warnings,
    });
  }

  private resetDrafts(): void {
    const drafts: Record<string, Record<string, DepartmentValues>> = {};
    for (const allocation of this.allocations()) {
      drafts[allocation.kpiId] = draftsOf(allocation);
    }
    this.drafts.set(drafts);
    this.suggested.set(new Set());
    this.edited.set(new Set());
    this.confirmedEmpty.set(new Set());
    this.pending.set(null);
    this.saveErrors.set(new Map());
  }

  private clearMarks(kpiIds: ReadonlySet<string>): void {
    const keep = (id: string) => !kpiIds.has(id.split(':')[0]);
    this.suggested.set(new Set([...this.suggested()].filter(keep)));
    this.edited.set(new Set([...this.edited()].filter(keep)));
  }
}
