/**
 * Shared, presentation-only types for the Target Allocation v3 components under `shared/target/**`.
 * Nothing here calls HTTP or depends on a feature module; the owning feature maps its own DTOs onto these.
 */

// ---------------------------------------------------------------------------
// SuggestAllocationPopover
// ---------------------------------------------------------------------------

export type SuggestionBasis = 'TARGET_LAST_YEAR' | 'ACTUAL_LAST_YEAR';

export type SuggestionScope = 'CURRENT_KPI' | 'GROUP' | 'UNMATCHED' | 'SELECTED';

export type SuggestionEmployeeScope = 'ALL' | 'NEW_ONLY';

export type SuggestionPeriodScope = 'YEAR_AND_QUARTERS' | 'YEAR_ONLY' | 'CURRENT_QUARTER';

export type SuggestionApplyMode = 'EMPTY_ONLY' | 'OVERWRITE_UNLOCKED';

export type SuggestionRoundingMode = 'NEAREST' | 'UP' | 'DOWN';

export type SuggestionRemainderMethod = 'LARGEST_REMAINDER' | 'BUFFER_UNIT';

export interface SuggestionBufferUnitOption {
  readonly id: string;
  readonly label: string;
}

/** D8: default rounding steps (10 tr VND for money, 1 for counts). */
export const DEFAULT_MONEY_STEP = 10_000_000;
export const DEFAULT_COUNT_STEP = 1;

export interface SuggestionRoundingConfig {
  readonly moneyStep: number;
  readonly countStep: number;
  readonly mode: SuggestionRoundingMode;
  /** D1: largest remainder by default; a buffer unit is opt-in and only for an actual-basis suggestion. */
  readonly remainderMethod: SuggestionRemainderMethod;
  readonly bufferUnitId: string | null;
}

export interface SuggestionPreview {
  readonly cellsToFill: number;
  readonly kpiCount: number;
  readonly sumCheckPassed: boolean;
  readonly warnings: readonly string[];
}

export interface SuggestAllocationRequest {
  readonly basis: SuggestionBasis;
  readonly scope: SuggestionScope;
  /** Only meaningful for the tier-3 (employee) popover. */
  readonly employeeScope: SuggestionEmployeeScope | null;
  readonly periodScope: SuggestionPeriodScope | null;
  readonly applyMode: SuggestionApplyMode;
  readonly rounding: SuggestionRoundingConfig;
}

// ---------------------------------------------------------------------------
// AllocationCell / CellLegend
// ---------------------------------------------------------------------------

export type AllocationCellState = 'DEFAULT' | 'SUGGESTED' | 'MANUAL' | 'MISMATCH' | 'LOCKED';

// ---------------------------------------------------------------------------
// TierStatusLine
// ---------------------------------------------------------------------------

export type TierStatusFilter = 'ALL' | 'MISMATCHED' | 'NOT_SPLIT';

export interface TierStatusCounts {
  readonly matched: number;
  readonly total: number;
  readonly mismatched: number;
  readonly notSplit: number;
}

// ---------------------------------------------------------------------------
// KpiGroupTable
// ---------------------------------------------------------------------------

/** The 7 KPI groups used across the target allocation module, in display order. */
export const KPI_GROUP_CATALOG: readonly string[] = [
  'Huy động vốn',
  'Tín dụng',
  'Thẻ',
  'Ngân hàng số',
  'Bảo hiểm',
  'Dịch vụ tại quầy',
  'Vận hành & tuân thủ',
];

export interface KpiGroupTableRow {
  readonly id: string;
  readonly kpiName: string;
  readonly kpiType: string;
}

export interface KpiGroupTableGroup {
  readonly id: string;
  readonly name: string;
  /** A complete group starts collapsed; an incomplete (problem) group starts open. */
  readonly complete: boolean;
  readonly rows: readonly KpiGroupTableRow[];
}

// ---------------------------------------------------------------------------
// PaceBar
// ---------------------------------------------------------------------------

export type PaceBarKind = 'NORMAL' | 'BALANCE' | 'RATIO';

/** D16 pace status: on track (gap >= 0), slow (-10 <= gap < 0), at risk (gap < -10). */
export type PaceStatus = 'ON_TRACK' | 'SLOW' | 'AT_RISK';

export function paceStatusFor(gapPercent: number): PaceStatus {
  if (gapPercent >= 0) return 'ON_TRACK';
  if (gapPercent >= -10) return 'SLOW';
  return 'AT_RISK';
}
