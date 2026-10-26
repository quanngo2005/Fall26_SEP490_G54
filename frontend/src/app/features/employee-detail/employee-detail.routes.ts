import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Employee Detail',
    loadComponent: () =>
      import('./employee-detail.component').then((m) => m.EmployeeDetailComponent),
  },
  {
    path: ':code',
    title: 'Employee Detail',
    loadComponent: () =>
      import('./employee-detail.component').then((m) => m.EmployeeDetailComponent),
  },
];
