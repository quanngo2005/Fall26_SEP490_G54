import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return (
    auth
      .currentUser()
      ?.roles.some((role) => ['ADMIN', 'SYSTEM_ADMIN'].includes(role.trim().toUpperCase())) ||
    inject(Router).parseUrl('/home')
  );
};
