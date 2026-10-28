import {
  BOARD_COLUMNS,
  TEAM_COLUMNS,
  businessClock,
  businessMonth,
  canComplete,
  canStart,
  columnKeyOf,
  daysOverdue,
  inDueRange,
  quickFilterColumns,
  timeLeftToday,
} from './work-board.utils';

describe('work board helpers', () => {
  it('derives the Vietnam business day and the time left today', () => {
    // 2026-10-27T17:30Z is 00:30 on 28/10 in UTC+7.
    const clock = businessClock(new Date('2026-10-27T17:30:00Z'));
    expect(clock.date).toBe('2026-10-28');
    expect(clock.minutesOfDay).toBe(30);
    expect(timeLeftToday(clock)).toEqual({ hours: 23, minutes: 30 });
    expect(timeLeftToday({ date: '2026-10-28', minutesOfDay: 24 * 60 })).toEqual({
      hours: 0,
      minutes: 0,
    });
  });

  it('counts days overdue and never goes negative', () => {
    expect(daysOverdue('2026-10-23', '2026-10-24')).toBe(1);
    expect(daysOverdue('2026-10-30', '2026-10-24')).toBe(0);
    expect(daysOverdue(null, '2026-10-24')).toBe(0);
    expect(daysOverdue('2026-12-31', '2027-01-02')).toBe(2);
  });

  it('matches due ranges using a Monday to Sunday week', () => {
    const today = '2026-10-28'; // Wednesday; week is 26/10 - 01/11
    expect(inDueRange(null, 'all', today)).toBeTrue();
    expect(inDueRange(null, 'today', today)).toBeFalse();
    expect(inDueRange('2026-10-28', 'today', today)).toBeTrue();
    expect(inDueRange('2026-10-26', 'week', today)).toBeTrue();
    expect(inDueRange('2026-11-01', 'week', today)).toBeTrue();
    expect(inDueRange('2026-10-25', 'week', today)).toBeFalse();
    expect(inDueRange('2026-11-02', 'week', today)).toBeFalse();
    expect(inDueRange('2026-10-01', 'month', today)).toBeTrue();
    expect(inDueRange('2026-11-01', 'month', today)).toBeFalse();
  });

  it('puts every status except Cancelled into a column', () => {
    expect(columnKeyOf(BOARD_COLUMNS, 'New')).toBe('new');
    expect(columnKeyOf(BOARD_COLUMNS, 'HandedOver')).toBe('inProgress');
    expect(columnKeyOf(BOARD_COLUMNS, 'Rejected')).toBe('inProgress');
    expect(columnKeyOf(BOARD_COLUMNS, 'Verified')).toBe('submitted');
    expect(columnKeyOf(BOARD_COLUMNS, 'Overdue')).toBe('overdue');
    expect(columnKeyOf(BOARD_COLUMNS, 'Cancelled')).toBeNull();
  });

  it('lists Verified work in the Done column on a manager board', () => {
    expect(columnKeyOf(TEAM_COLUMNS, 'Verified')).toBe('done');
    expect(columnKeyOf(TEAM_COLUMNS, 'Submitted')).toBe('submitted');
    expect(TEAM_COLUMNS.map((column) => column.key)).toEqual(BOARD_COLUMNS.map((c) => c.key));
  });

  it('completes only a Verified work', () => {
    expect(canComplete({ status: 'Verified' })).toBeTrue();
    expect(
      ['New', 'InProgress', 'Submitted', 'Overdue', 'Done', 'Cancelled'].some((status) =>
        canComplete({ status } as never),
      ),
    ).toBeFalse();
  });

  it('narrows the columns by a quick filter, and a manager has only open and overdue', () => {
    expect(quickFilterColumns('mine', null)).toBeNull();
    expect(quickFilterColumns('mine', 'done')).toEqual(['done']);
    expect(quickFilterColumns('mine', 'dueToday')).toEqual(['new', 'inProgress', 'submitted']);
    expect(quickFilterColumns('team', 'overdue')).toEqual(['overdue']);
    expect(quickFilterColumns('team', 'done')).toBeNull();
    expect(quickFilterColumns('team', 'dueToday')).toBeNull();
  });

  it('allows starting only from the statuses the API accepts', () => {
    expect(['New', 'HandedOver', 'Rejected'].every((status) => canStart({ status } as never))).toBe(
      true,
    );
    expect(
      ['InProgress', 'Submitted', 'Verified', 'Overdue', 'Done', 'Cancelled'].some((status) =>
        canStart({ status } as never),
      ),
    ).toBeFalse();
  });

  it('reads the business month of a date-time', () => {
    expect(businessMonth('2026-10-31T20:00:00+00:00')).toBe('2026-11');
    expect(businessMonth('not a date')).toBe('');
  });
});
