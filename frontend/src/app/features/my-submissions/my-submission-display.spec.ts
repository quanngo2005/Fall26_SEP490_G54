import { badgeOf, hoursLeft } from './my-submission-display';

describe('my-submission-display', () => {
  it('lets the decision win over the reconciliation', () => {
    expect(badgeOf('WITHDRAWN', 'MATCHED')).toEqual({ key: 'withdrawn', tone: 'gray' });
    expect(badgeOf('REJECTED', 'MATCHED')).toEqual({ key: 'rejected', tone: 'red' });
    expect(badgeOf('APPROVED', 'NONE')).toEqual({ key: 'approved', tone: 'green' });
  });

  it('shows the reconciliation while the submission is still open', () => {
    expect(badgeOf('SUBMITTED', 'MATCHED').key).toBe('matched');
    expect(badgeOf('SUBMITTED', 'NOT_MATCHED')).toEqual({ key: 'notMatched', tone: 'red' });
    expect(badgeOf('UNDER_REVIEW', 'IN_REVIEW')).toEqual({ key: 'inReview', tone: 'gray' });
    expect(badgeOf('SUBMITTED', 'NONE')).toEqual({ key: 'manual', tone: 'amber' });
  });

  it('rounds the hours left up and never goes below zero', () => {
    const now = new Date('2026-10-20T09:00:00Z');
    expect(hoursLeft('2026-10-21T16:00:00Z', now)).toBe(31);
    expect(hoursLeft('2026-10-20T09:20:00Z', now)).toBe(1);
    expect(hoursLeft('2026-10-19T09:00:00Z', now)).toBe(0);
    expect(hoursLeft('not a date', now)).toBe(0);
  });
});
