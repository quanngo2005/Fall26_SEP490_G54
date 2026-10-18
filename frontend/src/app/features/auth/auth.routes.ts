import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login-page.component').then((m) => m.LoginPageComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
];
