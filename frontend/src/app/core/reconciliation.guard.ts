import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * `reconciliation:read` opens the reconciliation screens (QA, administrators, department and branch managers, each limited to
 * their own scope by the API). The API enforces the same permission.
 */
export const RECONCILIATION_READ_PERMISSION = 'RECONCILIATION_READ';

export const canViewReconciliation = (permissions: readonly string[] | null | undefined): boolean =>
  (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === RECONCILIATION_READ_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });

/** Sends everybody else to /home. Compose after `authGuard`. */
export const reconciliationGuard: CanActivateFn = () =>
  canViewReconciliation(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
