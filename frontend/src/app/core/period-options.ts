import { EvaluationPeriodOption } from './evaluation.models';

type PeriodRange = Pick<EvaluationPeriodOption, 'startDate' | 'endDate'>;

const spanOf = (period: PeriodRange): number =>
  Date.parse(period.endDate) - Date.parse(period.startDate);

/** Newest period first; for the same start the shorter one (month before quarter before year) comes first. */
export function sortPeriodsNewestFirst<T extends PeriodRange>(periods: readonly T[]): T[] {
  return [...periods].sort(
    (a, b) =>
      (b.startDate ?? '').localeCompare(a.startDate ?? '') ||
      (a.endDate ?? '').localeCompare(b.endDate ?? ''),
  );
}

/**
 * The period a list should open on: of the periods that contain {@link today}, the shortest one (a month rather
 * than the quarter or year around it). `null` when no period contains the day, so the list falls back to every period.
 */
export function currentPeriod<T extends PeriodRange>(
  periods: readonly T[],
  today: Date = new Date(),
): T | null {
  const day = [
    today.getFullYear(),
    String(today.getMonth() + 1).padStart(2, '0'),
    String(today.getDate()).padStart(2, '0'),
  ].join('-');
  const containing = periods.filter(
    (p) => !!p.startDate && !!p.endDate && p.startDate <= day && p.endDate >= day,
  );
  return containing.sort((a, b) => spanOf(a) - spanOf(b))[0] ?? null;
}
