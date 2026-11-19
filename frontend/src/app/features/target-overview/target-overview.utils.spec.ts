import { EmployeeDetail } from '../../core/employee.models';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { QuarterSplitRow, TargetAssignment } from '../../core/target-assignment.models';
import {
  availableYears,
  buildBranchQuarterRows,
  buildDepartmentQuarterRows,
  buildFeedbackRows,
  buildQuotaRows,
  columnFormat,
  employeeFeedback,
  findQuarterPeriod,
  groupKpiRows,
  usedKpis,
  valuesByOwner,
} from './target-overview.utils';

const kpi = (id: string, aggregationType = 'SUM', kpiGroupName = 'OPERATIONS'): KpiDefinition =>
  ({
    id,
    code: id.toUpperCase(),
    name: id,
    unit: 'VND',
    aggregationType,
    kpiGroupName,
  }) as KpiDefinition;
const period = (type: string, startDate: string): EvaluationPeriodOption =>
  ({ id: `${type}-${startDate}`, periodType: type, startDate }) as EvaluationPeriodOption;
const assignment = (over: Partial<TargetAssignment>): TargetAssignment => ({
  id: 'a',
  kpiDefinitionId: 'k1',
  evaluationPeriodId: 'p',
  departmentId: null,
  employeeId: null,
  parentAssignmentId: null,
  targetValue: 0,
  status: 'ACTIVE',
  updatedAt: '2026-10-01T00:00:00+00:00',
  feedbackStatus: null,
  feedbackAt: null,
  ...over,
});
const employee = (id: string, departmentId: string, position = 'Teller'): EmployeeDetail =>
  ({
    id,
    fullName: id,
    employeeCode: id.toUpperCase(),
    departmentId,
    jobPositionTitle: position,
    status: 'ACTIVE',
  }) as EmployeeDetail;
const quarterSplitRow = (over: Partial<QuarterSplitRow> = {}): QuarterSplitRow => ({
  kpiId: 'k1',
  kpiCode: 'KPI_K1',
  kpiName: 'k1',
  unit: 'VND',
  groupCode: 'OPS',
  groupName: 'OPERATIONS',
  aggregationType: 'SUM',
  isRatio: false,
  yearValue: 100,
  q1: 25,
  q2: 25,
  q3: 25,
  q4: 25,
  previousYearValue: 80,
  previousYear: { q1: 20, q2: 20, q3: 20, q4: 20 },
  previousYearActual: null,
  sum: 100,
  diff: 0,
  matched: true,
  ...over,
});

describe('target overview utils', () => {
  it('finds years and quarter periods from the period list', () => {
    const periods = [
      period('YEAR', '2026-01-01'),
      period('YEAR', '2025-01-01'),
      period('QUARTER', '2026-10-01'),
    ];

    expect(availableYears(periods)).toEqual([2026, 2025]);
    expect(findQuarterPeriod(periods, 2026, 4)?.id).toBe('QUARTER-2026-10-01');
    expect(findQuarterPeriod(periods, 2026, 1)).toBeUndefined();
  });

  it('ignores superseded rows and keeps the KPIs that have targets', () => {
    const rows = [
      assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 5 }),
      assignment({
        departmentId: 'd1',
        kpiDefinitionId: 'k2',
        targetValue: 9,
        status: 'SUPERSEDED',
      }),
    ];

    expect(valuesByOwner(rows, 'departmentId').get('d1')?.get('k2')).toBeUndefined();
    expect(usedKpis([kpi('k1'), kpi('k3')], rows).map((k) => k.id)).toEqual(['k1']);
  });

  it('ranks a comment above not viewed above confirmed', () => {
    const row = (feedbackStatus: TargetAssignment['feedbackStatus']) =>
      assignment({ feedbackStatus });

    expect(employeeFeedback([row('CONFIRMED'), row('COMMENTED')])).toBe('COMMENTED');
    expect(employeeFeedback([row('CONFIRMED'), row('NOT_VIEWED')])).toBe('NOT_VIEWED');
    expect(employeeFeedback([row('CONFIRMED')])).toBe('CONFIRMED');
    expect(employeeFeedback([])).toBe('NOT_VIEWED');
  });

  describe('buildBranchQuarterRows', () => {
    it('reports matched, short and not-split SUM rows, and a fixed AVG row', () => {
      const rows = buildBranchQuarterRows([
        quarterSplitRow({ kpiId: 'matched' }),
        quarterSplitRow({
          kpiId: 'short',
          yearValue: 100,
          q1: 10,
          q2: 10,
          q3: 10,
          q4: 30,
          sum: 60,
          diff: 40,
          matched: false,
        }),
        quarterSplitRow({
          kpiId: 'notSplit',
          q1: null,
          q2: null,
          q3: null,
          q4: null,
          matched: false,
        }),
        quarterSplitRow({
          kpiId: 'avg',
          aggregationType: 'AVG',
          isRatio: true,
          yearValue: 4.5,
          previousYearValue: null,
        }),
      ]);

      expect(rows.map((r) => [r.kpiId, r.state, r.diff])).toEqual([
        ['matched', 'MATCHED', 0],
        ['short', 'SHORT', 40],
        ['notSplit', 'NOT_SPLIT', 0],
        ['avg', 'FIXED', 0],
      ]);
      expect(rows[0].changePercent).toBe(25);
      expect(rows[3].quarters).toEqual([4.5, 4.5, 4.5, 4.5]);
      expect(rows[3].changePercent).toBeNull();
      expect(rows[2].quarters).toEqual(['NONE', 'NONE', 'NONE', 'NONE']);
    });

    it('reports an over-split row as OVER with the absolute diff', () => {
      const rows = buildBranchQuarterRows([
        quarterSplitRow({
          yearValue: 100,
          q1: 40,
          q2: 40,
          q3: 40,
          q4: 40,
          sum: 160,
          diff: -60,
          matched: false,
        }),
      ]);

      expect(rows[0]).toEqual(jasmine.objectContaining({ state: 'OVER', diff: 60 }));
    });
  });

  describe('buildDepartmentQuarterRows', () => {
    const kpis = [kpi('k1'), kpi('k2', 'AVG', 'SALES')];
    const yearRows = [
      assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 120 }),
      assignment({ departmentId: 'd1', kpiDefinitionId: 'k2', targetValue: 4.5 }),
    ];
    const quarterRows = [
      [assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 30 })],
      [],
      [],
      [assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 50 })],
    ];
    const previousYearRows = [
      assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 100 }),
    ];

    it('builds one row per KPI the department has a year target for, ordered by group then code', () => {
      const rows = buildDepartmentQuarterRows(kpis, 'd1', yearRows, quarterRows, previousYearRows);

      expect(rows.map((r) => r.kpiId)).toEqual(['k1', 'k2']);
      const k1 = rows.find((r) => r.kpiId === 'k1')!;
      expect(k1.state).toBe('SHORT');
      expect(k1.diff).toBe(40);
      expect(k1.quarters).toEqual([30, 'NONE', 'NONE', 50]);
      expect(k1.changePercent).toBe(20);
      const k2 = rows.find((r) => r.kpiId === 'k2')!;
      expect(k2.state).toBe('FIXED');
      expect(k2.quarters).toEqual([4.5, 4.5, 4.5, 4.5]);
      expect(k2.changePercent).toBeNull();
    });

    it('ignores superseded rows and a KPI without a department-owned year target', () => {
      const superseded = [
        assignment({
          departmentId: 'd1',
          kpiDefinitionId: 'k1',
          targetValue: 999,
          status: 'SUPERSEDED',
        }),
        ...yearRows,
      ];
      expect(
        buildDepartmentQuarterRows(kpis, 'd1', superseded, quarterRows, previousYearRows).find(
          (r) => r.kpiId === 'k1',
        )!.year,
      ).toBe(120);
      expect(buildDepartmentQuarterRows([kpi('k3')], 'd1', yearRows, quarterRows, [])).toEqual([]);
    });
  });

  it('groups consecutive KPI rows by their KPI group', () => {
    const rows = buildBranchQuarterRows([
      quarterSplitRow({ kpiId: 'a', groupName: 'OPS' }),
      quarterSplitRow({ kpiId: 'b', groupName: 'OPS' }),
      quarterSplitRow({ kpiId: 'c', groupName: 'SALES' }),
    ]);

    expect(groupKpiRows(rows).map((g) => [g.groupName, g.rows.map((r) => r.kpiId)])).toEqual([
      ['OPS', ['a', 'b']],
      ['SALES', ['c']],
    ]);
  });

  it('builds the department quota and employee feedback rows', () => {
    const yearRows = [assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 120 })];
    const q1 = [assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 30 })];
    const q4 = [
      assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 40 }),
      assignment({
        id: 'm1',
        employeeId: 'e1',
        kpiDefinitionId: 'k1',
        targetValue: 15,
        feedbackStatus: 'COMMENTED',
        feedbackAt: '2026-10-22T08:00:00+00:00',
      }),
    ];

    const quota = buildQuotaRows([kpi('k1')], 'd1', yearRows, [q1, [], [], q4], q4);
    expect(
      buildQuotaRows([kpi('k1', 'AVG')], 'd1', yearRows, [q1, [], [], q4], q4)[0].remaining,
    ).toBeNull();
    const feedback = buildFeedbackRows(quota, [employee('e1', 'd1'), employee('e2', 'd1')], q4);

    expect(quota[0]).toEqual(
      jasmine.objectContaining({
        year: 120,
        quarters: [30, null, null, 40],
        allocated: 15,
        assigned: 40,
        remaining: 25,
      }),
    );
    expect(feedback.map((r) => [r.cells, r.feedback, r.feedbackAt])).toEqual([
      [[15], 'COMMENTED', '2026-10-22T08:00:00+00:00'],
      [['UNALLOCATED'], 'NOT_VIEWED', null],
    ]);
  });

  it('shows money in tỷ or triệu VND and fraction percentages ×100', () => {
    const labels = { billionVnd: 'tỷ VND', millionVnd: 'triệu VND' };
    expect(columnFormat('VND', [160_000_000_000, 8_000_000_000, 'NONE'], labels)).toEqual({
      divisor: 1_000_000_000,
      unit: 'tỷ VND',
    });
    expect(columnFormat('VND', [160_000_000_000, 500_000_000, 'UNALLOCATED'], labels)).toEqual({
      divisor: 1_000_000,
      unit: 'triệu VND',
    });
    expect(columnFormat('VND', [900_000], labels)).toEqual({ divisor: 1, unit: 'VND' });
    expect(columnFormat('%', [0.4, 0.35], labels)).toEqual({ divisor: 0.01, unit: '%' });
    expect(columnFormat('%', [40], labels)).toEqual({ divisor: 1, unit: '%' });
    expect(columnFormat('KH', [2000], labels)).toEqual({ divisor: 1, unit: 'KH' });
  });
});
