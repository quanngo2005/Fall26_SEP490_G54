import { WorkListItem, WorkStatus } from '../../core/work.models';

export type BoardColumnKey = 'new' | 'inProgress' | 'submitted' | 'done' | 'overdue';

interface BoardColumnDef {
  key: BoardColumnKey;
  /** Statuses whose works are listed in this column (queried with one `GET /works?status=a,b` call). */
  statuses: readonly WorkStatus[];
}

/** Whose work a board shows: the signed-in employee's own, or the whole department's. */
export type WorkScope = 'mine' | 'team';

/**
 * `Cancelled` is intentionally not shown. `HandedOver` and `Rejected` stay with the works the assignee still
 * has to do. An employee's board keeps `Verified` with the submitted ones, since only a manager completes it.
 */
export const BOARD_COLUMNS: readonly BoardColumnDef[] = [
  { key: 'new', statuses: ['New'] },
  { key: 'inProgress', statuses: ['InProgress', 'HandedOver', 'Rejected'] },
  { key: 'submitted', statuses: ['Submitted', 'Verified'] },
  { key: 'done', statuses: ['Done'] },
  { key: 'overdue', statuses: ['Overdue'] },
];

/** A manager's board shows `Verified` work in the Done column, where it waits to be completed. */
export const TEAM_COLUMNS: readonly BoardColumnDef[] = BOARD_COLUMNS.map((column) => {
  if (column.key === 'submitted') return { ...column, statuses: ['Submitted'] };
  if (column.key === 'done') return { ...column, statuses: ['Verified', 'Done'] };
  return column;
});

export type QuickFilter = 'open' | 'dueToday' | 'overdue' | 'done';
export type DueRange = 'all' | 'today' | 'week' | 'month';

const QUICK_FILTER_COLUMNS: Readonly<Record<QuickFilter, readonly BoardColumnKey[]>> = {
  open: ['new', 'inProgress', 'submitted'],
  dueToday: ['new', 'inProgress', 'submitted'],
  overdue: ['overdue'],
  done: ['done'],
};

/** Columns kept visible by a KPI card (null = all). A manager has no due-today or done card. */
export function quickFilterColumns(
  scope: WorkScope,
  filter: QuickFilter | null,
): readonly BoardColumnKey[] | null {
  if (!filter || (scope === 'team' && (filter === 'dueToday' || filter === 'done'))) return null;
  return QUICK_FILTER_COLUMNS[filter];
}

/** `PATCH /works/{id}/status` lets the assignee set `InProgress` only from these statuses. */
const STARTABLE: readonly WorkStatus[] = ['New', 'HandedOver', 'Rejected'];

const BUSINESS_TIME_ZONE = 'Asia/Ho_Chi_Minh';
const MINUTES_PER_DAY = 24 * 60;
const MS_PER_DAY = 86_400_000;

export function canStart(item: Pick<WorkListItem, 'status'>): boolean {
  return STARTABLE.includes(item.status);
}

/** `PATCH /works/{id}/status` lets the assignee's direct manager complete a work only once it is verified. */
export function canComplete(item: Pick<WorkListItem, 'status'>): boolean {
  return item.status === 'Verified';
}

export function columnKeyOf(
  columns: readonly BoardColumnDef[],
  status: WorkStatus,
): BoardColumnKey | null {
  return columns.find((column) => column.statuses.includes(status))?.key ?? null;
}

export interface BusinessClock {
  /** Business-day date `YYYY-MM-DD` in Vietnam time (UTC+7), like the overdue job on the server. */
  date: string;
  minutesOfDay: number;
}

export function businessClock(now: Date): BusinessClock {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: BUSINESS_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(now);
  const part = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find((candidate) => candidate.type === type)?.value ?? '00';
  return {
    date: `${part('year')}-${part('month')}-${part('day')}`,
    minutesOfDay: Number(part('hour')) * 60 + Number(part('minute')),
  };
}

/** Minutes left today, split for display. */
export function timeLeftToday(clock: BusinessClock): { hours: number; minutes: number } {
  const left = Math.max(0, MINUTES_PER_DAY - clock.minutesOfDay);
  return { hours: Math.floor(left / 60), minutes: left % 60 };
}

/** Whole days since the epoch for a `YYYY-MM-DD` date. */
function dayNumber(date: string): number {
  const [year, month, day] = date.split('-').map(Number);
  return Math.floor(Date.UTC(year, month - 1, day) / MS_PER_DAY);
}

/** Days past `plannedEndDate` as of `today`; 0 when not late or without a deadline. */
export function daysOverdue(plannedEndDate: string | null, today: string): number {
  return plannedEndDate ? Math.max(0, dayNumber(today) - dayNumber(plannedEndDate)) : 0;
}

export function isDueOn(plannedEndDate: string | null, today: string): boolean {
  return plannedEndDate === today;
}

export function inDueRange(plannedEndDate: string | null, range: DueRange, today: string): boolean {
  if (range === 'all') return true;
  if (!plannedEndDate) return false;
  if (range === 'today') return plannedEndDate === today;
  if (range === 'month') return plannedEndDate.slice(0, 7) === today.slice(0, 7);
  const current = dayNumber(today);
  // Epoch day 0 is a Thursday, so (day + 3) % 7 counts days since Monday.
  const weekStart = current - ((current + 3) % 7);
  const day = dayNumber(plannedEndDate);
  return day >= weekStart && day <= weekStart + 6;
}

/** `YYYY-MM` of an ISO date-time in Vietnam time. */
export function businessMonth(isoDateTime: string): string {
  const time = Date.parse(isoDateTime);
  return Number.isNaN(time) ? '' : businessClock(new Date(time)).date.slice(0, 7);
}

/** Local `Date` for a date-only value, so it formats without a time zone shift. */
export function dateOnly(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}
