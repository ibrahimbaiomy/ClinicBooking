import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { permissionGuard } from '../../core/auth/auth.guard';
import { Permissions } from '../../core/auth/permissions';
import { ClinicForm } from './form/clinic-form';
import { ClinicsList } from './list/clinics-list';
import { CLINICS_SCOPE, clinicsScopeResolver } from './clinics.scope';

/**
 * Lazy routes of the feature (the parent adds authGuard). Reading is open to any signed-in user
 * (D55); the form pages need clinics.manage. Guards are UX: the API enforces permissions.
 */
export const CLINICS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(CLINICS_SCOPE)],
    resolve: { scope: clinicsScopeResolver },
    children: [
      { path: '', pathMatch: 'full', component: ClinicsList },
      {
        path: 'new',
        canActivate: [permissionGuard(Permissions.ClinicsManage)],
        component: ClinicForm,
      },
      {
        path: ':id/edit',
        canActivate: [permissionGuard(Permissions.ClinicsManage)],
        component: ClinicForm,
      },
    ],
  },
];
