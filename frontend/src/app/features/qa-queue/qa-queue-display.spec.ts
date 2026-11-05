import { HttpErrorResponse } from '@angular/common/http';
import { daysUntil, isKind, qaErrorKey } from './qa-queue-display';

describe('qa-queue-display', () => {
  const failure = (status: number, errorCode?: string) =>
    new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });

  it('maps the stable error codes to localization keys', () => {
    expect(qaErrorKey(failure(409, 'PERIOD_LOCKED'))).toBe('qaQueue.errors.periodLocked');
    expect(qaErrorKey(failure(400, 'VALIDATION_ERROR'))).toBe('qaQueue.errors.validation');
    expect(qaErrorKey(failure(409, 'CONFLICT'))).toBe('qaQueue.errors.conflict');
    expect(qaErrorKey(failure(409, 'VERIFICATION_INVALID_STATE'))).toBe('qaQueue.errors.conflict');
    expect(qaErrorKey(failure(403, 'FORBIDDEN'))).toBe('qaQueue.errors.forbidden');
  });

  it('falls back to the HTTP status, then to a generic message', () => {
    expect(qaErrorKey(failure(403))).toBe('qaQueue.errors.forbidden');
    expect(qaErrorKey(failure(500))).toBe('qaQueue.errors.generic');
    expect(qaErrorKey(new Error('boom'))).toBe('qaQueue.errors.generic');
  });

  it('counts whole days left, with zero on the due day and negatives once overdue', () => {
    const now = new Date('2026-10-25T10:00:00Z');
    expect(daysUntil('2026-10-30T10:00:00Z', now)).toBe(5);
    expect(daysUntil('2026-10-25T20:00:00Z', now)).toBe(1);
    expect(daysUntil('2026-10-25T10:00:00Z', now)).toBe(0);
    expect(daysUntil('2026-10-22T10:00:00Z', now)).toBe(-3);
  });

  it('recognises the two item kinds', () => {
    expect(isKind('FLAG')).toBeTrue();
    expect(isKind('SAMPLE')).toBeTrue();
    expect(isKind('flag')).toBeFalse();
    expect(isKind(null)).toBeFalse();
  });
});
