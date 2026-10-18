import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Review & Verify',
    loadComponent: () => import('./review-verify.component').then((m) => m.ReviewVerifyComponent),
  },
  {
    path: ':submissionId',
    title: 'Review & Verify',
    loadComponent: () => import('./review-verify.component').then((m) => m.ReviewVerifyComponent),
  },
];
