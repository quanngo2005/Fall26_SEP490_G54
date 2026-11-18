import { HttpErrorResponse } from '@angular/common/http';

export const MAX_FILE_BYTES = 25 * 1024 * 1024;
export const ALLOWED_FILE_EXTENSIONS = ['.pdf', '.docx', '.xlsx', '.png', '.jpg', '.jpeg'];
export const MIN_REVERSAL_REASON = 10;

export type RiskErrorScope = 'create' | 'reverse' | 'details';

function bodyOf(
  error: unknown,
): { errorCode?: unknown; message?: unknown; errors?: unknown } | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  return error.error && typeof error.error === 'object' ? error.error : null;
}

/** Maps a failed risk event call to a key under `riskEvents.errors`, using the stable `errorCode` first. */
export function riskErrorKey(error: unknown, scope: RiskErrorScope): string {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  const body = bodyOf(error);
  const code = body?.errorCode;
  const texts = [body?.message, ...(Array.isArray(body?.errors) ? body.errors : [])]
    .filter((value): value is string => typeof value === 'string')
    .join(' ');

  switch (code) {
    case 'RISK_EVENT_PERIOD_UNAVAILABLE':
    case 'PERIOD_LOCKED':
      return 'riskEvents.errors.periodUnavailable';
    case 'RISK_EVENT_INVALID_STATE':
      return 'riskEvents.errors.invalidState';
    case 'VALIDATION_ERROR':
      return scope === 'create' && /workCode|công việc/i.test(texts)
        ? 'riskEvents.errors.workNotFound'
        : 'riskEvents.errors.validation';
    case 'NOT_FOUND':
      return scope === 'create' ? 'riskEvents.errors.workNotFound' : 'riskEvents.errors.generic';
    case 'FORBIDDEN':
      return /chính mình/i.test(texts)
        ? 'riskEvents.errors.selfEvent'
        : 'riskEvents.errors.forbidden';
    default:
      return status === 403 ? 'riskEvents.errors.forbidden' : 'riskEvents.errors.generic';
  }
}

export function fileExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
}

/** The `yyyy-MM-dd` of today in the browser's calendar, the largest date the form accepts. */
export function todayInput(now: Date = new Date()): string {
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

/** The violation date as the API wants it: the start of that calendar day in Vietnam (UTC+7). */
export function occurredAtOf(dateInput: string): string {
  return `${dateInput}T00:00:00+07:00`;
}
