import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Thẩm định kết quả công việc',
    loadComponent: () => import('./review-verify.component').then((m) => m.ReviewVerifyComponent),
  },
  {
    path: ':submissionId',
    title: 'Thẩm định kết quả công việc',
    loadComponent: () => import('./review-verify.component').then((m) => m.ReviewVerifyComponent),
  },
];
