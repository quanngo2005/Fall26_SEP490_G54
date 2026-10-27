import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Role codes allowed to open the organization screens (SCR-ORG-01 departments, SCR-ORG-02 job positions and the
 * department performance view): system admin, branch manager and department manager. Mirrors
 * `G54.BLL.Common.Constants.RoleCodes.OrganizationViewers`; the API enforces the same rule, this only avoids
 * rendering a page whose requests would be rejected with 403.
 */
export const ORGANIZATION_VIEWER_ROLES: ReadonlySet<string> = new Set([
  'SYSTEM_ADMIN',
  'ADMIN',
  'BRANCH_MANAGER',
  'DEPT_MANAGER',
  'DEPARTMENT_MANAGER',
  'MANAGER',
]);

/** Same normalisation as the backend: trim, upper-case, spaces/hyphens become underscores. */
export function normalizeRole(role: string | null | undefined): string {
  return (role ?? '')
    .trim()
    .toUpperCase()
    .replace(/[\s-]+/g, '_');
}

export function canViewOrganization(roles: readonly string[] | null | undefined): boolean {
  return (roles ?? []).some((role) => ORGANIZATION_VIEWER_ROLES.has(normalizeRole(role)));
}

/** Sends everybody else to /home, like `adminGuard`. Compose after `authGuard`. */
export const organizationGuard: CanActivateFn = () =>
  canViewOrganization(inject(AuthService).currentUser()?.roles) || inject(Router).parseUrl('/home');
