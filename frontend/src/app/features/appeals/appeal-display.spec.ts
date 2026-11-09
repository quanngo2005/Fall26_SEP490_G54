import { HttpErrorResponse } from '@angular/common/http';
import {
  appealCode,
  appealErrorKey,
  appealSubject,
  appealSubjectWithScore,
  openBlob,
  typeKey,
} from './appeal-display';

const failure = (status: number, errorCode?: string) =>
  new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });

describe('appeal display helpers', () => {
  const cases: [string, string][] = [
    ['APPEAL_WINDOW_EXPIRED', 'appeals.errors.windowExpired'],
    ['APPEAL_SCORE_WINDOW_EXPIRED', 'appeals.errors.scoreWindowExpired'],
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

  it('describes the subject of each kind of appeal and never invents one', () => {
    expect(
      appealSubject({ type: 'RESULT', workCode: 'WRK-1', submissionNo: 2, periodCode: null }),
    ).toBe('WRK-1 · v2');
    expect(
      appealSubject({ type: 'RESULT', workCode: 'WRK-1', submissionNo: null, periodCode: null }),
    ).toBe('WRK-1');
    expect(
      appealSubject({ type: 'RESULT', workCode: null, submissionNo: null, periodCode: null }),
    ).toBeNull();
    expect(
      appealSubject({ type: 'SCORE', workCode: null, submissionNo: null, periodCode: '2026-Q2' }),
    ).toBe('2026-Q2');
    expect(
      appealSubject({ type: 'SCORE', workCode: null, submissionNo: null, periodCode: null }),
    ).toBeNull();
  });

  it('adds the formatted score to a score appeal only when the score is known', () => {
    const format = (score: number) => `<${score}>`;
    const score = (evaluationScore: number | null, periodCode: string | null = '2026-Q2') => ({
      type: 'SCORE' as const,
      workCode: null,
      submissionNo: null,
      periodCode,
      evaluationScore,
    });
    expect(appealSubjectWithScore(score(87.2), format)).toBe('2026-Q2 · <87.2>');
    expect(appealSubjectWithScore(score(0), format)).toBe('2026-Q2 · <0>');
    expect(appealSubjectWithScore(score(null), format)).toBe('2026-Q2');
    expect(appealSubjectWithScore(score(87.2, null), format)).toBeNull();
    expect(
      appealSubjectWithScore(
        {
          type: 'RESULT',
          workCode: 'WRK-1',
          submissionNo: 2,
          periodCode: null,
          evaluationScore: 5,
        },
        format,
      ),
    ).toBe('WRK-1 · v2');
  });

  it('picks the label key of a kind', () => {
    expect(typeKey('SCORE')).toBe('scoreAppeal');
    expect(typeKey('RESULT')).toBe('resultAppeal');
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
