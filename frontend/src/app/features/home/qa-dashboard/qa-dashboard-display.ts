import { QaDashboardWeeklyRate } from '../../../core/qa-dashboard.models';

/** The deadline notice is shown when the lock is this close (calendar days) or codes still wait for an import. */
export const NOTICE_WINDOW_DAYS = 7;

/** Reason that is the QA/admin's job (import the file); every other reason is the employee's to fix. */
export const IMPORT_REASON = 'NO_IMPORTED_TRANSACTION';

const DAY_MS = 86_400_000;

/** Replaces `{name}` placeholders of a localized template. */
export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    key in values ? String(values[key]) : match,
  );
}

/** Calendar days from today to the deadline in the viewer's time zone (negative once it has passed). */
export function daysUntil(deadline: string, now: Date = new Date()): number {
  const end = new Date(deadline);
  const startOfDay = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
  return Math.round((startOfDay(end) - startOfDay(now)) / DAY_MS);
}

/** Locale number with a fixed number of decimals ("95,6" in vi, "95.6" in en). */
export function formatNumber(locale: string, value: number, decimals = 0): string {
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
}

/** Day and month in the locale order, from the locale's full date pattern ("DD/MM/YYYY" → "DD/MM"). */
export function shortDatePattern(datePattern: string): string {
  return datePattern.replace(/[^DMY]?YYYY[^DMY]?/, '');
}

/** NOT_MATCHED codes of a week: `totalCount` only counts decided codes (MATCHED + NOT_MATCHED), not IN_REVIEW. */
export function openCount(week: QaDashboardWeeklyRate): number {
  return Math.max(0, week.totalCount - week.matchedCount);
}

/** A round axis maximum (1, 2 or 5 × 10ⁿ steps) that leaves head room for the value labels. */
export function niceAxisMax(value: number, ticks = 3): number {
  if (value <= 0) return ticks;
  const raw = (value * 1.15) / ticks;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? raw;
  return Math.max(ticks, Math.ceil(step) * ticks);
}

/** Index of the week to highlight: the API's current week, otherwise the latest one. */
export function currentWeekIndex(weeks: QaDashboardWeeklyRate[], label: string): number {
  const index = weeks.findIndex((w) => w.weekLabel === label);
  return index >= 0 ? index : weeks.length - 1;
}

/** The latest week before `index` that has a match rate, to compare the current rate with. */
export function previousRatedWeek(
  weeks: QaDashboardWeeklyRate[],
  index: number,
): QaDashboardWeeklyRate | null {
  for (let i = index - 1; i >= 0; i--) {
    if (weeks[i].matchRate != null) return weeks[i];
  }
  return null;
}
