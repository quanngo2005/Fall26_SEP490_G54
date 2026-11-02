import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * Permission code that opens the branch evaluation period list (SCR-EVL-03). The API needs `evaluation:read` at
 * Branch scope, but the user's permission codes carry no scope and every employee holds `EVALUATION_READ` (Own).
 * `EVALUATION_FINALIZE` (Branch) is the code only branch-level approvers hold, and it is what the screen's
 * lock action needs. The API still enforces the real scope; this guard only avoids rendering a page whose
 * requests would be rejected.
 */
export const BRANCH_EVALUATION_PERMISSION = 'EVALUATION_FINALIZE';

export function canViewBranchEvaluations(
  permissions: readonly string[] | null | undefined,
): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === BRANCH_EVALUATION_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });
}

/** Sends everybody else to /home, like `organizationGuard`. Compose after `authGuard`. */
export const branchEvaluationGuard: CanActivateFn = () =>
  canViewBranchEvaluations(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
