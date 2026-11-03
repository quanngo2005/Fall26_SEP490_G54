/** Monday to Friday between two `YYYY-MM-DD` dates, inclusive (same calendar as the backend breakdown). */
export function weekdaysBetween(start: string, end: string): number {
  const last = Date.parse(`${end}T00:00:00Z`);
  let count = 0;
  for (let day = Date.parse(`${start}T00:00:00Z`); day <= last; day += 86_400_000) {
    const weekday = new Date(day).getUTCDay();
    if (weekday !== 0 && weekday !== 6) count += 1;
  }
  return count;
}

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

/** What is still to hand out: positive = short, negative = too much. Empty cells count as 0. */
export function remainingOf(target: number, values: readonly (number | null)[]): number {
  return target - values.reduce<number>((sum, value) => sum + (value ?? 0), 0);
}
