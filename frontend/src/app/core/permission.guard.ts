import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * Opens a route only to users that hold one of `codes` (or the development wildcard). Everybody else goes to `/home`
 * instead of a page whose requests the API would reject with 403. The API still enforces the real permission and scope.
 * Compose after `authGuard`.
 */
export function permissionGuard(...codes: readonly string[]): CanActivateFn {
  const wanted = new Set(codes.map(normalizePermission));
  return () => {
    const held = (inject(AuthService).currentUser()?.permissions ?? []).map(normalizePermission);
    return (
      held.some((code) => wanted.has(code) || code === FULL_ACCESS_PERMISSION) ||
      inject(Router).parseUrl('/home')
    );
  };
}
