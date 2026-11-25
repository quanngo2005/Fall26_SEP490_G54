import { HttpErrorResponse } from '@angular/common/http';
import { CampaignStatus } from '../../core/campaign.models';
import { ContactStatus } from '../../core/campaign-customer.models';

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

const CONTACT_BADGE: Record<ContactStatus, string> = {
  NOT_CONTACTED: 'is-muted',
  CALLED: 'is-info',
  APPOINTMENT_SET: 'is-warning',
  MET: '',
  CLOSED: '',
};

export function contactBadgeClass(status: ContactStatus): string {
  return CONTACT_BADGE[status] ?? 'is-muted';
}

/** The API's machine-readable `errorCode` for a failed call, when it sent one. */
export function apiErrorCode(error: unknown): string | null {
  const body = error instanceof HttpErrorResponse ? error.error : null;
  const code =
    body && typeof body === 'object' ? (body as { errorCode?: unknown }).errorCode : null;
  return typeof code === 'string' && code ? code : null;
}

/** Fills `{name}` placeholders of a localized template. */
export function fillTemplate(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    key in values ? String(values[key]) : match,
  );
}
