import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';
import { RiskTransition } from './risk-event.models';

/**
 * Permission codes of the risk event screens. `risk_event:read` opens the list (QA, administrators and managers, who only
 * view); `risk_event:create` shows the "record" button. The API enforces the same permissions.
 */
export const RISK_EVENT_READ_PERMISSION = 'RISK_EVENT_READ';
export const RISK_EVENT_CREATE_PERMISSION = 'RISK_EVENT_CREATE';

function holds(permissions: readonly string[] | null | undefined, wanted: string): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === wanted || code === FULL_ACCESS_PERMISSION;
  });
}

export const canViewRiskEvents = (permissions: readonly string[] | null | undefined): boolean =>
  holds(permissions, RISK_EVENT_READ_PERMISSION);

export const canCreateRiskEvents = (permissions: readonly string[] | null | undefined): boolean =>
  holds(permissions, RISK_EVENT_CREATE_PERMISSION);

/** `risk_event:confirm`, `risk_event:dismiss` or `risk_event:resolve` shows the matching button of the drawer. */
export const canTransitionRiskEvents = (
  permissions: readonly string[] | null | undefined,
  transition: RiskTransition,
): boolean => holds(permissions, `RISK_EVENT_${transition.toUpperCase()}`);

/** Sends everybody else to /home. Compose after `authGuard`. */
export const riskEventGuard: CanActivateFn = () =>
  canViewRiskEvents(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
