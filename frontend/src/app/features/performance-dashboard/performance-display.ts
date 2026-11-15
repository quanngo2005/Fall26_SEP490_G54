/**
 * Display helpers shared by the performance dashboards (SCR-PRF-02 / SCR-PRF-03). They only format values the API
 * returns; every figure, threshold and label comes from the API or the localization dictionary.
 */
export const NOT_AVAILABLE = '—';

export type Tone = 'up' | 'down' | 'flat';

/** Visual only: how far an achievement track extends past its 100 % marker. */
export const TRACK_HEADROOM = 1.15;

/** A number in the current locale with a fixed number of decimals; null shows as "—". */
export function formatValue(
  locale: string,
  value: number | null | undefined,
  decimals = 1,
): string {
  if (value == null) return NOT_AVAILABLE;
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
}

/** A change with an explicit sign ("+2,0", "−0,4"); null shows as "—". */
export function formatSigned(
  locale: string,
  value: number | null | undefined,
  decimals = 1,
): string {
  if (value == null) return NOT_AVAILABLE;
  const text = formatValue(locale, Math.abs(value), decimals);
  if (value > 0) return `+${text}`;
  return value < 0 ? `−${text}` : text;
}

/** A change with an arrow ("▲ 0,1", "▼ 6,5"); null shows as "—". */
export function formatArrow(
  locale: string,
  value: number | null | undefined,
  decimals = 1,
): string {
  if (value == null) return NOT_AVAILABLE;
  const text = formatValue(locale, Math.abs(value), decimals);
  if (value > 0) return `▲ ${text}`;
  return value < 0 ? `▼ ${text}` : text;
}

export function toneOf(value: number | null | undefined): Tone | null {
  if (value == null) return null;
  if (value > 0) return 'up';
  return value < 0 ? 'down' : 'flat';
}

/** Width in percent of `value` on a 0..`max` track, clamped to 0..100. */
export function barWidth(value: number | null | undefined, max: number): number {
  if (value == null || max <= 0) return 0;
  return Math.min(100, Math.max(0, (value / max) * 100));
}

/** Two-letter initials of a person's name (last two words). */
export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(-2)
    .map((part) => part[0])
    .join('')
    .toUpperCase();
}
