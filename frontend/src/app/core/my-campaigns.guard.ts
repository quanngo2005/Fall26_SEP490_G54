import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { canUpdateMyCampaigns } from './campaign-permissions';

/** Sends users without `campaign_customer:update` to /home. Compose after `authGuard`. */
export const myCampaignsGuard: CanActivateFn = () =>
  canUpdateMyCampaigns(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
