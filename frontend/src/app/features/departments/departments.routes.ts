import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Phòng ban',
    loadComponent: () =>
      import('./department-list.component').then((m) => m.DepartmentListComponent),
  },
];
