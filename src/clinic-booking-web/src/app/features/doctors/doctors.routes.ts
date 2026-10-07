import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { clinicPermissionInAnyGuard } from '../../core/auth/auth.guard';
import { ClinicPermissions } from '../../core/auth/permissions';
import { DoctorDetail } from './detail/doctor-detail';
import { DoctorForm } from './form/doctor-form';
import { DoctorsList } from './list/doctors-list';
import { DOCTORS_SCOPE, doctorsScopeResolver } from './doctors.scope';

/**
 * Lazy routes of the feature (the parent adds authGuard). Reading is open to any signed-in user (D57).
 * Guards and hidden controls are UX only: the API decides (D61, D62).
 */
export const DOCTORS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(DOCTORS_SCOPE)],
    resolve: { scope: doctorsScopeResolver },
    children: [
      { path: '', pathMatch: 'full', component: DoctorsList },
      {
        path: 'new',
        canActivate: [clinicPermissionInAnyGuard(ClinicPermissions.DoctorsManage)],
        component: DoctorForm,
      },
      { path: ':id', component: DoctorDetail },
      // Editing is allowed after loading, by canIn on the doctor's clinics (D62): no route guard can know them.
      { path: ':id/edit', component: DoctorForm },
    ],
  },
];
