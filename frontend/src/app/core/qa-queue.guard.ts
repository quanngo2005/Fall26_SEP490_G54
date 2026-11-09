import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * Permission code that opens the QA queue (SCR-REV-06). The migration `AddQualityAssurance` grants it to the QA role and
 * the administrators. The API enforces the same permission; this guard only avoids rendering a page whose requests would
 * be rejected.
 */
export const QA_QUEUE_PERMISSION = 'QUALITY_FLAG_READ';

export function canViewQaQueue(permissions: readonly string[] | null | undefined): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === QA_QUEUE_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });
}

/** Sends everybody else to /home. Compose after `authGuard`. */
export const qaQueueGuard: CanActivateFn = () =>
  canViewQaQueue(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
