import { formatUnits, unitsOf } from './target-math';

describe('target-math', () => {
  it('formats exact units with locale grouping', () => {
    expect(formatUnits(unitsOf(6_000_000_000), 'vi-VN')).toBe('6.000.000.000');
    expect(formatUnits(unitsOf(1234.5), 'en-US')).toBe('1,234.5');
    expect(formatUnits(unitsOf(-40), 'vi-VN')).toBe('-40');
  });
});
