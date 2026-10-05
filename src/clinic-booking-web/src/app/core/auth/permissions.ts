/**
 * Permission names, mirroring Domain/Permissions/Permissions.cs (D34, D57). The API sends the user's
 * permissions as strings (GET /api/auth/me); these constants only name the ones the UI asks about, and
 * `npm run check:permissions` fails when one is not a name the API defines, or sits in the wrong list.
 * A wrong name fails closed: the control stays hidden.
 *
 * The assignment screens do not use these: they read the full lists from GET /api/permissions, so a
 * name is never hard-coded twice (D59).
 */

/** Global permissions (`Permissions.Global`): held once, for the whole system. */
export const Permissions = {
  UsersManage: 'users.manage',
  SpecialtiesManage: 'specialties.manage',
  ClinicsManage: 'clinics.manage',
} as const;

/** Clinic-scoped permissions (`Permissions.ClinicScoped`): granted per clinic; ask with `canIn`. */
export const ClinicPermissions = {
  DoctorsManage: 'doctors.manage',
} as const;
