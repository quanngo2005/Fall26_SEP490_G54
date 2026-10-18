/**
 * Enum wire adapter.
 *
 * docs/api/API_CONVENTIONS.md specifies UPPER_SNAKE enum strings, but the Employee, KPI definition
 * and Work type DTOs are currently serialized by ASP.NET Core without a string-enum converter, so
 * they arrive as ordinals. Reading accepts both forms and writing sends the ordinal, which both a
 * converter-less and a string-enum backend accept. Presentation code only sees the canonical string.
 */
const normalize = (value: string): string => value.replace(/[^a-z0-9]/gi, '').toUpperCase();

export function parseWireEnum<T extends string>(
  values: readonly T[],
  raw: unknown,
  firstOrdinal = 0,
): T | null {
  if (typeof raw === 'number') return values[raw - firstOrdinal] ?? null;
  if (typeof raw !== 'string') return null;
  const key = normalize(raw);
  return values.find((value) => normalize(value) === key) ?? null;
}

export function toWireOrdinal<T extends string>(
  values: readonly T[],
  value: T,
  firstOrdinal = 0,
): number {
  return values.indexOf(value) + firstOrdinal;
}
