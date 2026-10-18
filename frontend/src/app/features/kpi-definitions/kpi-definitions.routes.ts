import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'KPI Definitions - Cấu hình đo lường hiệu suất',
    loadComponent: () =>
      import('./kpi-definitions.component').then((m) => m.KpiDefinitionsComponent),
  },
];
