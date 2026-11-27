import {
  currentPeriod,
  findPreviousPeriod,
  findSameLastYearPeriod,
  sortPeriodsNewestFirst,
} from './period-options';
import { EvaluationPeriodOption } from './evaluation.models';

const p = (id: string, startDate: string, endDate: string) => ({ id, startDate, endDate });

describe('period options', () => {
  const periods = [
    p('year', '2026-01-01', '2026-12-31'),
    p('sep', '2026-09-01', '2026-09-30'),
    p('q4', '2026-10-01', '2026-12-31'),
    p('oct', '2026-10-01', '2026-10-31'),
  ];

  it('lists the newest period first and the shorter one first for the same start', () => {
    expect(sortPeriodsNewestFirst(periods).map((x) => x.id)).toEqual(['oct', 'q4', 'sep', 'year']);
  });

  it('does not change the list it is given and copes with missing dates', () => {
    const copy = [...periods];
    sortPeriodsNewestFirst(copy);
    expect(copy).toEqual(periods);
    expect(
      sortPeriodsNewestFirst([{ startDate: undefined, endDate: undefined }] as never).length,
    ).toBe(1);
  });

  it('opens on the shortest period that contains today', () => {
    expect(currentPeriod(periods, new Date(2026, 9, 20))?.id).toBe('oct');
    expect(currentPeriod(periods, new Date(2026, 10, 20))?.id).toBe('q4');
    expect(currentPeriod(periods, new Date(2026, 0, 5))?.id).toBe('year');
  });

  it('returns null when no period contains today or the dates are missing', () => {
    expect(currentPeriod(periods, new Date(2027, 5, 1))).toBeNull();
    expect(currentPeriod([{ startDate: '', endDate: '' }], new Date())).toBeNull();
    expect(currentPeriod([], new Date())).toBeNull();
  });

  describe('findPreviousPeriod and findSameLastYearPeriod', () => {
    const currentQ3: EvaluationPeriodOption = {
      id: 'p-2026-q3',
      code: '2026-Q3',
      name: 'Quý 3/2026',
      startDate: '2026-07-01',
      endDate: '2026-09-30',
      periodType: 'Quarterly',
      status: 'OPEN',
      isActive: true,
    };

    const pastQ2: EvaluationPeriodOption = {
      id: 'p-2026-q2',
      code: '2026-Q2',
      name: 'Quý 2/2026',
      startDate: '2026-04-01',
      endDate: '2026-06-30',
      periodType: 'Quarterly',
      status: 'CLOSED',
      isActive: false,
    };

    const pastQ1: EvaluationPeriodOption = {
      id: 'p-2026-q1',
      code: '2026-Q1',
      name: 'Quý 1/2026',
      startDate: '2026-01-01',
      endDate: '2026-03-31',
      periodType: 'Quarterly',
      status: 'CLOSED',
      isActive: false,
    };

    const lastYearQ3: EvaluationPeriodOption = {
      id: 'p-2025-q3',
      code: '2025-Q3',
      name: 'Quý 3/2025',
      startDate: '2025-07-01',
      endDate: '2025-09-30',
      periodType: 'Quarterly',
      status: 'CLOSED',
      isActive: false,
    };

    const monthlyPeriod: EvaluationPeriodOption = {
      id: 'p-2026-m06',
      code: '2026-M06',
      name: 'Tháng 6/2026',
      startDate: '2026-06-01',
      endDate: '2026-06-30',
      periodType: 'Monthly',
      status: 'CLOSED',
      isActive: false,
    };

    it('returns immediate previous period of same type', () => {
      const candidates = [pastQ1, pastQ2, monthlyPeriod, lastYearQ3];
      const prev = findPreviousPeriod(currentQ3, candidates);
      expect(prev).toBe(pastQ2);
    });

    it('returns null when no previous period exists', () => {
      expect(findPreviousPeriod(pastQ1, [currentQ3, monthlyPeriod])).toBeNull();
    });

    it('returns same period from 1 year ago', () => {
      const candidates = [pastQ1, pastQ2, lastYearQ3, monthlyPeriod];
      const sameLastYear = findSameLastYearPeriod(currentQ3, candidates);
      expect(sameLastYear).toBe(lastYearQ3);
    });

    it('returns null when no matching period from 1 year ago exists', () => {
      const candidates = [pastQ1, pastQ2];
      expect(findSameLastYearPeriod(currentQ3, candidates)).toBeNull();
    });
  });
});
