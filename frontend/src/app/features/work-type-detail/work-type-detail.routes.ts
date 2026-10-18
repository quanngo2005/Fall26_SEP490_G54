import { Routes } from '@angular/router';
import { WorkTypeDetailComponent } from './work-type-detail.component';

export const routes: Routes = [
  {
    path: '',
    title: 'Work Type Detail',
    component: WorkTypeDetailComponent,
  },
  {
    path: ':id',
    title: 'Work Type Detail',
    component: WorkTypeDetailComponent,
  },
];

export default routes;
