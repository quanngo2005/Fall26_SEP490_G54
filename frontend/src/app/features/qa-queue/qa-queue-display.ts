import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { QaKind } from '../../core/qa-queue.models';

const DAY_MS = 24 * 60 * 60 * 1000;

/** Maps the stable `errorCode` (or HTTP status) of a failed QA call to a key under `qaQueue.errors`. */
export function qaErrorKey(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
  const code =
    body && typeof body === 'object' && 'errorCode' in body
      ? (body as { errorCode?: unknown }).errorCode
      : null;

  switch (code) {
    case 'PERIOD_LOCKED':
      return 'qaQueue.errors.periodLocked';
    case 'VALIDATION_ERROR':
      return 'qaQueue.errors.validation';
    case 'CONFLICT':
    case 'VERIFICATION_INVALID_STATE':
      return 'qaQueue.errors.conflict';
    case 'FORBIDDEN':
      return 'qaQueue.errors.forbidden';
    default:
      return status === 403 ? 'qaQueue.errors.forbidden' : 'qaQueue.errors.generic';
  }
}

/** Whole days between now and the due date: positive while there is time left, negative once it is overdue. */
export function daysUntil(dueAt: string, now: Date = new Date()): number {
  return Math.ceil((new Date(dueAt).getTime() - now.getTime()) / DAY_MS);
}

export function isKind(value: string | null): value is QaKind {
  return value === 'FLAG' || value === 'SAMPLE';
}

/** The items of the list the user came from, so the detail screen can step to the previous / next one. */
@Injectable({ providedIn: 'root' })
export class QaQueueContext {
  readonly items = signal<readonly { kind: QaKind; id: string }[]>([]);
}
