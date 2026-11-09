import { currentPeriod, sortPeriodsNewestFirst } from './period-options';

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
});
