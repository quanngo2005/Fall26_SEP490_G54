import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { hasRole, ROLE } from './role-codes';

export const adminGuard: CanActivateFn = () =>
  hasRole(inject(AuthService).currentUser()?.roles, ROLE.systemAdmin) ||
  inject(Router).parseUrl('/home');
