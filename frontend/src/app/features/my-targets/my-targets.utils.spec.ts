import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { KpiDefinition } from '../../core/kpi-definition.models';
import {
  SuggestedDailyTarget,
  TargetAssignment,
  TargetAssignmentFeedback,
} from '../../core/target-assignment.models';
import {
  YearInput,
  buildYearView,
  changedAfterConfirm,
  pendingQuarter,
  quarterComments,
  slicesOfMonth,
} from './my-targets.utils';

const period = (id: string, name: string, start: string, end: string, type = 'QUARTER') =>
  ({
    id,
    code: id,
    name,
    periodType: type,
    startDate: start,
    endDate: end,
    status: 'OPEN',
    isActive: true,
  }) as EvaluationPeriodOption;

const kpi = (id: string, code: string) =>
  ({ id, code, name: `KPI ${code}`, unit: 'VND' }) as KpiDefinition;

const target = (
  id: string,
  periodId: string,
  kpiId: string,
  value: number,
  extra: Partial<TargetAssignment> = {},
): TargetAssignment => ({
  id,
  kpiDefinitionId: kpiId,
  evaluationPeriodId: periodId,
  departmentId: null,
  employeeId: 'e1',
  parentAssignmentId: null,
  targetValue: value,
  status: 'ACTIVE',
  updatedAt: '2026-10-20T09:15:02+07:00',
  feedbackStatus: 'NOT_VIEWED',
  feedbackAt: null,
  ...extra,
});

const daily = (
  periodId: string,
  workingDays: number,
  items: SuggestedDailyTarget['items'],
): SuggestedDailyTarget => ({
  employeeId: 'e1',
  evaluationPeriodId: periodId,
  periodStart: '2026-10-01',
  periodEnd: '2026-12-31',
  workingDays,
  items,
});

const spreadItem = (id: string, kpiId: string, months: [string, number, number][]) => ({
  targetAssignmentId: id,
  kpiDefinitionId: kpiId,
  kpiCode: kpiId,
  unit: 'VND',
  isWholeNumber: false,
  targetValue: months.reduce((sum, m) => sum + m[2], 0),
  plannedDaily: 1,
  plannedWeekly: 5,
  months: months.map(([month, workingDays, value]) => ({
    month,
    workingDays,
    value,
    daily: 1,
    weekly: 5,
  })),
});

const PERIODS = [
  period('q3', 'Quý 3/2026', '2026-07-01', '2026-09-30'),
  period('q4', 'Quý 4/2026', '2026-10-01', '2026-12-31'),
  period('m10', 'Tháng 10/2026', '2026-10-01', '2026-10-31', 'MONTH'),
  period('q1-25', 'Quý 1/2025', '2025-01-01', '2025-03-31'),
];

const KPIS = [kpi('k2', 'THE'), kpi('k1', 'HDV')];

describe('my-targets utils', () => {
  describe('buildYearView', () => {
    const base = (): YearInput => ({
      periods: PERIODS,
      targets: [
        target('t1', 'q4', 'k1', 6_400_000_000),
        target('t2', 'q4', 'k2', 50),
        target('t3', 'q3', 'k1', 5_600_000_000, {
          feedbackStatus: 'CONFIRMED',
          feedbackAt: '2026-07-02T08:00:00+07:00',
        }),
        target('x', 'm10', 'k1', 1),
      ],
      daily: {
        q4: daily('q4', 64, [
          spreadItem('t1', 'k1', [
            ['2026-10', 22, 2.2],
            ['2026-11', 21, 2.1],
            ['2026-12', 21, 2.1],
          ]),
          spreadItem('t2', 'k2', [
            ['2026-10', 22, 17],
            ['2026-11', 21, 17],
            ['2026-12', 21, 16],
          ]),
        ]),
        q3: daily('q3', 65, [
          spreadItem('t3', 'k1', [
            ['2026-07', 23, 2],
            ['2026-08', 21, 2],
            ['2026-09', 21, 1.6],
          ]),
        ]),
      },
      kpis: KPIS,
      today: '2026-10-28',
    });

    it('lists only the quarters the employee has targets in, in date order, for the running year', () => {
      const view = buildYearView(base())!;

      expect(view.year).toBe(2026);
      expect(view.quarters.map((q) => q.periodId)).toEqual(['q3', 'q4']);
    });

    it('uses the KPIs of the targets as columns, ordered by code', () => {
      const view = buildYearView(base())!;

      expect(view.kpis.map((k) => k.code)).toEqual(['HDV', 'THE']);
    });

    it('takes the quarter value and working days from the target and the planned split', () => {
      const q4 = buildYearView(base())!.quarters[1];

      expect(q4.workingDays).toBe(64);
      expect(q4.values).toEqual({ k1: 6_400_000_000, k2: 50 });
      expect(q4.assignedAt).toBe('2026-10-20T09:15:02+07:00');
    });

    it('builds the month rows from the planned split and marks which month is running', () => {
      const months = buildYearView(base())!.quarters[1].months;

      expect(months.map((m) => [m.key, m.workingDays, m.phase])).toEqual([
        ['2026-10', 22, 'CURRENT'],
        ['2026-11', 21, 'PLANNED'],
        ['2026-12', 21, 'PLANNED'],
      ]);
      expect(months[0].values).toEqual({ k1: 2.2, k2: 17 });
      expect(months[0].end).toBe('2026-10-31');
    });

    it('sets the status: finished quarters first, then unanswered, confirmed, commented', () => {
      const view = buildYearView(base())!;
      expect(view.quarters.map((q) => q.status)).toEqual(['ENDED', 'PENDING']);

      const confirmed = buildYearView({
        ...base(),
        targets: base().targets.map((t) => ({
          ...t,
          feedbackStatus: 'CONFIRMED' as const,
          feedbackAt: '2026-10-21T14:02:11+07:00',
        })),
      })!;
      expect(confirmed.quarters[1].status).toBe('CONFIRMED');
      expect(confirmed.quarters[1].feedbackAt).toBe('2026-10-21T14:02:11+07:00');

      const commented = buildYearView({
        ...base(),
        targets: base().targets.map((t) => ({
          ...t,
          feedbackStatus: 'COMMENTED' as const,
          feedbackAt: '2026-10-21T14:02:11+07:00',
        })),
      })!;
      expect(commented.quarters[1].status).toBe('COMMENTED');
    });

    it('treats a quarter as pending while any one of its targets is unanswered', () => {
      const input = base();
      const targets = input.targets.map((t, i) =>
        i === 0 ? { ...t, feedbackStatus: 'CONFIRMED' as const } : t,
      );

      expect(buildYearView({ ...input, targets })!.quarters[1].status).toBe('PENDING');
    });

    it('adds the year up only for KPIs every quarter spreads over the days', () => {
      const input = base();
      const q3 = daily('q3', 65, [{ ...spreadItem('t3', 'k1', []), months: [] }]);

      const view = buildYearView({ ...input, daily: { ...input.daily, q3 } })!;

      expect(view.totalWorkingDays).toBe(129);
      expect(view.totalValues['k1']).toBeNull();
      expect(view.totalValues['k2']).toBe(50);
    });

    it('sums a spread KPI over the year', () => {
      const view = buildYearView(base())!;

      expect(view.totalValues['k1']).toBe(12_000_000_000);
    });

    it('shows the latest year when none is running, and nothing without quarter targets', () => {
      const old = buildYearView({ ...base(), today: '2027-02-01' })!;
      expect(old.year).toBe(2026);

      expect(buildYearView({ ...base(), targets: [target('x', 'm10', 'k1', 1)] })).toBeNull();
      expect(buildYearView({ ...base(), periods: [] })).toBeNull();
    });

    it('copes with a quarter whose planned split could not be loaded', () => {
      const q4 = buildYearView({ ...base(), daily: {} })!.quarters[1];

      expect(q4.workingDays).toBe(0);
      expect(q4.months).toEqual([]);
    });
  });

  describe('pendingQuarter and changedAfterConfirm', () => {
    const view = () =>
      buildYearView({
        periods: PERIODS,
        targets: [target('t1', 'q4', 'k1', 10)],
        daily: {},
        kpis: KPIS,
        today: '2026-10-28',
      })!;

    const entry = (
      type: 'CONFIRM' | 'COMMENT',
      createdAt: string,
      comment: string | null = null,
    ): TargetAssignmentFeedback => ({
      id: `${type}-${createdAt}`,
      targetAssignmentId: 't1',
      employeeId: 'e1',
      type,
      comment,
      createdAt,
    });

    it('finds the quarter still waiting, none once it is answered', () => {
      expect(pendingQuarter(view())?.periodId).toBe('q4');

      const answered = buildYearView({
        periods: PERIODS,
        targets: [target('t1', 'q4', 'k1', 10, { feedbackStatus: 'CONFIRMED' })],
        daily: {},
        kpis: KPIS,
        today: '2026-10-28',
      })!;
      expect(pendingQuarter(answered)).toBeNull();
    });

    it('detects a change after a confirmation only when an older confirmation exists', () => {
      const q4 = pendingQuarter(view())!;

      expect(changedAfterConfirm(q4, {})).toBeFalse();
      expect(
        changedAfterConfirm(q4, { t1: [entry('COMMENT', '2026-10-21T08:00:00Z', 'x')] }),
      ).toBeFalse();
      expect(
        changedAfterConfirm(q4, { t1: [entry('CONFIRM', '2026-10-21T08:00:00Z')] }),
      ).toBeTrue();
    });

    it('lists comments newest first and shows a comment sent to every KPI once', () => {
      const q4 = buildYearView({
        periods: PERIODS,
        targets: [target('t1', 'q4', 'k1', 10), target('t2', 'q4', 'k2', 5)],
        daily: {},
        kpis: KPIS,
        today: '2026-10-28',
      })!.quarters[0];

      const comments = quarterComments(q4, {
        t1: [
          entry('COMMENT', '2026-10-22T08:00:00.000Z', 'Số liệu chưa khớp'),
          entry('COMMENT', '2026-10-21T08:00:00.000Z', 'Trước đó'),
        ],
        t2: [
          entry('COMMENT', '2026-10-22T08:00:00.400Z', 'Số liệu chưa khớp'),
          entry('CONFIRM', '2026-10-20T08:00:00.000Z'),
        ],
      });

      expect(comments.map((c) => c.comment)).toEqual(['Số liệu chưa khớp', 'Trước đó']);
    });
  });

  describe('slicesOfMonth', () => {
    it('joins the KPIs by bucket and keeps the buckets that start inside the month', () => {
      const slices = slicesOfMonth({ start: '2026-11-01', end: '2026-11-30' }, [
        {
          kpiDefinitionId: 'k1',
          items: [
            { start: '2026-10-26', end: '2026-10-30', workingDays: 5, value: 9 },
            { start: '2026-11-02', end: '2026-11-06', workingDays: 5, value: 10 },
            { start: '2026-11-09', end: '2026-11-13', workingDays: 5, value: 11 },
          ],
        },
        {
          kpiDefinitionId: 'k2',
          items: [{ start: '2026-11-02', end: '2026-11-06', workingDays: 5, value: 3 }],
        },
      ]);

      expect(slices).toEqual([
        { start: '2026-11-02', end: '2026-11-06', values: { k1: 10, k2: 3 } },
        { start: '2026-11-09', end: '2026-11-13', values: { k1: 11 } },
      ]);
    });
  });
});
