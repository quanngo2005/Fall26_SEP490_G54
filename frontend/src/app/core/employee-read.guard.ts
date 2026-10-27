import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

export const EMPLOYEE_READ_PERMISSION = 'EMPLOYEE_READ';

export const employeeReadGuard: CanActivateFn = () => {
  const permissions = inject(AuthService).currentUser()?.permissions ?? [];
  return (
    permissions.some((permission) => {
      const code = normalizePermission(permission);
      return code === EMPLOYEE_READ_PERMISSION || code === FULL_ACCESS_PERMISSION;
    }) || inject(Router).parseUrl('/home')
  );
};
