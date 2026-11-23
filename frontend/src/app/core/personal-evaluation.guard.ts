import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';
import { BRANCH_EVALUATION_PERMISSION, BRANCH_EVALUATION_ROUTE } from './branch-evaluation.guard';
import { APPEAL_DECISION_PERMISSION } from './appeal-queue.guard';
import { hasRole, ROLE, RoleCode } from './role-codes';

/**
 * Roles that manage or approve evaluations at unit/branch level.
 * These actors use dedicated management screens (SCR-EVL-01, SCR-EVL-03) rather
 * than the employee personal evaluation screen (SCR-EVL-04 / UC108 / UC109).
 */
export const MANAGER_ROLES: readonly RoleCode[] = [
  ROLE.branchManager,
  ROLE.deptManager,
  ROLE.systemAdmin,
];

export function canViewPersonalEvaluation(
  roles: readonly string[] | null | undefined,
  permissions: readonly string[] | null | undefined,
): boolean {
  if (hasRole(roles, ...MANAGER_ROLES)) {
    return false;
  }
  const normPerms = (permissions ?? []).map(normalizePermission);
  if (
    normPerms.includes(BRANCH_EVALUATION_PERMISSION) ||
    normPerms.includes(APPEAL_DECISION_PERMISSION) ||
    normPerms.includes(FULL_ACCESS_PERMISSION)
  ) {
    return false;
  }
  return true;
}

/**
 * Protects SCR-EVL-04 (Personal Evaluation Result).
 * Directs department managers to `/evaluations`, branch managers to `/branch-performance/evaluations`,
 * and admins to `/home`. Compose after `authGuard`.
 */
export const personalEvaluationGuard: CanActivateFn = () => {
  const user = inject(AuthService).currentUser();
  if (canViewPersonalEvaluation(user?.roles, user?.permissions)) {
    return true;
  }

  const router = inject(Router);
  const normPerms = (user?.permissions ?? []).map(normalizePermission);

  if (hasRole(user?.roles, ROLE.systemAdmin) || normPerms.includes(FULL_ACCESS_PERMISSION)) {
    return router.parseUrl('/home');
  }

  if (
    hasRole(user?.roles, ROLE.branchManager) ||
    normPerms.includes(BRANCH_EVALUATION_PERMISSION)
  ) {
    return router.parseUrl(BRANCH_EVALUATION_ROUTE);
  }

  return router.parseUrl('/evaluations');
};
