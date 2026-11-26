import {
  deltaPercent,
  rebalanceQuarters,
  splitByQuarterShape,
  splitByWeights,
} from './target-allocation.utils';

describe('target allocation utils', () => {
  it('splits a total so the parts add up to it', () => {
    const parts = splitByWeights(100, [1, 1, 1]);
    expect(parts).toEqual([34, 33, 33]);
    expect(splitByWeights(100, [1, 3])).toEqual([25, 75]);
  });

  it('falls back to an equal split when every weight is zero', () => {
    expect(splitByWeights(10, [0, 0])).toEqual([5, 5]);
    expect(splitByWeights(10, [])).toEqual([]);
  });
});

describe('deltaPercent', () => {
  it('rounds the change to one decimal and ignores a missing or zero base', () => {
    expect(deltaPercent(6400, 6100)).toBe(4.9);
    expect(deltaPercent(2000, 2000)).toBe(0);
    expect(deltaPercent(10, 0)).toBeNull();
    expect(deltaPercent(10, null)).toBeNull();
  });
});

describe('splitByQuarterShape', () => {
  it('spreads the year value by the parent quarter proportions', () => {
    expect(splitByQuarterShape(100, [25, 25, 25, 25])).toEqual([25, 25, 25, 25]);
    const uneven = splitByQuarterShape(100, [22, 24, 25, 29]);
    expect(uneven.reduce((a, b) => a + b, 0)).toBe(100);
  });

  it('splits progressively 1:2:3:4 when the parent has no quarter shape', () => {
    expect(splitByQuarterShape(1000, [0, 0, 0, 0])).toEqual([100, 200, 300, 400]);
  });
});

describe('rebalanceQuarters (W11)', () => {
  it('keeps the year fixed and spreads the difference over the other three quarters', () => {
    const result = rebalanceQuarters(2000, [500, 500, 500, 500], 1, 800);
    expect(result[1]).toBe(800);
    expect(result.reduce((a, b) => a + b, 0)).toBe(2000);
  });

  it('clamps the edited quarter to [0, year]', () => {
    const over = rebalanceQuarters(1000, [250, 250, 250, 250], 0, 5000);
    expect(over[0]).toBe(1000);
    expect(over.reduce((a, b) => a + b, 0)).toBe(1000);
  });

  it('splits the remainder equally when the other quarters are all zero', () => {
    const result = rebalanceQuarters(900, [0, 0, 0, 0], 0, 300);
    expect(result).toEqual([300, 200, 200, 200]);
  });
});
