import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { permissionGuard } from '../../core/auth/auth.guard';
import { Permissions } from '../../core/auth/permissions';
import { PatientForm } from './form/patient-form';
import { PatientsList } from './list/patients-list';
import { PATIENTS_SCOPE, patientsScopeResolver } from './patients.scope';

/**
 * Lazy routes of the feature (the parent adds authGuard). Every page needs its own global permission, the list
 * included: patients are personal data (D63). Guards are UX: the API enforces permissions.
 */
export const PATIENTS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(PATIENTS_SCOPE)],
    resolve: { scope: patientsScopeResolver },
    children: [
      { path: '', pathMatch: 'full', canActivate: [permissionGuard(Permissions.PatientsRead)], component: PatientsList },
      { path: 'new', canActivate: [permissionGuard(Permissions.PatientsCreate)], component: PatientForm },
      {
        // Editing loads the patient first, which needs patients.read too (D63).
        path: ':id/edit',
        canActivate: [permissionGuard(Permissions.PatientsRead), permissionGuard(Permissions.PatientsEdit)],
        component: PatientForm,
      },
    ],
  },
];
