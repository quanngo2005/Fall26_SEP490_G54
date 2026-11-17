import { HttpErrorResponse } from '@angular/common/http';
import { StatusBadgeTone } from '../../shared/ui';
import { ReconciliationStatus } from '../../core/reconciliation.models';

/** The tone of the status badge: only a mismatch stands out, a waiting code is a soft warning. */
export function statusTone(status: ReconciliationStatus): StatusBadgeTone {
  switch (status) {
    case 'NOT_MATCHED':
      return 'danger';
    case 'IN_REVIEW':
      return 'warning';
    default:
      return 'success';
  }
}

/** Whole hours left until the deadline (never negative). */
export function hoursLeft(deadline: string, now: number = Date.now()): number {
  return Math.max(0, Math.ceil((Date.parse(deadline) - now) / 3_600_000));
}

/** The text key of a failed load or action, chosen by the status the API answered with. */
export function reconciliationErrorKey(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  if (status === 403) return 'reconciliation.errors.forbidden';
  if (status === 404) return 'reconciliation.errors.notFound';
  return 'reconciliation.errors.generic';
}

/** What the list screen remembers between visits ("Lưu bộ lọc"). */
export interface SavedReconciliationFilters {
  status: string;
  search: string;
  period: string;
  from: string;
  to: string;
  department: string;
  jobPosition: string;
  product: string;
  reason: string;
}

export const SAVED_FILTERS_KEY = 'g54.reconciliation.filters';

export function readSavedFilters(): SavedReconciliationFilters | null {
  try {
    const raw = localStorage.getItem(SAVED_FILTERS_KEY);
    return raw ? (JSON.parse(raw) as SavedReconciliationFilters) : null;
  } catch {
    return null;
  }
}

export function writeSavedFilters(filters: SavedReconciliationFilters | null): boolean {
  try {
    if (filters) localStorage.setItem(SAVED_FILTERS_KEY, JSON.stringify(filters));
    else localStorage.removeItem(SAVED_FILTERS_KEY);
    return true;
  } catch {
    return false;
  }
}
