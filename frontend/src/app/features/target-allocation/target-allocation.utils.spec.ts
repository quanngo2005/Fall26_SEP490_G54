import { remainingOf, splitByWeights, weekdaysBetween } from './target-allocation.utils';

describe('target allocation utils', () => {
  it('counts Monday to Friday in a period', () => {
    expect(weekdaysBetween('2026-10-01', '2026-12-31')).toBe(66);
    expect(weekdaysBetween('2026-10-03', '2026-10-04')).toBe(0);
  });

  it('splits a total so the parts add up to it', () => {
    const parts = splitByWeights(100, [1, 1, 1]);
    expect(parts).toEqual([34, 33, 33]);
    expect(splitByWeights(100, [1, 3])).toEqual([25, 75]);
  });

  it('falls back to an equal split when every weight is zero', () => {
    expect(splitByWeights(10, [0, 0])).toEqual([5, 5]);
    expect(splitByWeights(10, [])).toEqual([]);
  });

  it('reports what is left to hand out, treating empty cells as 0', () => {
    expect(remainingOf(100, [30, null, 20])).toBe(50);
    expect(remainingOf(100, [60, 60])).toBe(-20);
  });
});
