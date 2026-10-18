import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Hồ sơ Cán bộ',
    loadComponent: () =>
      import('./employee-profile.component').then((m) => m.EmployeeProfileComponent),
  },
];
