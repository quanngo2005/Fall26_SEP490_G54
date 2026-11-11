import { MatchStatus, SubmissionStatusCode } from '../../core/my-submission.models';

export type BadgeKey =
  'matched' | 'notMatched' | 'inReview' | 'manual' | 'approved' | 'rejected' | 'withdrawn';

export type BadgeTone = 'green' | 'red' | 'gray' | 'amber';

const TONES: Readonly<Record<BadgeKey, BadgeTone>> = {
  matched: 'green',
  approved: 'green',
  notMatched: 'red',
  rejected: 'red',
  inReview: 'gray',
  withdrawn: 'gray',
  manual: 'amber',
};

/** The badge of a submission: its decision wins over the reconciliation, which only matters while it is open. */
export function badgeOf(
  status: SubmissionStatusCode,
  matchStatus: MatchStatus,
): { key: BadgeKey; tone: BadgeTone } {
  const key = badgeKey(status, matchStatus);
  return { key, tone: TONES[key] };
}

function badgeKey(status: SubmissionStatusCode, matchStatus: MatchStatus): BadgeKey {
  if (status === 'WITHDRAWN') return 'withdrawn';
  if (status === 'REJECTED') return 'rejected';
  if (status === 'APPROVED') return 'approved';
  switch (matchStatus) {
    case 'MATCHED':
      return 'matched';
    case 'NOT_MATCHED':
      return 'notMatched';
    case 'IN_REVIEW':
      return 'inReview';
    default:
      return 'manual';
  }
}

/** Whole hours left until the deadline, rounded up so "0 hours" never shows while time remains. */
export function hoursLeft(deadline: string, now: Date = new Date()): number {
  const ms = new Date(deadline).getTime() - now.getTime();
  return Number.isNaN(ms) ? 0 : Math.max(0, Math.ceil(ms / 3_600_000));
}

/** Error codes the server may put in `reason`; anything else is shown as no explanation. */
export const KNOWN_REASONS: readonly string[] = [
  'EMPLOYEE_MISMATCH',
  'PRODUCT_MISMATCH',
  'KPI_TARGET_NOT_ASSIGNED',
  'KPI_AGGREGATION_NOT_SUPPORTED',
];
