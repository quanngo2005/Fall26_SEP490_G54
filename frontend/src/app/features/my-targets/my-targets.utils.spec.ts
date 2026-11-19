import { MyTargetKpi, MyTargetProgressKpi, MyTargetYear } from './my-targets.models';
import {
  bannerFor,
  buildYearViewRows,
  deltaVsPreviousYearPercent,
  filterProgress,
  groupKpis,
  isIncreasedOver15,
  isNewKpi,
  progressCardCounts,
  sortByPaceGap,
  summaryCards,
  yearElapsedPercent,
} from './my-targets.utils';

function kpi(overrides: Partial<MyTargetKpi> = {}): MyTargetKpi {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI1',
    kpiName: 'KPI 1',
    unit: 'tr VND',
    aggregationType: 'SUM',
    year: 1200,
    q1: 300,
    q2: 300,
    q3: 300,
    q4: 300,
    previousYearTarget: 1000,
    previousYearActual: 950,
    changed: false,
    changedOldValue: null,
    changedNewValue: null,
    hasFeedback: false,
    ...overrides,
  };
}

function year(overrides: Partial<MyTargetYear> = {}): MyTargetYear {
  return {
    year: 2027,
    employeeId: 'e1',
    employeeName: 'A',
    departmentId: 'd1',
    departmentName: 'Phòng A',
    ackStatus: 'PENDING',
    confirmedAt: null,
    kpis: [kpi()],
    ...overrides,
  };
}

function progressKpi(overrides: Partial<MyTargetProgressKpi> = {}): MyTargetProgressKpi {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI1',
    kpiName: 'KPI 1',
    unit: 'tr VND',
    progress: {
      target: 100,
      achieved: 50,
      inReviewCount: 0,
      achievedPercent: 50,
      expectedPacePercent: 50,
      paceStatus: 'ON_PACE',
    },
    ...overrides,
  };
}

describe('my-targets.utils', () => {
  it('computes the delta vs. last year and flags a >15% increase', () => {
    expect(deltaVsPreviousYearPercent(kpi({ year: 1200, previousYearTarget: 1000 }))).toBeCloseTo(
      20,
    );
    expect(deltaVsPreviousYearPercent(kpi({ previousYearTarget: null }))).toBeNull();
    expect(isIncreasedOver15(kpi({ year: 1200, previousYearTarget: 1000 }))).toBeTrue();
    expect(isIncreasedOver15(kpi({ year: 1100, previousYearTarget: 1000 }))).toBeFalse();
  });

  it('treats a KPI with no previous-year target as new', () => {
    expect(isNewKpi(kpi({ previousYearTarget: null }))).toBeTrue();
    expect(isNewKpi(kpi({ previousYearTarget: 1000 }))).toBeFalse();
  });

  it('builds the summary cards from the KPI list', () => {
    const data = year({
      kpis: [
        kpi({ kpiId: 'k1', year: 1200, previousYearTarget: 1000, hasFeedback: true }),
        kpi({ kpiId: 'k2', year: 100, previousYearTarget: null }),
      ],
    });
    const s = summaryCards(data);
    expect(s.kpiCount).toBe(2);
    expect(s.increasedCount).toBe(1);
    expect(s.newCount).toBe(1);
    expect(s.feedbackCount).toBe(1);
    expect(s.avgDeltaPercent).toBeCloseTo(20);
  });

  it('picks the banner kind from the ack status', () => {
    expect(bannerFor(year({ ackStatus: 'PENDING' })).kind).toBe('PENDING');
    expect(bannerFor(year({ ackStatus: 'RECONFIRM' })).kind).toBe('RECONFIRM');
    expect(bannerFor(year({ ackStatus: 'CONFIRMED' })).kind).toBe('CONFIRMED');
  });

  it('groups KPIs by the shared 7-group catalog order', () => {
    const data = [kpi({ kpiId: 'k1' }), kpi({ kpiId: 'k2' }), kpi({ kpiId: 'k3' })];
    const groupNames = new Map([
      ['k1', 'Tín dụng'],
      ['k2', 'Huy động vốn'],
      ['k3', 'Huy động vốn'],
    ]);
    const groups = groupKpis(data, groupNames);
    expect(groups.map((g) => g.name)).toEqual(['Huy động vốn', 'Tín dụng']);
    expect(groups[0].kpis.length).toBe(2);
  });

  it('buckets quarter progress into needs-attention / all / on-pace', () => {
    const kpis = [
      progressKpi({ kpiId: 'slow', progress: { ...progressKpi().progress, paceStatus: 'SLOW' } }),
      progressKpi({
        kpiId: 'risk',
        progress: { ...progressKpi().progress, paceStatus: 'AT_RISK' },
      }),
      progressKpi({ kpiId: 'ok', progress: { ...progressKpi().progress, paceStatus: 'ON_PACE' } }),
    ];
    expect(filterProgress(kpis, 'NEEDS_ATTENTION').map((k) => k.kpiId)).toEqual(['slow', 'risk']);
    expect(filterProgress(kpis, 'ON_PACE').map((k) => k.kpiId)).toEqual(['ok']);
    expect(filterProgress(kpis, 'ALL').length).toBe(3);

    const counts = progressCardCounts(kpis);
    expect(counts.slow).toBe(1);
    expect(counts.atRisk).toBe(1);
    expect(counts.onPace).toBe(1);
  });

  it('sorts progress rows by pace gap, worst first', () => {
    const kpis = [
      progressKpi({
        kpiId: 'a',
        progress: { ...progressKpi().progress, achievedPercent: 40, expectedPacePercent: 50 },
      }),
      progressKpi({
        kpiId: 'b',
        progress: { ...progressKpi().progress, achievedPercent: 20, expectedPacePercent: 50 },
      }),
    ];
    expect(sortByPaceGap(kpis).map((k) => k.kpiId)).toEqual(['b', 'a']);
  });

  it('computes the % of the year elapsed', () => {
    expect(yearElapsedPercent('2027-01-01', 2027)).toBeCloseTo(0, 0);
    expect(yearElapsedPercent('2027-12-31', 2027)).toBeCloseTo(100, 0);
    expect(yearElapsedPercent('2027-07-02', 2027)).toBeGreaterThan(49);
  });

  it('builds the year-view rows with past/current/future quarter cells', () => {
    const yearKpis = [
      progressKpi({ progress: { ...progressKpi().progress, target: 400, achieved: 180 } }),
    ];
    const q1 = [
      progressKpi({ progress: { ...progressKpi().progress, target: 100, achieved: 100 } }),
    ];
    const q2 = [
      progressKpi({ progress: { ...progressKpi().progress, target: 100, achieved: 80 } }),
    ];
    const q3: typeof q1 = [];
    const q4: typeof q1 = [];
    const rows = buildYearViewRows(
      yearKpis,
      { Q1: q1, Q2: q2, Q3: q3, Q4: q4 },
      2027,
      '2027-05-15',
    );

    expect(rows[0].quarters[0].state).toBe('ACHIEVED_PAST');
    expect(rows[0].quarters[1].state).toBe('CURRENT');
    expect(rows[0].quarters[2].state).toBeNull();
  });
});
