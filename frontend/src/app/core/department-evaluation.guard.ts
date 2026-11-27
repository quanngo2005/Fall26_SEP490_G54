import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { BRANCH_EVALUATION_PERMISSION, BRANCH_EVALUATION_ROUTE } from './branch-evaluation.guard';
import { normalizePermission } from './organization.guard';
import { hasRole, ROLE, RoleCode } from './role-codes';

/** System administrators use administrative screens, not the evaluation overview. */
export const FORBIDDEN_DEPARTMENT_EVALUATION_ROLES: readonly RoleCode[] = [ROLE.systemAdmin];

export const DEPARTMENT_EVALUATION_PERMISSIONS: readonly string[] = [
  'EVALUATION_SUBMIT',
  'EVALUATION_CALIBRATE',
];

export function canViewDepartmentEvaluation(
  roles: readonly string[] | null | undefined,
  permissions: readonly string[] | null | undefined,
): boolean {
  if (hasRole(roles, ...FORBIDDEN_DEPARTMENT_EVALUATION_ROLES)) {
    return false;
  }

  if (
    hasRole(roles, ROLE.branchManager) ||
    (permissions ?? []).map(normalizePermission).includes(BRANCH_EVALUATION_PERMISSION)
  ) {
    return false;
  }

  if (hasRole(roles, ROLE.deptManager)) {
    return true;
  }

  const normPerms = (permissions ?? []).map(normalizePermission);
  if (normPerms.some((p) => DEPARTMENT_EVALUATION_PERMISSIONS.includes(p))) {
    return true;
  }

  return false;
}

/**
 * Protects `/evaluations` (Performance & Evaluation Overview).
 * Only Department Managers are permitted to open this screen.
 * Branch Managers are redirected to `/branch-performance/evaluations`.
 */
export const departmentEvaluationGuard: CanActivateFn = () => {
  const user = inject(AuthService).currentUser();
  const router = inject(Router);

  if (
    hasRole(user?.roles, ROLE.branchManager) ||
    (user?.permissions ?? []).map(normalizePermission).includes(BRANCH_EVALUATION_PERMISSION)
  ) {
    return router.parseUrl(BRANCH_EVALUATION_ROUTE);
  }

  if (canViewDepartmentEvaluation(user?.roles, user?.permissions)) {
    return true;
  }

  return router.parseUrl('/home');
};
