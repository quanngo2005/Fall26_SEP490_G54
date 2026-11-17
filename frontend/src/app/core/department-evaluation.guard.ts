import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { BRANCH_EVALUATION_PERMISSION } from './branch-evaluation.guard';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

export const DEPARTMENT_MANAGER_ROLES: readonly string[] = [
  'DEPARTMENT_MANAGER',
  'DEPT_MANAGER',
  'MANAGER',
];

export const FORBIDDEN_DEPARTMENT_EVALUATION_ROLES: readonly string[] = [
  'BRANCH_MANAGER',
  'ADMIN',
  'SYSTEM_ADMIN',
];

export const DEPARTMENT_EVALUATION_PERMISSIONS: readonly string[] = [
  'EVALUATION_SUBMIT',
  'EVALUATION_CALIBRATE',
];

export function canViewDepartmentEvaluation(
  roles: readonly string[] | null | undefined,
  permissions: readonly string[] | null | undefined,
): boolean {
  const normRoles = (roles ?? []).map((r) =>
    r
      .trim()
      .toUpperCase()
      .replace(/[.:\-\s]+/g, '_'),
  );
  if (normRoles.some((r) => FORBIDDEN_DEPARTMENT_EVALUATION_ROLES.includes(r))) {
    return false;
  }

  const normPerms = (permissions ?? []).map(normalizePermission);
  if (
    normPerms.includes(BRANCH_EVALUATION_PERMISSION) ||
    normPerms.includes(FULL_ACCESS_PERMISSION)
  ) {
    return false;
  }

  if (normRoles.some((r) => DEPARTMENT_MANAGER_ROLES.includes(r))) {
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
  const normRoles = (user?.roles ?? []).map((r) =>
    r
      .trim()
      .toUpperCase()
      .replace(/[.:\-\s]+/g, '_'),
  );
  const normPerms = (user?.permissions ?? []).map(normalizePermission);
  if (normRoles.includes('BRANCH_MANAGER') || normPerms.includes(BRANCH_EVALUATION_PERMISSION)) {
    return router.parseUrl('/evaluations/branch-periods');
  }

  return router.parseUrl('/home');
};
