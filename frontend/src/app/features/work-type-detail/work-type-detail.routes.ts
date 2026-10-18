import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Work Type Detail',
    loadComponent: () =>
      import('./work-type-detail.component').then((m) => m.WorkTypeDetailComponent),
  },
];
