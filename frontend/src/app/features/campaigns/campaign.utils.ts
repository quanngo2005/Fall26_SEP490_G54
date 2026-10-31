import { HttpErrorResponse } from '@angular/common/http';
import { CampaignStatus } from '../../core/campaign.models';

const STATUS_BADGE: Record<CampaignStatus, string> = {
  PLANNED: 'is-warning',
  RUNNING: 'is-info',
  PAUSED: 'is-warning',
  COMPLETED: 'is-muted',
  CANCELLED: 'is-danger',
};

export function statusBadgeClass(status: CampaignStatus): string {
  return STATUS_BADGE[status] ?? 'is-muted';
}

/** Share of generated works that are completed, or null when nothing was generated yet. */
export function worksProgress(generated: number, completed: number): number | null {
  return generated > 0 ? Math.round((completed * 100) / generated) : null;
}

/** The API's own (localized) message for a failed call, when it sent one. */
export function serverMessage(error: unknown): string | null {
  const body = error instanceof HttpErrorResponse ? error.error : null;
  const message = body && typeof body === 'object' ? (body as { message?: unknown }).message : null;
  return typeof message === 'string' && message.trim() ? message : null;
}
