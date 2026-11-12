import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';
import { BRANCH_EVALUATION_PERMISSION } from './branch-evaluation.guard';
import { APPEAL_DECISION_PERMISSION } from './appeal-queue.guard';

/**
 * Roles that manage or approve evaluations at unit/branch level.
 * These actors use dedicated management screens (SCR-EVL-01, SCR-EVL-03) rather
 * than the employee personal evaluation screen (SCR-EVL-04 / UC108 / UC109).
 */
export const MANAGER_ROLES: readonly string[] = [
  'BRANCH_MANAGER',
  'DEPARTMENT_MANAGER',
  'DEPT_MANAGER',
  'MANAGER',
  'ADMIN',
  'SYSTEM_ADMIN',
];

export function canViewPersonalEvaluation(
  roles: readonly string[] | null | undefined,
  permissions: readonly string[] | null | undefined,
): boolean {
  const normRoles = (roles ?? []).map((r) =>
    r
      .trim()
      .toUpperCase()
      .replace(/[.:\-\s]+/g, '_'),
  );
  if (normRoles.some((r) => MANAGER_ROLES.includes(r))) {
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
 * Directs managers/admins to `/evaluations`. Compose after `authGuard`.
 */
export const personalEvaluationGuard: CanActivateFn = () => {
  const user = inject(AuthService).currentUser();
  return (
    canViewPersonalEvaluation(user?.roles, user?.permissions) ||
    inject(Router).parseUrl('/evaluations')
  );
};
