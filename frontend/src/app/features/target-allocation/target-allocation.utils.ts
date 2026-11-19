/**
 * Splits `total` into whole units in proportion to `weights`; the units left by rounding go to the
 * first parts, so the parts always add up to `total`.
 */
export function splitByWeights(total: number, weights: readonly number[]): number[] {
  const sum = weights.reduce((acc, weight) => acc + weight, 0);
  if (weights.length === 0) return [];
  if (sum <= 0)
    return splitByWeights(
      total,
      weights.map(() => 1),
    );
  const parts = weights.map((weight) => Math.floor((total * weight) / sum));
  let rest = total - parts.reduce((acc, part) => acc + part, 0);
  for (let index = 0; rest > 0; index = (index + 1) % parts.length) {
    parts[index] += 1;
    rest -= 1;
  }
  return parts;
}

/** Change against a base in percent, to one decimal; null when there is no base to compare with. */
export function deltaPercent(value: number, base: number | null | undefined): number | null {
  return base ? Math.round(((value - base) / base) * 1000) / 10 : null;
}

/** Q1..Q4 proportional to a parent's own quarter shape; without one, progressive 1:2:3:4 (like the engine). */
export function splitByQuarterShape(
  year: number,
  parentQuarters: readonly [number, number, number, number],
): [number, number, number, number] {
  const shape = parentQuarters.some((value) => value > 0) ? parentQuarters : [1, 2, 3, 4];
  const [q1, q2, q3, q4] = splitByWeights(year, shape);
  return [q1, q2, q3, q4];
}

/**
 * W11: editing one quarter of an employee's own cumulative KPI keeps `year` fixed and spreads the
 * difference over the other three quarters, in proportion to what they already hold (equal split when
 * they are all 0). Returns the new [q1, q2, q3, q4].
 */
export function rebalanceQuarters(
  year: number,
  quarters: readonly [number, number, number, number],
  editedIndex: 0 | 1 | 2 | 3,
  newValue: number,
): [number, number, number, number] {
  const clamped = Math.max(0, Math.min(year, Math.floor(newValue)));
  const others = [0, 1, 2, 3].filter((index) => index !== editedIndex);
  const remainder = Math.max(0, year - clamped);
  const parts = splitByWeights(
    remainder,
    others.map((index) => quarters[index]),
  );
  const result: [number, number, number, number] = [0, 0, 0, 0];
  result[editedIndex] = clamped;
  others.forEach((index, position) => {
    result[index] = parts[position];
  });
  return result;
}
