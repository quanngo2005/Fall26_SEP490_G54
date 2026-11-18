import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import {
  SuggestedDailyTarget,
  TargetAssignment,
  TargetAssignmentFeedback,
  TargetBreakdownItem,
} from '../../core/target-assignment.models';

/** `ENDED` wins over feedback: a finished quarter needs nothing from the employee any more. */
export type QuarterStatus = 'ENDED' | 'PENDING' | 'CONFIRMED' | 'COMMENTED';
export type MonthPhase = 'ENDED' | 'CURRENT' | 'PLANNED';

export interface KpiColumn {
  id: string;
  code: string;
  name: string;
  unit: string;
}

/** Value per KPI id; null when the KPI has no figure for that row (not assigned, or not spread over the days). */
export type KpiValues = Readonly<Record<string, number | null>>;

export interface MonthRow {
  /** `YYYY-MM`. */
  key: string;
  start: string;
  end: string;
  workingDays: number;
  values: KpiValues;
  phase: MonthPhase;
}

export interface QuarterRow {
  periodId: string;
  name: string;
  startDate: string;
  endDate: string;
  workingDays: number;
  values: KpiValues;
  status: QuarterStatus;
  targets: TargetAssignment[];
  months: MonthRow[];
  /** Latest time one of the quarter's targets was set or changed by the manager. */
  assignedAt: string;
  /** Latest confirmation or comment, null while nothing was answered. */
  feedbackAt: string | null;
}

export interface YearView {
  year: number;
  kpis: KpiColumn[];
  quarters: QuarterRow[];
  totalWorkingDays: number;
  totalValues: KpiValues;
}

export interface YearInput {
  periods: readonly EvaluationPeriodOption[];
  targets: readonly TargetAssignment[];
  /** Planned split per quarter id. */
  daily: Readonly<Record<string, SuggestedDailyTarget>>;
  kpis: readonly KpiDefinition[];
  /** Business-day date `YYYY-MM-DD`. */
  today: string;
}

const QUARTER = 'QUARTER';

function maxOf(values: readonly string[]): string {
  return values.reduce((latest, value) => (value > latest ? value : latest), '');
}

function monthEnd(key: string): string {
  const [year, month] = key.split('-').map(Number);
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return `${key}-${String(last).padStart(2, '0')}`;
}

function phaseOf(start: string, end: string, today: string): MonthPhase {
  if (today > end) return 'ENDED';
  return today >= start ? 'CURRENT' : 'PLANNED';
}

function statusOf(
  quarter: EvaluationPeriodOption,
  targets: readonly TargetAssignment[],
  today: string,
): QuarterStatus {
  if (today > quarter.endDate) return 'ENDED';
  if (
    targets.some(
      (target) => target.feedbackStatus !== 'CONFIRMED' && target.feedbackStatus !== 'COMMENTED',
    )
  ) {
    return 'PENDING';
  }
  return targets.every((target) => target.feedbackStatus === 'CONFIRMED')
    ? 'CONFIRMED'
    : 'COMMENTED';
}

function monthRows(quarter: SuggestedDailyTarget | undefined, today: string): MonthRow[] {
  const rows = new Map<string, { workingDays: number; values: Record<string, number | null> }>();
  for (const item of quarter?.items ?? []) {
    for (const month of item.months) {
      const row = rows.get(month.month) ?? { workingDays: month.workingDays, values: {} };
      row.values[item.kpiDefinitionId] = month.value;
      rows.set(month.month, row);
    }
  }
  return [...rows.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, row]) => {
      const start = `${key}-01`;
      const end = monthEnd(key);
      return {
        key,
        start,
        end,
        workingDays: row.workingDays,
        values: row.values,
        phase: phaseOf(start, end, today),
      };
    });
}

/**
 * Builds the "My targets" table for one year from the employee's quarter targets. Only quarters the employee actually
 * has targets in are listed; the year shown is the one running today, or the latest one with targets.
 */
export function buildYearView(input: YearInput): YearView | null {
  const withTargets = input.periods
    .filter(
      (period) =>
        period.periodType === QUARTER &&
        input.targets.some((t) => t.evaluationPeriodId === period.id),
    )
    .sort((a, b) => a.startDate.localeCompare(b.startDate));
  if (withTargets.length === 0) return null;

  const running = withTargets.find(
    (period) => input.today >= period.startDate && input.today <= period.endDate,
  );
  const year = Number((running ?? withTargets[withTargets.length - 1]).startDate.slice(0, 4));
  const periods = withTargets.filter((period) => Number(period.startDate.slice(0, 4)) === year);

  const kpiIds = new Set(
    input.targets
      .filter((t) => periods.some((p) => p.id === t.evaluationPeriodId))
      .map((t) => t.kpiDefinitionId),
  );
  const kpis: KpiColumn[] = input.kpis
    .filter((kpi) => kpiIds.has(kpi.id))
    .map((kpi) => ({ id: kpi.id, code: kpi.code, name: kpi.name, unit: kpi.unit }))
    .sort((a, b) => a.code.localeCompare(b.code));

  /** A KPI is added up over the year only when every quarter spreads it over the days (a SUM KPI). */
  const additive = (kpiId: string) =>
    periods.every((period) => {
      const item = input.daily[period.id]?.items.find((i) => i.kpiDefinitionId === kpiId);
      return item === undefined || item.months.length > 0;
    });

  const quarters = periods.map((period): QuarterRow => {
    const targets = input.targets.filter((t) => t.evaluationPeriodId === period.id);
    const feedbackTimes = targets
      .map((t) => t.feedbackAt)
      .filter((time): time is string => time !== null);
    return {
      periodId: period.id,
      name: period.name,
      startDate: period.startDate,
      endDate: period.endDate,
      workingDays: input.daily[period.id]?.workingDays ?? 0,
      values: Object.fromEntries(targets.map((t) => [t.kpiDefinitionId, t.targetValue])),
      status: statusOf(period, targets, input.today),
      targets,
      months: monthRows(input.daily[period.id], input.today),
      assignedAt: maxOf(targets.map((t) => t.updatedAt)),
      feedbackAt: feedbackTimes.length > 0 ? maxOf(feedbackTimes) : null,
    };
  });

  const totalValues = Object.fromEntries(
    kpis.map((kpi) => {
      const parts = quarters.map((q) => q.values[kpi.id]).filter((v): v is number => v != null);
      const total =
        parts.length > 0 && additive(kpi.id) ? parts.reduce((sum, v) => sum + v, 0) : null;
      return [kpi.id, total];
    }),
  );

  return {
    year,
    kpis,
    quarters,
    totalWorkingDays: quarters.reduce((sum, q) => sum + q.workingDays, 0),
    totalValues,
  };
}

/** The quarter still waiting for the employee: the latest one that is not finished and has an unanswered target. */
export function pendingQuarter(view: YearView): QuarterRow | null {
  return [...view.quarters].reverse().find((quarter) => quarter.status === 'PENDING') ?? null;
}

/**
 * True when the manager changed a target after the employee had confirmed it: the target is unanswered again but
 * its history still holds an earlier confirmation.
 */
export function changedAfterConfirm(
  quarter: QuarterRow,
  history: Readonly<Record<string, readonly TargetAssignmentFeedback[]>>,
): boolean {
  return quarter.targets.some(
    (target) =>
      target.feedbackStatus !== 'CONFIRMED' &&
      (history[target.id] ?? []).some((entry) => entry.type === 'CONFIRM'),
  );
}

/** Comments the employee left on a quarter, newest first; one comment sent to every KPI target appears once. */
export function quarterComments(
  quarter: QuarterRow,
  history: Readonly<Record<string, readonly TargetAssignmentFeedback[]>>,
): TargetAssignmentFeedback[] {
  const all = quarter.targets
    .flatMap((target) => history[target.id] ?? [])
    .filter((entry) => entry.type === 'COMMENT')
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  return all.filter(
    (entry, index) =>
      all.findIndex(
        (other) =>
          other.comment === entry.comment &&
          Math.abs(Date.parse(other.createdAt) - Date.parse(entry.createdAt)) < 10_000,
      ) === index,
  );
}

/** A week or a day of one month for the "by week / day" panel. */
export interface PeriodSlice {
  start: string;
  end: string;
  values: KpiValues;
}

/** Joins the breakdown of each KPI target by bucket and keeps the buckets that start inside the month. */
export function slicesOfMonth(
  month: Pick<MonthRow, 'start' | 'end'>,
  breakdowns: readonly { kpiDefinitionId: string; items: readonly TargetBreakdownItem[] }[],
): PeriodSlice[] {
  const slices = new Map<
    string,
    { start: string; end: string; values: Record<string, number | null> }
  >();
  for (const breakdown of breakdowns) {
    for (const item of breakdown.items) {
      if (item.start < month.start || item.start > month.end) continue;
      const slice = slices.get(item.start) ?? { start: item.start, end: item.end, values: {} };
      slice.values[breakdown.kpiDefinitionId] = item.value;
      slices.set(item.start, slice);
    }
  }
  return [...slices.values()].sort((a, b) => a.start.localeCompare(b.start));
}
