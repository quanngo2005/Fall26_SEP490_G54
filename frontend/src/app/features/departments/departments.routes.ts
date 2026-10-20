import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Department View',
    loadComponent: () =>
      import('./department-view.component').then((m) => m.DepartmentViewComponent),
  },
];
