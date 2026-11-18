export const parseDate = (value: string): Date => {
  const [y, m, d] = value.slice(0, 10).split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d));
};

/**
 * Quarter label (Q4/2026) when the range is a whole calendar quarter, otherwise `from - to` formatted with
 * `format`; `openEnded` stands in for a missing end date.
 */
export function evaluationPeriodLabel(
  effectiveFrom: string,
  effectiveTo: string | null,
  format: (value: string) => string,
  openEnded: string,
): string {
  const from = parseDate(effectiveFrom);
  const to = effectiveTo ? parseDate(effectiveTo) : null;
  if (to && from.getUTCDate() === 1 && from.getUTCMonth() % 3 === 0) {
    const quarterEnd = new Date(Date.UTC(from.getUTCFullYear(), from.getUTCMonth() + 3, 0));
    if (quarterEnd.getTime() === to.getTime())
      return `Q${from.getUTCMonth() / 3 + 1}/${from.getUTCFullYear()}`;
  }
  const start = format(effectiveFrom);
  return `${start} – ${effectiveTo ? format(effectiveTo) : openEnded}`;
}
