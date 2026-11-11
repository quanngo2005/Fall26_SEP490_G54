import { DepartmentMember, DepartmentOverview } from '../../core/department.models';
import {
  barWidth,
  formatArrow,
  formatSigned,
  formatValue,
  initials,
  NOT_AVAILABLE,
  toneOf,
} from './perf-display';

/** API-shaped test data (docs/api/DEPARTMENT_CONTRACT.md); the names are deliberately not the seed's. */
export function overviewFixture(patch: Partial<DepartmentOverview> = {}): DepartmentOverview {
  return {
    department: {
      id: 'dep-1',
      code: 'UNIT-A',
      name: 'Unit Alpha',
      description: null,
      levelType: 'DEPARTMENT',
      status: 'ACTIVE',
      parentDepartmentId: 'br-1',
      ancestors: [],
      manager: {
        id: 'emp-m',
        employeeCode: 'E-M',
        fullName: 'Manager Mia',
        jobPositionTitle: 'Head',
      },
      directEmployeeCount: 3,
      employeeCount: 3,
      childDepartmentCount: 0,
      lastSyncAt: '2026-10-01T00:00:00Z',
      updatedAt: '2026-10-01T00:00:00Z',
    },
    period: {
      id: 'p-cur',
      code: 'P-CUR',
      name: 'Period Current',
      periodType: 'MONTH',
      startDate: '2026-10-01',
      endDate: '2026-10-31',
    },
    comparePeriod: {
      id: 'p-prev',
      code: 'P-PREV',
      name: 'Period Before',
      periodType: 'MONTH',
      startDate: '2026-09-01',
      endDate: '2026-09-30',
    },
    availablePeriods: [
      {
        id: 'p-cur',
        code: 'P-CUR',
        name: 'Period Current',
        periodType: 'MONTH',
        startDate: '2026-10-01',
        endDate: '2026-10-31',
      },
    ],
    metrics: {
      employeeCount: 3,
      activeEmployeeCount: 3,
      onLeaveEmployeeCount: 0,
      openWorkCount: 4,
      attentionWorkCount: 1,
      pendingReviewCount: 2,
      declinedEmployeeCount: 1,
      comparedEmployeeCount: 2,
      declinedChildUnitCount: null,
    },
    performance: {
      average: 81.25,
      previousAverage: 80,
      target: null,
      changeFromPrevious: 1.25,
      evaluatedEmployeeCount: 2,
      kpis: [],
      trend: [
        { periodId: 'p-prev', periodName: 'Period Before', average: 80 },
        { periodId: 'p-cur', periodName: 'Period Current', average: 81.25 },
      ],
      kpiGroups: [
        {
          name: 'Group One',
          displayOrder: 1,
          weights: [{ jobPositionId: 'pos-1', jobPositionTitle: 'Role X', weightPercent: 60 }],
          kpis: [
            {
              kpiDefinitionId: 'k-1',
              code: 'K1',
              name: 'Indicator One',
              unit: 'u',
              direction: 'HIGHER_BETTER',
              targetValue: 100,
              actualValue: 70,
              achievementPercent: 70,
              previousAchievementPercent: 75,
              achievementChange: -5,
              campaignActualValue: 20,
            },
            {
              kpiDefinitionId: 'k-2',
              code: 'K2',
              name: 'Indicator Two',
              unit: '%',
              direction: 'LOWER_BETTER',
              targetValue: 1,
              actualValue: 0.9,
              achievementPercent: 111,
              previousAchievementPercent: null,
              achievementChange: null,
              campaignActualValue: null,
            },
          ],
        },
      ],
      positions: [
        {
          jobPositionId: 'pos-1',
          jobPositionTitle: 'Role X',
          employeeCount: 3,
          evaluatedEmployeeCount: 2,
          average: 81.25,
          previousAverage: 80,
          changeFromPrevious: 1.25,
          groups: [
            {
              profileItemId: 'g-1',
              name: 'Group One',
              displayOrder: 1,
              weightPercent: 60,
              average: 77,
              isWeak: true,
            },
          ],
        },
      ],
    },
    workMix: [{ workTypeId: 'wt-1', code: 'WT1', name: 'Work Kind', count: 4 }],
    operational: {
      waiting: 1,
      inProgress: 2,
      overdue: 1,
      escalated: null,
      completedInPeriod: 6,
      pendingReview: 2,
    },
    attentionMembers: [
      {
        employeeId: 'emp-1',
        employeeCode: 'E-1',
        fullName: 'Person One',
        jobPositionTitle: 'Role X',
        totalScore: 70.5,
        previousTotalScore: 75,
        changeFromPrevious: -4.5,
        weakestGroup: { name: 'Group One', score: 61 },
        overdueWorkCount: 1,
        reasons: ['SCORE_DROP', 'LOW_SCORE', 'OVERDUE_WORK'],
      },
    ],
    childUnits: [],
    generatedAt: '2026-10-18T08:30:00Z',
    ...patch,
  };
}

export function memberFixture(patch: Partial<DepartmentMember> = {}): DepartmentMember {
  return {
    id: 'emp-1',
    employeeCode: 'E-1',
    fullName: 'Person One',
    departmentId: 'dep-1',
    departmentName: 'Unit Alpha',
    jobPositionId: 'pos-1',
    jobPositionTitle: 'Role X',
    status: 'ACTIVE',
    openWorkCount: 3,
    performanceScore: 70.5,
    evaluationStatus: 'APPROVED',
    previousPerformanceScore: 75,
    changeFromPrevious: -4.5,
    groups: [
      { profileItemId: 'g-1', name: 'Group One', displayOrder: 1, weightPercent: 60, score: 61 },
    ],
    weakestProfileItemId: 'g-1',
    overdueWorkCount: 1,
    pendingReviewCount: 1,
    ...patch,
  };
}

describe('perf-display', () => {
  it('formats null as a dash and keeps signs and arrows', () => {
    expect(formatValue('en', null)).toBe(NOT_AVAILABLE);
    expect(formatValue('en', 84.95)).toBe('85.0');
    expect(formatValue('vi', 84.9)).toBe('84,9');
    expect(formatSigned('en', 2)).toBe('+2.0');
    expect(formatSigned('en', -0.4)).toBe('−0.4');
    expect(formatArrow('en', -6.5)).toBe('▼ 6.5');
    expect(formatArrow('en', 0.1)).toBe('▲ 0.1');
    expect(formatArrow('en', undefined)).toBe(NOT_AVAILABLE);
  });

  it('derives tone, bar width and initials', () => {
    expect(toneOf(1)).toBe('up');
    expect(toneOf(-1)).toBe('down');
    expect(toneOf(0)).toBe('flat');
    expect(toneOf(null)).toBeNull();
    expect(barWidth(50, 200)).toBe(25);
    expect(barWidth(300, 100)).toBe(100);
    expect(barWidth(null, 100)).toBe(0);
    expect(barWidth(5, 0)).toBe(0);
    expect(initials('nguyễn văn an')).toBe('VA');
  });
});
