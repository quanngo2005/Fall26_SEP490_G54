import { deadlineSpan, endOfDeadlineDay, percentDone, spanParts } from './work-detail.utils';

describe('work detail time helpers', () => {
  it('treats the deadline day as running until 23:59:59 local time', () => {
    const end = endOfDeadlineDay('2026-10-25');

    expect([end.getFullYear(), end.getMonth(), end.getDate()]).toEqual([2026, 9, 25]);
    expect([end.getHours(), end.getMinutes(), end.getSeconds()]).toEqual([23, 59, 59]);
  });

  it('has no span for a work without a deadline', () => {
    expect(deadlineSpan(null)).toBeNull();
  });

  it('counts what is left before the deadline in days and hours', () => {
    const span = deadlineSpan('2026-10-25', new Date(2026, 9, 24, 17, 59, 59))!;

    expect(span).toEqual(
      jasmine.objectContaining({ overdue: false, now: false, days: 1, hours: 6, minutes: 0 }),
    );
    expect(spanParts(span)).toEqual([
      { unit: 'day', n: 1 },
      { unit: 'hour', n: 6 },
    ]);
  });

  it('shows hours and minutes on the last day, minutes alone in the last hour', () => {
    const hours = deadlineSpan('2026-10-25', new Date(2026, 9, 25, 18, 29, 59))!;
    const minutes = deadlineSpan('2026-10-25', new Date(2026, 9, 25, 23, 14, 59))!;

    expect(spanParts(hours)).toEqual([
      { unit: 'hour', n: 5 },
      { unit: 'minute', n: 30 },
    ]);
    expect(spanParts(minutes)).toEqual([{ unit: 'minute', n: 45 }]);
  });

  it('reports how late a work is once the deadline has passed', () => {
    const span = deadlineSpan('2026-10-25', new Date(2026, 9, 27, 0, 59, 59))!;

    expect(span.overdue).toBeTrue();
    expect(spanParts(span)).toEqual([
      { unit: 'day', n: 1 },
      { unit: 'hour', n: 1 },
    ]);
  });

  it('marks the last minute as "now" with nothing to count', () => {
    const span = deadlineSpan('2026-10-25', new Date(2026, 9, 25, 23, 59, 30))!;

    expect(span.now).toBeTrue();
    expect(spanParts(span)).toEqual([]);
  });

  it('rounds the share of done items and has none for an empty list', () => {
    expect(percentDone(2, 4)).toBe(50);
    expect(percentDone(1, 3)).toBe(33);
    expect(percentDone(2, 3)).toBe(67);
    expect(percentDone(0, 0)).toBeNull();
  });
});
