import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { BRANCH_EVALUATION_PERMISSION, BRANCH_EVALUATION_ROUTE } from './branch-evaluation.guard';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';
import { hasRole, ROLE, RoleCode } from './role-codes';

/** Branch managers and system administrators use the branch screens, not the department evaluation overview. */
export const FORBIDDEN_DEPARTMENT_EVALUATION_ROLES: readonly RoleCode[] = [
  ROLE.branchManager,
  ROLE.systemAdmin,
];

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

  const normPerms = (permissions ?? []).map(normalizePermission);
  if (
    normPerms.includes(BRANCH_EVALUATION_PERMISSION) ||
    normPerms.includes(FULL_ACCESS_PERMISSION)
  ) {
    return false;
  }

  if (hasRole(roles, ROLE.deptManager)) {
    return true;
  }

  return normPerms.some((p) => DEPARTMENT_EVALUATION_PERMISSIONS.includes(p));
}

/**
 * Protects `/evaluations` (Department Performance & Evaluation Overview).
 * Only Department Managers are permitted to open this screen; Branch Managers and Admins are redirected.
 */
export const departmentEvaluationGuard: CanActivateFn = () => {
  const user = inject(AuthService).currentUser();
  if (canViewDepartmentEvaluation(user?.roles, user?.permissions)) {
    return true;
  }

  const router = inject(Router);
  const normPerms = (user?.permissions ?? []).map(normalizePermission);
  if (
    hasRole(user?.roles, ROLE.branchManager) ||
    normPerms.includes(BRANCH_EVALUATION_PERMISSION)
  ) {
    return router.parseUrl(BRANCH_EVALUATION_ROUTE);
  }

  return router.parseUrl('/home');
};
