import { WorkStatus } from '../../core/work.models';
import { dateOnly } from '../work-board/work-board.utils';

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/** How far the end of the deadline day is from now. */
export interface DeadlineSpan {
  /** The deadline has passed. */
  overdue: boolean;
  /** Less than a minute apart: neither "left" nor "late" reads naturally. */
  now: boolean;
  days: number;
  hours: number;
  minutes: number;
}

/** One unit of a span as shown to people, from the biggest unit that matters. */
export interface SpanPart {
  unit: 'day' | 'hour' | 'minute';
  n: number;
}

/** Statuses in which a work still waits for the assignee, so a late deadline matters. */
export const OPEN_FOR_ASSIGNEE: readonly WorkStatus[] = [
  'New',
  'InProgress',
  'Rejected',
  'Overdue',
];

/** A deadline `YYYY-MM-DD` runs until 23:59:59 of that day, local time. */
export function endOfDeadlineDay(plannedEndDate: string): Date {
  const day = dateOnly(plannedEndDate);
  return new Date(day.getFullYear(), day.getMonth(), day.getDate(), 23, 59, 59);
}

/** Null when the work has no deadline. */
export function deadlineSpan(
  plannedEndDate: string | null,
  now: Date = new Date(),
): DeadlineSpan | null {
  if (!plannedEndDate) return null;
  const diff = endOfDeadlineDay(plannedEndDate).getTime() - now.getTime();
  const abs = Math.abs(diff);
  return {
    overdue: diff < 0,
    now: abs < MINUTE_MS,
    days: Math.floor(abs / DAY_MS),
    hours: Math.floor((abs % DAY_MS) / HOUR_MS),
    minutes: Math.floor((abs % HOUR_MS) / MINUTE_MS),
  };
}

/**
 * The two biggest non-zero units, so "1 day 6 hours" and "5 hours 30 minutes" read like people say them.
 * A span under a minute has no parts.
 */
export function spanParts(span: DeadlineSpan): SpanPart[] {
  const parts: SpanPart[] = [];
  if (span.days > 0) parts.push({ unit: 'day', n: span.days });
  if (span.hours > 0) parts.push({ unit: 'hour', n: span.hours });
  if (span.days === 0 && span.minutes > 0) parts.push({ unit: 'minute', n: span.minutes });
  return parts.slice(0, 2);
}

/** Share of done items as a whole percent; null without items. */
export function percentDone(done: number, total: number): number | null {
  return total > 0 ? Math.round((done * 100) / total) : null;
}
