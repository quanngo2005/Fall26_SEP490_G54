import { evaluationPeriodLabel, parseDate } from './evaluation-profile-period';

const format = (value: string): string => `<${value}>`;

describe('evaluation-profile-period', () => {
  it('reads a date-only value as a UTC date', () => {
    expect(parseDate('2026-10-01T07:00:00+00:00').toISOString()).toBe('2026-10-01T00:00:00.000Z');
  });

  it('labels a whole calendar quarter as Qn/yyyy', () => {
    expect(evaluationPeriodLabel('2026-10-01', '2026-12-31', format, 'open')).toBe('Q4/2026');
    expect(evaluationPeriodLabel('2026-01-01', '2026-03-31', format, 'open')).toBe('Q1/2026');
  });

  it('labels any other range with the formatted dates', () => {
    expect(evaluationPeriodLabel('2026-10-15', '2026-12-31', format, 'open')).toBe(
      '<2026-10-15> – <2026-12-31>',
    );
    expect(evaluationPeriodLabel('2026-10-01', '2026-11-30', format, 'open')).toBe(
      '<2026-10-01> – <2026-11-30>',
    );
  });

  it('uses the open-ended text when there is no end date', () => {
    expect(evaluationPeriodLabel('2026-10-01', null, format, 'open')).toBe('<2026-10-01> – open');
  });
});
