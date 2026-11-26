import { expand, Observable, reduce } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { PagedResult } from '../../core/paged-result.model';
import {
  KpiAllocation,
  QuarterSplitRow,
  TargetAssignment,
  TargetFeedbackStatus,
} from '../../core/target-assignment.models';

/** `NONE` renders as "—" (KPI does not apply), `UNALLOCATED` as "not allocated yet". */
export type TargetCell = number | 'NONE' | 'UNALLOCATED';

const MAX_PAGE_SIZE = 100;

/** Loads every page of a paged endpoint (the API clamps the page size to 100). */
export function fetchAllPages<T>(
  load: (page: number, pageSize: number) => Observable<PagedResult<T>>,
): Observable<T[]> {
  return load(1, MAX_PAGE_SIZE).pipe(
    expand((result) =>
      result.page * result.pageSize < result.totalCount ? load(result.page + 1, MAX_PAGE_SIZE) : [],
    ),
    reduce((all, result) => [...all, ...result.items], [] as T[]),
  );
}

export function periodYear(period: EvaluationPeriodOption): number {
  return Number(period.startDate.slice(0, 4));
}

export function periodQuarter(period: EvaluationPeriodOption): number {
  return Math.floor((Number(period.startDate.slice(5, 7)) - 1) / 3) + 1;
}

export function availableYears(periods: readonly EvaluationPeriodOption[]): number[] {
  return [
    ...new Set(periods.filter((p) => p.periodType === 'YEAR').map((p) => periodYear(p))),
  ].sort((a, b) => b - a);
}

export function findYearPeriod(
  periods: readonly EvaluationPeriodOption[],
  year: number,
): EvaluationPeriodOption | undefined {
  return periods.find((p) => p.periodType === 'YEAR' && periodYear(p) === year);
}

export function findQuarterPeriod(
  periods: readonly EvaluationPeriodOption[],
  year: number,
  quarter: number,
): EvaluationPeriodOption | undefined {
  return periods.find(
    (p) => p.periodType === 'QUARTER' && periodYear(p) === year && periodQuarter(p) === quarter,
  );
}

/** Target values per owner, ignoring rows that were replaced or closed. */
export function valuesByOwner(
  assignments: readonly TargetAssignment[],
  owner: 'departmentId' | 'employeeId',
): Map<string, Map<string, number>> {
  const result = new Map<string, Map<string, number>>();
  for (const assignment of assignments) {
    const ownerId = assignment[owner];
    if (!ownerId || assignment.status === 'SUPERSEDED' || assignment.status === 'CLOSED') {
      continue;
    }
    const values = result.get(ownerId) ?? new Map<string, number>();
    values.set(assignment.kpiDefinitionId, assignment.targetValue);
    result.set(ownerId, values);
  }
  return result;
}

/** KPIs that have at least one assignment, in the order of the KPI list. */
export function usedKpis(
  kpis: readonly KpiDefinition[],
  assignments: readonly TargetAssignment[],
): KpiDefinition[] {
  const used = new Set(assignments.map((a) => a.kpiDefinitionId));
  return kpis.filter((kpi) => used.has(kpi.id));
}

/** One status per employee: a comment wins over "not viewed", which wins over "confirmed". */
export function employeeFeedback(rows: readonly TargetAssignment[]): TargetFeedbackStatus {
  const statuses = rows.map((r) => r.feedbackStatus);
  if (statuses.includes('COMMENTED')) {
    return 'COMMENTED';
  }
  return statuses.length > 0 && statuses.every((s) => s === 'CONFIRMED')
    ? 'CONFIRMED'
    : 'NOT_VIEWED';
}

export function includesText(value: string, query: string): boolean {
  return value.toLowerCase().includes(query);
}

/** How a KPI column is shown: cell value ÷ `divisor`, with `unit` in the header. */
export interface ColumnFormat {
  divisor: number;
  unit: string;
}

export interface ColumnUnitLabels {
  billionVnd: string;
  millionVnd: string;
}

const BILLION = 1_000_000_000;
const MILLION = 1_000_000;

/**
 * Money (unit VND) is shown in tỷ or triệu VND when every value in the column is at least that large; percentages
 * stored as fractions (all values within 0..1) are shown ×100, so 0.4 reads 40 %. Other units are shown as they are.
 */
export function columnFormat(
  unit: string,
  cells: readonly TargetCell[],
  labels: ColumnUnitLabels,
): ColumnFormat {
  const values = cells.filter((cell): cell is number => typeof cell === 'number' && cell !== 0);
  const smallest = values.length ? Math.min(...values.map(Math.abs)) : 0;
  const normalized = unit.trim().toUpperCase();
  if (normalized === 'VND' || normalized === 'VNĐ') {
    if (smallest >= BILLION) return { divisor: BILLION, unit: labels.billionVnd };
    if (smallest >= MILLION) return { divisor: MILLION, unit: labels.millionVnd };
  }
  if (normalized === '%' && values.every((value) => Math.abs(value) <= 1)) {
    return { divisor: 0.01, unit: '%' };
  }
  return { divisor: 1, unit };
}

// --- Branch manager KPI × quarter table (SCR-STRAT-03.1) ------------------------------------

export type KpiSplitState = 'MATCHED' | 'SHORT' | 'OVER' | 'NOT_SPLIT' | 'FIXED';

export interface KpiQuarterRow {
  kpiId: string;
  kpiCode: string;
  kpiName: string;
  unit: string;
  groupName: string;
  /** Non-SUM KPI: the year value applies to every quarter and the row cannot be split. */
  isRatio: boolean;
  year: number;
  /** [Q1, Q2, Q3, Q4]; `'NONE'` renders "—" for a quarter that has not been split yet. */
  quarters: TargetCell[];
  previousYear: number | null;
  /** `(year - previousYear) / previousYear × 100`; null with no previous-year value. */
  changePercent: number | null;
  state: KpiSplitState;
  /** `|year - sum(quarters)|` in the KPI's base unit; 0 unless `state` is `SHORT` or `OVER`. */
  diff: number;
}

export interface KpiGroupSection {
  groupName: string;
  rows: KpiQuarterRow[];
}

/** Percent change from `previous` to `current`, or null when there is nothing to compare against. */
export function yearOverYearChange(current: number | null, previous: number | null): number | null {
  return current === null || !previous ? null : ((current - previous) / previous) * 100;
}

/** How a KPI's quarters compare to its year value: fixed (non-SUM), not split, matched, short or over. */
export function quarterStatus(
  isRatio: boolean,
  quarters: readonly (number | null)[],
  matched: boolean,
  diff: number,
): { state: KpiSplitState; diff: number } {
  if (isRatio) {
    return { state: 'FIXED', diff: 0 };
  }
  if (quarters.every((q) => q === null)) {
    return { state: 'NOT_SPLIT', diff: 0 };
  }
  if (matched) {
    return { state: 'MATCHED', diff: 0 };
  }
  return { state: diff > 0 ? 'SHORT' : 'OVER', diff: Math.abs(diff) };
}

/** One row per KPI of the branch's quarter split, in the group/code order the API already returns. */
export function buildBranchQuarterRows(rows: readonly QuarterSplitRow[]): KpiQuarterRow[] {
  return rows.map((row): KpiQuarterRow => {
    const rawQuarters = [row.q1, row.q2, row.q3, row.q4];
    const { state, diff } = quarterStatus(row.isRatio, rawQuarters, row.matched, row.diff);
    const quarters: TargetCell[] = row.isRatio
      ? [row.yearValue, row.yearValue, row.yearValue, row.yearValue]
      : rawQuarters.map((q): TargetCell => q ?? 'NONE');
    return {
      kpiId: row.kpiId,
      kpiCode: row.kpiCode,
      kpiName: row.kpiName,
      unit: row.unit,
      groupName: row.groupName,
      isRatio: row.isRatio,
      year: row.yearValue,
      quarters,
      previousYear: row.previousYearValue,
      changePercent: yearOverYearChange(row.yearValue, row.previousYearValue),
      state,
      diff,
    };
  });
}

/** One row per KPI the department has a year target for, ordered by KPI group then code. */
export function buildDepartmentQuarterRows(
  kpis: readonly KpiDefinition[],
  departmentId: string,
  yearRows: readonly TargetAssignment[],
  quarterRows: readonly (readonly TargetAssignment[])[],
  previousYearRows: readonly TargetAssignment[],
): KpiQuarterRow[] {
  const yearValues = valuesByOwner(yearRows, 'departmentId').get(departmentId);
  if (!yearValues) {
    return [];
  }
  const quarterValues = quarterRows.map((rows) =>
    valuesByOwner(rows, 'departmentId').get(departmentId),
  );
  const previousValues = valuesByOwner(previousYearRows, 'departmentId').get(departmentId);

  const rows: KpiQuarterRow[] = [];
  for (const kpi of kpis) {
    const year = yearValues.get(kpi.id);
    if (year === undefined) {
      continue;
    }
    const isRatio = kpi.aggregationType !== 'SUM';
    const rawQuarters = quarterValues.map((values) => values?.get(kpi.id) ?? null);
    const sum = rawQuarters.reduce((total: number, q) => total + (q ?? 0), 0);
    const { state, diff } = quarterStatus(isRatio, rawQuarters, sum === year, year - sum);
    const quarters: TargetCell[] = isRatio
      ? [year, year, year, year]
      : rawQuarters.map((q): TargetCell => q ?? 'NONE');
    const previousYear = previousValues?.get(kpi.id) ?? null;
    rows.push({
      kpiId: kpi.id,
      kpiCode: kpi.code,
      kpiName: kpi.name,
      unit: kpi.unit,
      groupName: kpi.kpiGroupName,
      isRatio,
      year,
      quarters,
      previousYear,
      changePercent: yearOverYearChange(year, previousYear),
      state,
      diff,
    });
  }
  return rows.sort(
    (a, b) => a.groupName.localeCompare(b.groupName) || a.kpiCode.localeCompare(b.kpiCode),
  );
}

/** Groups consecutive rows that share a KPI group, for the "<groupName> · n KPI" header rows. */
export function groupKpiRows(rows: readonly KpiQuarterRow[]): KpiGroupSection[] {
  const sections: KpiGroupSection[] = [];
  for (const row of rows) {
    const last = sections[sections.length - 1];
    if (last && last.groupName === row.groupName) {
      last.rows.push(row);
    } else {
      sections.push({ groupName: row.groupName, rows: [row] });
    }
  }
  return sections;
}

// --- Department manager screen ----------------------------------------------------------------

export interface QuotaRow {
  kpi: KpiDefinition;
  year: number | null;
  quarters: (number | null)[];
  assigned: number | null;
  /** Sum handed to employees and what is left; null for KPIs that are not summed (ratings, averages). */
  allocated: number | null;
  remaining: number | null;
}

export interface FeedbackRow {
  employee: EmployeeDetail;
  cells: TargetCell[];
  feedback: TargetFeedbackStatus;
  /** Latest time of the answer behind `feedback`; null for not viewed. */
  feedbackAt: string | null;
  assignmentIds: string[];
}

/** Quota of the department per KPI for the year and each quarter, plus what was handed to employees. */
export function buildQuotaRows(
  kpis: readonly KpiDefinition[],
  departmentId: string,
  yearRows: readonly TargetAssignment[],
  quarterRows: readonly (readonly TargetAssignment[])[],
  selectedQuarter: readonly TargetAssignment[],
): QuotaRow[] {
  const ofDepartment = (rows: readonly TargetAssignment[]) =>
    valuesByOwner(rows, 'departmentId').get(departmentId);
  const year = ofDepartment(yearRows);
  const quarters = quarterRows.map(ofDepartment);
  const employeeValues = valuesByOwner(selectedQuarter, 'employeeId');

  return kpis.map((kpi) => {
    const assigned = ofDepartment(selectedQuarter)?.get(kpi.id) ?? null;
    const allocated =
      kpi.aggregationType === 'SUM'
        ? [...employeeValues.values()].reduce((sum, v) => sum + (v.get(kpi.id) ?? 0), 0)
        : null;
    return {
      kpi,
      year: year?.get(kpi.id) ?? null,
      quarters: quarters.map((q) => q?.get(kpi.id) ?? null),
      assigned,
      allocated,
      remaining: assigned !== null && allocated !== null ? assigned - allocated : null,
    };
  });
}

export function buildFeedbackRows(
  quota: readonly QuotaRow[],
  employees: readonly EmployeeDetail[],
  assignments: readonly TargetAssignment[],
): FeedbackRow[] {
  const current = assignments.filter((a) => a.status !== 'SUPERSEDED' && a.status !== 'CLOSED');
  const values = valuesByOwner(current, 'employeeId');
  return employees.map((employee) => {
    const owned = current.filter((a) => a.employeeId === employee.id);
    const feedback = employeeFeedback(owned);
    return {
      employee,
      cells: quota.map(
        (row): TargetCell =>
          values.get(employee.id)?.get(row.kpi.id) ??
          (row.assigned === null ? 'NONE' : 'UNALLOCATED'),
      ),
      feedback,
      feedbackAt: owned
        .filter((a) => a.feedbackStatus === feedback)
        .map((a) => a.feedbackAt)
        .reduce<string | null>(
          (latest, at) => (at && (!latest || at > latest) ? at : latest),
          null,
        ),
      assignmentIds: owned.map((a) => a.id),
    };
  });
}

// =========================================================================
// W2 heatmap (Theo phòng): department x KPI grid built from the bulk allocation
// endpoint (`GET /{branchYearId}/allocations`). The contract has no per-department
// "% chia xuống NV" or "% đạt so với nhịp" figure yet (no `deptYearId` on the Phase 2
// department row to reach tier-3 data, and no branch/department actual-progress
// endpoint) - see the W1/W2 gap note in the handback report. The one real signal the
// endpoint carries is each department's allocation match (`matched`), so the heatmap
// is colored by that instead of a percentage.
// =========================================================================

export type HeatmapCellState = 'MATCHED' | 'MISMATCH' | 'NOT_ELIGIBLE';

export interface HeatmapColumn {
  kpiId: string;
  kpiCode: string;
  kpiName: string;
  groupName: string;
}

export interface HeatmapCell {
  state: HeatmapCellState;
  year: number | null;
  diff: number | null;
  /** Set only when `state` is `NOT_ELIGIBLE` (e.g. no headcount for this KPI's work type). */
  reason: string | null;
}

export interface HeatmapRow {
  departmentId: string;
  departmentName: string;
  cells: HeatmapCell[];
  mismatchCount: number;
}

export interface HeatmapData {
  columns: HeatmapColumn[];
  rows: HeatmapRow[];
}

/** `departments` is the branch's direct child departments (the same list the view-by-department filter uses). */
export function buildAllocationHeatmap(
  allocations: readonly KpiAllocation[],
  kpiRows: readonly KpiQuarterRow[],
  departments: readonly { id: string; name: string }[],
): HeatmapData {
  const rowByKpiId = new Map(kpiRows.map((row) => [row.kpiId, row]));
  const columns: HeatmapColumn[] = allocations.map((allocation) => {
    const row = rowByKpiId.get(allocation.kpiId);
    return {
      kpiId: allocation.kpiId,
      kpiCode: row?.kpiCode ?? allocation.kpiName,
      kpiName: allocation.kpiName,
      groupName: row?.groupName ?? '',
    };
  });

  const rows: HeatmapRow[] = departments.map((department) => {
    const cells = allocations.map((allocation): HeatmapCell => {
      const deptRow = allocation.departments.find((d) => d.departmentId === department.id);
      if (deptRow) {
        return {
          state: deptRow.matched ? 'MATCHED' : 'MISMATCH',
          year: deptRow.year,
          diff: null,
          reason: null,
        };
      }
      const hidden = allocation.hiddenDepartments.find((d) => d.departmentId === department.id);
      return {
        state: 'NOT_ELIGIBLE',
        year: null,
        diff: null,
        reason: hidden?.reason ?? 'no_headcount',
      };
    });
    return {
      departmentId: department.id,
      departmentName: department.name,
      cells,
      mismatchCount: cells.filter((c) => c.state === 'MISMATCH').length,
    };
  });

  return { columns, rows };
}
