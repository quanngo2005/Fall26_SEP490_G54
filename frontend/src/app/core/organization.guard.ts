import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Permission code that opens the organization screens (SCR-ORG-01 departments, SCR-ORG-02 job positions and the
 * department performance view). The migration `SeedOrganizationReadAccess` grants it in `role_permission` to the
 * system admin, branch manager and department manager only; `FULL_ACCESS` is the development admin's wildcard.
 * The API enforces the same permission; this guard only avoids rendering a page whose requests would be rejected.
 */
export const ORGANIZATION_READ_PERMISSION = 'ORGANIZATION_READ';
export const FULL_ACCESS_PERMISSION = 'FULL_ACCESS';

/** Same normalisation `NavigationService` uses for permission codes: `organization:read` and `ORGANIZATION_READ` match. */
export function normalizePermission(permission: string | null | undefined): string {
  return (permission ?? '')
    .trim()
    .toUpperCase()
    .replace(/[.:\-\s]+/g, '_');
}

export function canViewOrganization(permissions: readonly string[] | null | undefined): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === ORGANIZATION_READ_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });
}

/** Sends everybody else to /home, like `adminGuard`. Compose after `authGuard`. */
export const organizationGuard: CanActivateFn = () =>
  canViewOrganization(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
