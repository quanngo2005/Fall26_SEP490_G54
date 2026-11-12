import { HttpErrorResponse } from '@angular/common/http';
import { appealCode, appealErrorKey, appealSubject, openBlob } from './appeal-display';

const failure = (status: number, errorCode?: string) =>
  new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });

describe('appeal display helpers', () => {
  const cases: [string, string][] = [
    ['APPEAL_WINDOW_EXPIRED', 'appeals.errors.windowExpired'],
    ['APPEAL_ALREADY_OPEN', 'appeals.errors.alreadyOpen'],
    ['APPEAL_INVALID_STATE', 'appeals.errors.invalidState'],
    ['PERIOD_LOCKED', 'appeals.errors.periodLocked'],
    ['MAKER_CANNOT_BE_CHECKER', 'appeals.errors.makerChecker'],
    ['KPI_TARGET_NOT_ASSIGNED', 'appeals.errors.targetNotAssigned'],
    ['CONFLICT', 'appeals.errors.conflict'],
    ['VALIDATION_ERROR', 'appeals.errors.validation'],
    ['NOT_FOUND', 'appeals.errors.notFound'],
  ];
  for (const [code, key] of cases) {
    it(`maps ${code} to ${key}`, () => expect(appealErrorKey(failure(409, code))).toBe(key));
  }

  it('falls back on the HTTP status when there is no error code', () => {
    expect(appealErrorKey(failure(403))).toBe('appeals.errors.forbidden');
    expect(appealErrorKey(failure(404))).toBe('appeals.errors.notFound');
    expect(appealErrorKey(failure(500))).toBe('appeals.errors.generic');
    expect(appealErrorKey(new Error('boom'))).toBe('appeals.errors.generic');
  });

  it('shortens an id to its first block', () => {
    expect(appealCode('3f2a9c10-1111-2222-3333-444455556666')).toBe('3F2A9C10');
  });

  it('describes the subject as the work code and the submission number', () => {
    expect(appealSubject({ workCode: 'WRK-1', submissionNo: 2 })).toBe('WRK-1 \u00B7 v2');
  });

  it('opens a blob in a new tab without an opener and releases the URL later', () => {
    jasmine.clock().install();
    try {
      const open = spyOn(window, 'open');
      const create = spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
      const revoke = spyOn(URL, 'revokeObjectURL');
      openBlob(new Blob(['a']));
      expect(create).toHaveBeenCalled();
      expect(open).toHaveBeenCalledWith('blob:x', '_blank', 'noopener,noreferrer');
      expect(revoke).not.toHaveBeenCalled();
      jasmine.clock().tick(60_001);
      expect(revoke).toHaveBeenCalledWith('blob:x');
    } finally {
      jasmine.clock().uninstall();
    }
  });
});
