import { KPI_GROUP_CATALOG } from '../../shared/target/target-shared.models';
import {
  MyTargetKpi,
  MyTargetProgressKpi,
  MyTargetYear,
  PaceStatus,
} from '../../core/my-targets.models';

/** D16: KPI counted "increased" when it is at least 15% above last year's target. */
export const INCREASE_THRESHOLD_PERCENT = 15;

export const MAX_FEEDBACK_REASON_LENGTH = 500;

export type BannerKind = 'PENDING' | 'RECONFIRM' | 'CONFIRMED';

export interface BannerInfo {
  readonly kind: BannerKind;
  readonly kpiCount: number;
}

/** E1/E3 banner selection: the ack status drives which copy and action the page shows. */
export function bannerFor(data: MyTargetYear): BannerInfo {
  return { kind: data.ackStatus, kpiCount: data.kpis.length };
}

/** `(year - previousYearTarget) / previousYearTarget * 100`; null when there is no previous-year target (new KPI). */
export function deltaVsPreviousYearPercent(kpi: MyTargetKpi): number | null {
  if (kpi.previousYearTarget === null || kpi.previousYearTarget === 0) return null;
  return ((kpi.year - kpi.previousYearTarget) / kpi.previousYearTarget) * 100;
}

export function isNewKpi(kpi: MyTargetKpi): boolean {
  return kpi.previousYearTarget === null;
}

export function isIncreasedOver15(kpi: MyTargetKpi): boolean {
  const delta = deltaVsPreviousYearPercent(kpi);
  return delta !== null && delta > INCREASE_THRESHOLD_PERCENT;
}

export interface SummaryCards {
  readonly kpiCount: number;
  /** Average of each KPI's delta vs. last year's target, over KPIs that have one; null if none do. */
  readonly avgDeltaPercent: number | null;
  readonly increasedCount: number;
  readonly newCount: number;
  readonly feedbackCount: number;
}

export function summaryCards(data: MyTargetYear): SummaryCards {
  const deltas = data.kpis
    .map((kpi) => deltaVsPreviousYearPercent(kpi))
    .filter((delta): delta is number => delta !== null);
  return {
    kpiCount: data.kpis.length,
    avgDeltaPercent:
      deltas.length > 0 ? deltas.reduce((sum, d) => sum + d, 0) / deltas.length : null,
    increasedCount: data.kpis.filter(isIncreasedOver15).length,
    newCount: data.kpis.filter(isNewKpi).length,
    feedbackCount: data.kpis.filter((kpi) => kpi.hasFeedback).length,
  };
}

export interface KpiGroupChip {
  readonly id: string;
  readonly name: string;
  readonly count: number;
}

export interface KpiGroup {
  readonly id: string;
  readonly name: string;
  readonly kpis: readonly MyTargetKpi[];
}

/** Joins `kpiId -> kpiGroupName` (from `KpiDefinitionService`), then orders groups per the fixed 7-group catalog. */
export function groupKpis(
  kpis: readonly MyTargetKpi[],
  kpiGroupNameById: ReadonlyMap<string, string>,
): readonly KpiGroup[] {
  const byGroup = new Map<string, MyTargetKpi[]>();
  for (const kpi of kpis) {
    const groupName = kpiGroupNameById.get(kpi.kpiId) ?? '';
    const bucket = byGroup.get(groupName) ?? [];
    bucket.push(kpi);
    byGroup.set(groupName, bucket);
  }
  const order = [...KPI_GROUP_CATALOG, ''];
  return order
    .filter((name) => byGroup.has(name))
    .map((name) => ({ id: name || 'ungrouped', name, kpis: byGroup.get(name) ?? [] }));
}

export function groupChips(groups: readonly KpiGroup[]): readonly KpiGroupChip[] {
  return groups.map((g) => ({ id: g.id, name: g.name, count: g.kpis.length }));
}

/** A "changed" KPI's four quarters plus the year, reused to build the E3 struck-through/new value pairs. */
export interface ChangedCell {
  readonly oldValue: number;
  readonly newValue: number;
}

/**
 * The year total is the only value the change log always carries (`changedOldValue`/`changedNewValue`); quarter
 * cells have no individual before/after in the DTO, so only the year cell gets the struck-through treatment.
 */
export function changedYearCell(kpi: MyTargetKpi): ChangedCell | null {
  if (!kpi.changed || kpi.changedOldValue === null || kpi.changedNewValue === null) return null;
  return { oldValue: kpi.changedOldValue, newValue: kpi.changedNewValue };
}

// ---- E4/E6 progress -------------------------------------------------------------------------

export type ProgressBucket = 'NEEDS_ATTENTION' | 'ALL' | 'ON_PACE';

export function inNeedsAttention(status: PaceStatus): boolean {
  return status === 'SLOW' || status === 'AT_RISK';
}

export function inOnPace(status: PaceStatus): boolean {
  return status === 'ACHIEVED' || status === 'ON_PACE';
}

export interface ProgressCardCounts {
  readonly onPace: number;
  readonly slow: number;
  readonly atRisk: number;
  readonly inReview: number;
}

export function progressCardCounts(kpis: readonly MyTargetProgressKpi[]): ProgressCardCounts {
  return {
    onPace: kpis.filter((k) => inOnPace(k.progress.paceStatus)).length,
    slow: kpis.filter((k) => k.progress.paceStatus === 'SLOW').length,
    atRisk: kpis.filter((k) => k.progress.paceStatus === 'AT_RISK').length,
    inReview: kpis.reduce((sum, k) => sum + k.progress.inReviewCount, 0),
  };
}

export function filterProgress(
  kpis: readonly MyTargetProgressKpi[],
  bucket: ProgressBucket,
): readonly MyTargetProgressKpi[] {
  if (bucket === 'ALL') return kpis;
  if (bucket === 'NEEDS_ATTENTION')
    return kpis.filter((k) => inNeedsAttention(k.progress.paceStatus));
  return kpis.filter((k) => inOnPace(k.progress.paceStatus));
}

/** Pace gap, worst (most negative) first — D16 sort order for the quarter-progress table. */
export function sortByPaceGap(
  kpis: readonly MyTargetProgressKpi[],
): readonly MyTargetProgressKpi[] {
  return [...kpis].sort(
    (a, b) =>
      a.progress.achievedPercent -
      a.progress.expectedPacePercent -
      (b.progress.achievedPercent - b.progress.expectedPacePercent),
  );
}

export function missingToTarget(progress: MyTargetProgressKpi['progress']): number {
  return Math.max(0, progress.target - progress.achieved);
}

// ---- E6 year view -----------------------------------------------------------------------------

/** D16-adjacent: % of the year elapsed so far, for the "cumulative vs. time elapsed" card. */
export function yearElapsedPercent(today: string, year: number): number {
  const start = Date.UTC(year, 0, 1);
  const end = Date.UTC(year, 11, 31);
  const current = Date.parse(`${today}T00:00:00Z`);
  if (Number.isNaN(current)) return 0;
  const clamped = Math.max(start, Math.min(end, current));
  return ((clamped - start) / (end - start)) * 100;
}

export type QuarterKey = 'Q1' | 'Q2' | 'Q3' | 'Q4';
export const QUARTER_KEYS: readonly QuarterKey[] = ['Q1', 'Q2', 'Q3', 'Q4'];

export type QuarterCellState = 'ACHIEVED_PAST' | 'MISSED_PAST' | 'CURRENT' | 'FUTURE';

/** Calendar quarter (Jan-Mar, Apr-Jun, ...) end date, `YYYY-MM-DD`, used only to tell past/current/future apart. */
function quarterEndDate(year: number, quarter: QuarterKey): string {
  const month = QUARTER_KEYS.indexOf(quarter) * 3 + 2; // 0-based end month index
  const last = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(last).padStart(2, '0')}`;
}

function quarterStartDate(year: number, quarter: QuarterKey): string {
  const month = QUARTER_KEYS.indexOf(quarter) * 3;
  return `${year}-${String(month + 1).padStart(2, '0')}-01`;
}

type QuarterTimeState = 'PAST' | 'CURRENT' | 'FUTURE';

function quarterTimeState(year: number, quarter: QuarterKey, today: string): QuarterTimeState {
  if (today < quarterStartDate(year, quarter)) return 'FUTURE';
  if (today > quarterEndDate(year, quarter)) return 'PAST';
  return 'CURRENT';
}

export interface YearViewQuarterCell {
  readonly quarter: QuarterKey;
  readonly achieved: number;
  readonly target: number;
  /** `null` for a future quarter: nothing has run yet, cell shows "—". */
  readonly state: QuarterCellState | null;
}

export interface YearViewRow {
  readonly kpiId: string;
  readonly kpiCode: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly yearTarget: number;
  readonly cumulativeAchieved: number;
  readonly percentOfYear: number;
  readonly quarters: readonly YearViewQuarterCell[];
}

/**
 * Builds the E6 "year view" table rows: year target/cumulative achieved from the YEAR progress call, each
 * quarter's achieved/target plus a past/current/future state from the per-quarter progress calls.
 */
export function buildYearViewRows(
  yearKpis: readonly MyTargetProgressKpi[],
  quarterKpis: Readonly<Record<QuarterKey, readonly MyTargetProgressKpi[]>>,
  year: number,
  today: string,
): readonly YearViewRow[] {
  return yearKpis.map((yk) => {
    const quarters = QUARTER_KEYS.map((q): YearViewQuarterCell => {
      const found = quarterKpis[q].find((k) => k.kpiId === yk.kpiId);
      const timeState = quarterTimeState(year, q, today);
      const achieved = found?.progress.achieved ?? 0;
      const target = found?.progress.target ?? 0;
      const state: QuarterCellState | null =
        timeState === 'FUTURE'
          ? null
          : timeState === 'CURRENT'
            ? 'CURRENT'
            : achieved >= target
              ? 'ACHIEVED_PAST'
              : 'MISSED_PAST';
      return { quarter: q, achieved, target, state };
    });
    return {
      kpiId: yk.kpiId,
      kpiCode: yk.kpiCode,
      kpiName: yk.kpiName,
      unit: yk.unit,
      yearTarget: yk.progress.target,
      cumulativeAchieved: yk.progress.achieved,
      percentOfYear: yk.progress.achievedPercent,
      quarters,
    };
  });
}
