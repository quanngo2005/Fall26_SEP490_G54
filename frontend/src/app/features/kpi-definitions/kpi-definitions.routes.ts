import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'KPI Definitions',
    loadComponent: () =>
      import('./kpi-definitions.component').then((m) => m.KpiDefinitionsComponent),
  },
];
