import { expand, Observable, reduce } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { EmployeeDetail } from '../../core/employee.models';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { PagedResult } from '../../core/paged-result.model';
import { TargetAssignment, TargetFeedbackStatus } from '../../core/target-assignment.models';

/** KPI id -> target value of one owner. */
export type KpiValues = ReadonlyMap<string, number>;

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

// --- Branch manager table ---------------------------------------------------------------------

export interface BranchEmployeeRow {
  employee: EmployeeDetail;
  cells: TargetCell[];
}

export interface BranchDepartmentRow {
  department: DepartmentSummary;
  memberCount: number;
  cells: TargetCell[];
  members: BranchEmployeeRow[];
}

export interface BranchTable {
  branchName: string;
  total: TargetCell[];
  departments: BranchDepartmentRow[];
}

export interface BranchTableInput {
  kpis: readonly KpiDefinition[];
  assignments: readonly TargetAssignment[];
  departments: readonly DepartmentSummary[];
  employees: readonly EmployeeDetail[];
  search: string;
  departmentId: string;
  position: string;
}

function cellOf(values: KpiValues | undefined, kpiId: string): TargetCell {
  return values?.get(kpiId) ?? 'NONE';
}

/** A department and every team beneath it. */
function subtreeIds(departments: readonly DepartmentSummary[], rootId: string): Set<string> {
  const ids = new Set([rootId]);
  let size = 0;
  while (ids.size !== size) {
    size = ids.size;
    for (const department of departments) {
      if (department.parentDepartmentId && ids.has(department.parentDepartmentId)) {
        ids.add(department.id);
      }
    }
  }
  return ids;
}

export function buildBranchTable(input: BranchTableInput): BranchTable {
  const branch = input.departments.find((d) => d.levelType === 'BRANCH');
  const deptValues = valuesByOwner(input.assignments, 'departmentId');
  const employeeValues = valuesByOwner(input.assignments, 'employeeId');
  const query = input.search.trim().toLowerCase();

  const departments = input.departments
    .filter((d) => branch && d.parentDepartmentId === branch.id)
    .filter((d) => !input.departmentId || d.id === input.departmentId)
    .map((department): BranchDepartmentRow => {
      const values = deptValues.get(department.id);
      const scope = subtreeIds(input.departments, department.id);
      const everyone = input.employees.filter(
        (e) => scope.has(e.departmentId) && e.status === 'ACTIVE',
      );
      const deptMatches = !query || includesText(department.name, query);
      const members = everyone
        .filter((e) => !input.position || e.jobPositionTitle === input.position)
        .filter(
          (e) =>
            deptMatches || includesText(e.fullName, query) || includesText(e.employeeCode, query),
        )
        .map((employee) => ({
          employee,
          cells: input.kpis.map((kpi): TargetCell => {
            const own = employeeValues.get(employee.id)?.get(kpi.id);
            return own ?? (values?.has(kpi.id) ? 'UNALLOCATED' : 'NONE');
          }),
        }));
      return {
        department,
        memberCount: everyone.length,
        cells: input.kpis.map((kpi) => cellOf(values, kpi.id)),
        members,
      };
    })
    .filter((row) => (!query && !input.position) || row.members.length > 0);

  return {
    branchName: branch?.name ?? '',
    total: input.kpis.map((kpi) => cellOf(branch ? deptValues.get(branch.id) : undefined, kpi.id)),
    departments,
  };
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
