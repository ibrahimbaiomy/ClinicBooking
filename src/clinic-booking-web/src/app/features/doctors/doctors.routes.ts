import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { clinicPermissionInAnyGuard } from '../../core/auth/auth.guard';
import { ClinicPermissions } from '../../core/auth/permissions';
import { DoctorClinicsPage } from './clinics/doctor-clinics';
import { DoctorEditPage } from './edit/doctor-edit';
import { DoctorForm } from './form/doctor-form';
import { WorkingHoursPage } from './hours/working-hours';
import { DoctorsList } from './list/doctors-list';
import { DoctorSpecialtiesPage } from './specialties/doctor-specialties';
import { DOCTORS_SCOPE, doctorsScopeResolver } from './doctors.scope';

/**
 * Lazy routes of the feature (the parent adds authGuard). Reading is open to any signed-in user (D57).
 * Guards and hidden controls are UX only: the API decides (D61, D62). There is no detail page (D64): each
 * list row opens Edit, Specialties or Clinics; an old `/doctors/:id` link opens the Clinics page.
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
      { path: ':id', pathMatch: 'full', redirectTo: ':id/clinics' },
      // Edit and Specialties are allowed after loading, by canIn on the doctor's clinics (D62): no route guard
      // can know them.
      { path: ':id/edit', component: DoctorEditPage },
      { path: ':id/specialties', component: DoctorSpecialtiesPage },
      // Read by anyone signed in; each row's controls follow canIn for its clinic (D62, D64).
      { path: ':id/clinics', component: DoctorClinicsPage },
      // Read by anyone signed in; editable with doctors.manage in that clinic, decided on the page (D62).
      { path: ':id/clinics/:clinicId/hours', component: WorkingHoursPage },
    ],
  },
];
