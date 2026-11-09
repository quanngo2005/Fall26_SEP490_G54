import { DepartmentSummary } from '../../core/department.models';
import { EmployeeDetail } from '../../core/employee.models';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { TargetAssignment } from '../../core/target-assignment.models';
import {
  availableYears,
  buildBranchTable,
  buildFeedbackRows,
  buildQuotaRows,
  employeeFeedback,
  findQuarterPeriod,
  usedKpis,
  valuesByOwner,
} from './target-overview.utils';

const kpi = (id: string, aggregationType = 'SUM'): KpiDefinition =>
  ({ id, name: id, unit: 'VND', aggregationType }) as KpiDefinition;
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
const department = (id: string, parent: string | null, levelType: string): DepartmentSummary =>
  ({ id, name: id, parentDepartmentId: parent, levelType }) as DepartmentSummary;
const employee = (id: string, departmentId: string, position = 'Teller'): EmployeeDetail =>
  ({
    id,
    fullName: id,
    employeeCode: id.toUpperCase(),
    departmentId,
    jobPositionTitle: position,
    status: 'ACTIVE',
  }) as EmployeeDetail;

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

  describe('buildBranchTable', () => {
    const base = {
      kpis: [kpi('k1'), kpi('k2')],
      assignments: [
        assignment({ departmentId: 'b', kpiDefinitionId: 'k1', targetValue: 100 }),
        assignment({ departmentId: 'd1', kpiDefinitionId: 'k1', targetValue: 60 }),
        assignment({ employeeId: 'e1', kpiDefinitionId: 'k1', targetValue: 25 }),
      ],
      departments: [department('b', null, 'BRANCH'), department('d1', 'b', 'DEPARTMENT')],
      employees: [employee('e1', 'd1'), employee('e2', 'd1', 'Sales Officer')],
      search: '',
      departmentId: '',
      position: '',
    };

    it('shows totals, department values and marks employees without a target', () => {
      const table = buildBranchTable(base);

      expect(table.total).toEqual([100, 'NONE']);
      expect(table.departments[0].cells).toEqual([60, 'NONE']);
      expect(table.departments[0].members.map((m) => m.cells)).toEqual([
        [25, 'NONE'],
        ['UNALLOCATED', 'NONE'],
      ]);
    });

    it('filters by position and drops departments without a matching member', () => {
      expect(
        buildBranchTable({ ...base, position: 'Sales Officer' }).departments[0].members.map(
          (m) => m.employee.id,
        ),
      ).toEqual(['e2']);
      expect(buildBranchTable({ ...base, position: 'Manager' }).departments).toEqual([]);
    });

    it('keeps every member when the search matches the department name', () => {
      expect(buildBranchTable({ ...base, search: 'd1' }).departments[0].members.length).toBe(2);
      expect(buildBranchTable({ ...base, search: 'e2' }).departments[0].members.length).toBe(1);
    });
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
});
