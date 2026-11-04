import { HttpErrorResponse } from '@angular/common/http';
import { AppealListItem, AppealStatus, AppealType } from '../../core/appeal.models';

/** Maps the stable `errorCode` (or HTTP status) of a failed appeal call to a key under `appeals.errors`. */
export function appealErrorKey(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
  const code =
    body && typeof body === 'object' && 'errorCode' in body
      ? (body as { errorCode?: unknown }).errorCode
      : null;

  switch (code) {
    case 'APPEAL_WINDOW_EXPIRED':
      return 'appeals.errors.windowExpired';
    case 'APPEAL_SCORE_WINDOW_EXPIRED':
      return 'appeals.errors.scoreWindowExpired';
    case 'APPEAL_ALREADY_OPEN':
      return 'appeals.errors.alreadyOpen';
    case 'APPEAL_INVALID_STATE':
      return 'appeals.errors.invalidState';
    case 'PERIOD_LOCKED':
      return 'appeals.errors.periodLocked';
    case 'MAKER_CANNOT_BE_CHECKER':
      return 'appeals.errors.makerChecker';
    case 'KPI_TARGET_NOT_ASSIGNED':
      return 'appeals.errors.targetNotAssigned';
    case 'CONFLICT':
      return 'appeals.errors.conflict';
    case 'VALIDATION_ERROR':
      return 'appeals.errors.validation';
    case 'NOT_FOUND':
      return 'appeals.errors.notFound';
    default:
      if (status === 403) return 'appeals.errors.forbidden';
      if (status === 404) return 'appeals.errors.notFound';
      return 'appeals.errors.generic';
  }
}

/** Short, stable label of an appeal: the first block of its id (the API has no display code). */
export function appealCode(id: string): string {
  return id.split('-')[0].toUpperCase();
}

/** What the appeal is about: the work and submission of a result appeal, the period of a score appeal. */
export function appealSubject(
  item: Pick<AppealListItem, 'type' | 'workCode' | 'submissionNo' | 'periodCode'>,
): string | null {
  if (item.type === 'SCORE') return item.periodCode;
  if (!item.workCode) return null;
  return item.submissionNo == null ? item.workCode : `${item.workCode} · v${item.submissionNo}`;
}

export const RESOLVED_STATUSES: readonly AppealStatus[] = ['ACCEPTED', 'REJECTED', 'WITHDRAWN'];
export const OPEN_STATUSES: readonly AppealStatus[] = ['OPEN', 'UNDER_REVIEW'];

export function typeKey(type: AppealType): 'resultAppeal' | 'scoreAppeal' {
  return type === 'SCORE' ? 'scoreAppeal' : 'resultAppeal';
}

/** Technical, not business: how long the opened tab may keep reading the object URL before it is released. */
const OBJECT_URL_LIFETIME_MS = 60_000;

/** Opens a fetched evidence file in a new tab, then releases the object URL. */
export function openBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener,noreferrer');
  setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_LIFETIME_MS);
}
