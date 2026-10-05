import { AssignablePermissions, UserClinicPermissions, UserDetail, UserPage, UserSummary } from '../api/users-api';

export const USERS_URL = '/api/users';
export const PERMISSIONS_URL = '/api/permissions';

export const user = (id: number, userName: string, extras: Partial<UserSummary> = {}): UserSummary => ({
  id,
  userName,
  isActive: true,
  mustChangePassword: false,
  createdAt: '2026-01-15T10:00:00Z',
  ...extras,
});

export const pageOfUsers = (items: UserSummary[], totalCount = items.length, page = 1, pageSize = 20): UserPage => ({
  items,
  page,
  pageSize,
  totalCount,
});

export const clinicGrant = (
  clinicId: number | string,
  clinicNameAr: string,
  clinicNameEn: string,
  permissions: string[],
): UserClinicPermissions => ({ clinicId, clinicNameAr, clinicNameEn, permissions });

export const detail = (
  id: number | string,
  userName: string,
  extras: Partial<UserDetail> = {},
): UserDetail => ({
  id,
  userName,
  isActive: true,
  mustChangePassword: false,
  createdAt: '2026-01-15T10:00:00Z',
  globalPermissions: [],
  clinicPermissions: [],
  ...extras,
});

/** What GET /api/permissions answers today (D57). */
export const ASSIGNABLE: AssignablePermissions = {
  global: ['users.manage', 'specialties.manage', 'clinics.manage'],
  clinicScoped: ['doctors.manage'],
};
