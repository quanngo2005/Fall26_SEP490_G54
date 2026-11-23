/**
 * Exact decimal arithmetic for the target setup screens. Targets carry 4 decimals (NUMERIC(30,4) in the
 * database), so values are compared and split as integer units of 1e-4 and never drift with floating point.
 */
export const SCALE = 10000;
export const SCALE_UNITS = 10000n;

export function roundValue(value: number): number {
  return Number.isFinite(value) ? Math.round(value * SCALE) / SCALE : 0;
}

export function unitsOf(value: number): bigint {
  const fixed = (Number.isFinite(value) ? value : 0).toFixed(4);
  const negative = fixed.startsWith('-');
  const [whole, fraction = ''] = fixed.replace('-', '').split('.');
  const magnitude =
    BigInt(whole || '0') * SCALE_UNITS + BigInt(fraction.padEnd(4, '0').slice(0, 4) || '0');
  return negative ? -magnitude : magnitude;
}

export function valueOfUnits(units: bigint): number {
  return Number(units) / SCALE;
}

function floorDiv(value: bigint, divisor: bigint): bigint {
  const quotient = value / divisor;
  const remainder = value % divisor;
  const differentSigns = value < 0n !== divisor < 0n;
  return remainder !== 0n && differentSigns ? quotient - 1n : quotient;
}

/** Whole units when the source value is an integer, otherwise the full 4-decimal precision. */
export function stepFor(value: number): bigint {
  return Number.isInteger(value) ? SCALE_UNITS : 1n;
}

/**
 * Spreads `total` over `weights`, rounding every share except the last one down to `step` and putting the
 * remaining units on the last share so the result adds up exactly. Non-positive total weight = equal shares.
 */
export function distribute(total: bigint, weights: readonly bigint[], step: bigint): bigint[] {
  if (weights.length === 0) {
    return [];
  }

  let effective = weights;
  let totalWeight = weights.reduce((sum, weight) => sum + weight, 0n);
  if (totalWeight <= 0n) {
    effective = weights.map(() => 1n);
    totalWeight = BigInt(weights.length);
  }

  const shares: bigint[] = [];
  let assigned = 0n;
  for (let index = 0; index < effective.length - 1; index += 1) {
    const raw = floorDiv(total * effective[index], totalWeight);
    const share = floorDiv(raw, step) * step;
    shares.push(share);
    assigned += share;
  }
  shares.push(total - assigned);
  return shares;
}

export function numberLocale(language: string): string {
  return language === 'vi' ? 'vi-VN' : 'en-US';
}

/** Grouped text of an exact unit amount (`1.234,5` in vi-VN, `1,234.5` in en-US). */
export function formatUnits(units: bigint, locale: string): string {
  const group = locale === 'vi-VN' ? '.' : ',';
  const decimal = locale === 'vi-VN' ? ',' : '.';
  const negative = units < 0n;
  const absolute = negative ? -units : units;
  const whole = (absolute / SCALE_UNITS).toString().replace(/\B(?=(\d{3})+(?!\d))/g, group);
  const fraction = (absolute % SCALE_UNITS).toString().padStart(4, '0').replace(/0+$/, '');
  const body = fraction ? `${whole}${decimal}${fraction}` : whole;
  return negative ? `-${body}` : body;
}

export function formatValue(value: number, locale: string): string {
  return formatUnits(unitsOf(value), locale);
}

/**
 * Parses what the user typed in the given locale: vi-VN groups with `.` and uses `,` for decimals (`40.000`
 * is forty thousand), en-US the other way round. Empty or invalid text is 0.
 */
export function parseNumber(text: string, locale: string): number {
  const cleaned = text.replace(/\s/g, '');
  if (!cleaned) {
    return 0;
  }
  const normalized =
    locale === 'vi-VN' ? cleaned.replace(/\./g, '').replace(',', '.') : cleaned.replace(/,/g, '');
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? roundValue(parsed) : 0;
}

/** The raw text an input shows while focused: no grouping, locale decimal separator. */
export function editText(value: number, locale: string): string {
  const plain = `${roundValue(value)}`;
  return locale === 'vi-VN' ? plain.replace('.', ',') : plain;
}
