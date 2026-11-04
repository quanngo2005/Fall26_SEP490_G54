import { inject } from '@angular/core';
import { CanActivateFn, RedirectFunction, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * Permission code that lets a user decide the appeals of other employees. The API shows such a user the appeals of
 * the department they manage (`scope=TEAM`) and rejects everybody else, so the queue screens are only rendered for
 * holders of this code. The API still enforces the real scope.
 */
export const APPEAL_DECISION_PERMISSION = 'VERIFICATION_DECIDE';

export function canDecideAppeals(permissions: readonly string[] | null | undefined): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === APPEAL_DECISION_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });
}

/** Sends users who cannot decide appeals to their own appeals. Compose after `authGuard`. */
export const appealQueueGuard: CanActivateFn = () =>
  canDecideAppeals(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/appeals/mine');

/**
 * Own appeals belong to employees; whoever can decide appeals works from the queue of the department instead.
 * Compose after `authGuard`.
 */
export const ownAppealsGuard: CanActivateFn = () =>
  !canDecideAppeals(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/appeals/department');

/** `/appeals`: managers land on the queue of their department, everybody else on their own appeals. */
export const appealsHomeRedirect: RedirectFunction = () =>
  inject(Router).parseUrl(
    canDecideAppeals(inject(AuthService).currentUser()?.permissions)
      ? '/appeals/department'
      : '/appeals/mine',
  );
