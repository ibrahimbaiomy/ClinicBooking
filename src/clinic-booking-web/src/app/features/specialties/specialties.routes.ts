import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { permissionGuard } from '../../core/auth/auth.guard';
import { Permissions } from '../../core/auth/permissions';
import { SpecialtyForm } from './form/specialty-form';
import { SpecialtiesList } from './list/specialties-list';
import { SPECIALTIES_SCOPE, specialtiesScopeResolver } from './specialties.scope';

/**
 * Lazy routes of the feature (the parent adds authGuard). Reading is open to any signed-in user
 * (D50); the form pages need specialties.manage. Guards are UX: the API enforces permissions.
 */
export const SPECIALTIES_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(SPECIALTIES_SCOPE)],
    resolve: { scope: specialtiesScopeResolver },
    children: [
      { path: '', pathMatch: 'full', component: SpecialtiesList },
      {
        path: 'new',
        canActivate: [permissionGuard(Permissions.SpecialtiesManage)],
        component: SpecialtyForm,
      },
      {
        path: ':id/edit',
        canActivate: [permissionGuard(Permissions.SpecialtiesManage)],
        component: SpecialtyForm,
      },
    ],
  },
];
