import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * Permission code that opens the target setup wizard (SCR-STRAT-04). The API requires `target:manage` at Department
 * scope; this guard only avoids rendering a page whose requests would be rejected.
 */
export const TARGET_MANAGE_PERMISSION = 'TARGET_MANAGE';

export function canManageTargets(permissions: readonly string[] | null | undefined): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === TARGET_MANAGE_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });
}

/** Sends everybody else to /home, like `organizationGuard`. Compose after `authGuard`. */
export const targetManageGuard: CanActivateFn = () =>
  canManageTargets(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
