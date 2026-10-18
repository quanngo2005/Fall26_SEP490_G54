import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Chi tiết nhân viên',
    loadComponent: () =>
      import('./employee-detail.component').then((m) => m.EmployeeDetailComponent),
  },
  {
    path: ':id',
    title: 'Chi tiết nhân viên',
    loadComponent: () =>
      import('./employee-detail.component').then((m) => m.EmployeeDetailComponent),
  },
];
