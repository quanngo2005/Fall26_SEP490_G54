import {
  distribute,
  editText,
  formatUnits,
  parseNumber,
  stepFor,
  unitsOf,
  valueOfUnits,
} from './target-math';

describe('target-math', () => {
  it('parses Vietnamese grouping and decimals', () => {
    expect(parseNumber('40.000', 'vi-VN')).toBe(40000);
    expect(parseNumber('1.234,5', 'vi-VN')).toBe(1234.5);
    expect(parseNumber('0,25', 'vi-VN')).toBe(0.25);
  });

  it('parses English grouping and decimals', () => {
    expect(parseNumber('40,000', 'en-US')).toBe(40000);
    expect(parseNumber('1,234.5', 'en-US')).toBe(1234.5);
  });

  it('treats empty or invalid text as 0', () => {
    expect(parseNumber('', 'vi-VN')).toBe(0);
    expect(parseNumber('abc', 'en-US')).toBe(0);
  });

  it('formats exact units with locale grouping', () => {
    expect(formatUnits(unitsOf(6_000_000_000), 'vi-VN')).toBe('6.000.000.000');
    expect(formatUnits(unitsOf(1234.5), 'en-US')).toBe('1,234.5');
    expect(formatUnits(unitsOf(-40), 'vi-VN')).toBe('-40');
  });

  it('shows the edit text with the locale decimal separator', () => {
    expect(editText(0.25, 'vi-VN')).toBe('0,25');
    expect(editText(0.25, 'en-US')).toBe('0.25');
  });

  it('distributes so the shares add up exactly, remainder on the last share', () => {
    const total = unitsOf(1000);
    const shares = distribute(total, [1n, 1n, 1n], stepFor(1000));

    expect(shares.map(valueOfUnits)).toEqual([333, 333, 334]);
    expect(shares.reduce((sum, share) => sum + share, 0n)).toBe(total);
  });

  it('splits by weight and falls back to equal shares when all weights are 0', () => {
    expect(
      distribute(unitsOf(100), [unitsOf(1), unitsOf(3)], stepFor(100)).map(valueOfUnits),
    ).toEqual([25, 75]);
    expect(distribute(unitsOf(10), [0n, 0n], stepFor(10)).map(valueOfUnits)).toEqual([5, 5]);
  });
});
