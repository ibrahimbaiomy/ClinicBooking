/**
 * Permission names, mirroring Domain/Permissions/Permissions.cs (D34). The API sends the user's
 * permissions as strings (GET /api/auth/me); these constants only name the ones the UI asks about,
 * and `npm run check:permissions` fails when one of them is not a name the API defines.
 * A wrong name fails closed: the control stays hidden.
 */
export const Permissions = {
  UsersManage: 'users.manage',
  SpecialtiesManage: 'specialties.manage',
  ClinicsManage: 'clinics.manage',
} as const;
