import { Routes } from '@angular/router';
import { Home } from './shell/home';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: Home },
  // Deep links such as /specialties are served index.html by the API (D15); until their screens
  // exist they show the shell.
  { path: '**', redirectTo: '' },
];
