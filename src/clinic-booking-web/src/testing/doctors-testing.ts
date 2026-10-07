import { Doctor, DoctorClinic, DoctorPage, WorkingHours } from '../api/doctors-api';
import { CurrentUser } from '../api/auth-api';

export const DOCTORS_URL = '/api/doctors';
export const SPECIALTIES_URL = '/api/specialties';
export const CLINICS_URL = '/api/clinics';

export const assignment = (clinicId: number, nameAr: string, nameEn: string, isActive = true): DoctorClinic => ({
  clinicId,
  nameAr,
  nameEn,
  isActive,
});

/** Two clinics used throughout the Doctors specs. */
export const NILE_AT = assignment(1, 'عيادة النيل', 'Nile Clinic');
export const NOOR_AT = assignment(2, 'عيادة النور', 'Al Noor Clinic');

/** A doctor as the API returns it. */
export const doctor = (id: number, nameAr: string, nameEn: string, extras: Partial<Doctor> = {}): Doctor => ({
  id,
  nameAr,
  nameEn,
  specialties: [{ id: 10, nameAr: 'قلب', nameEn: 'Cardiology' }],
  clinics: [NILE_AT],
  slotMinutes: 15,
  pendingSlotChange: null,
  createdAt: '2026-01-15T10:00:00Z',
  updatedAt: null,
  rowVersion: 'AAAAAAAAAAE=',
  ...extras,
});

export const AHMED = doctor(7, 'أحمد علي', 'Ahmed Ali', {
  specialties: [
    { id: 11, nameAr: 'جلدية', nameEn: 'Dermatology' },
    { id: 10, nameAr: 'قلب', nameEn: 'Cardiology' },
  ],
  clinics: [NILE_AT, { ...NOOR_AT, isActive: false }],
});

export const pageOfDoctors = (items: Doctor[], totalCount = items.length, page = 1, pageSize = 20): DoctorPage => ({
  items,
  page,
  pageSize,
  totalCount,
});

/** A reference page (specialties or clinics) as the filters and forms load it. */
export const referencePage = (items: { id: number; nameAr: string; nameEn: string }[], totalCount = items.length) => ({
  items: items.map((item) => ({ ...item, address: null, phone: null, createdAt: '2026-01-15T10:00:00Z', updatedAt: null, rowVersion: 'AAAAAAAAAAE=' })),
  page: 1,
  pageSize: 100,
  totalCount,
});

export const SPECIALTIES = [
  { id: 10, nameAr: 'قلب', nameEn: 'Cardiology' },
  { id: 11, nameAr: 'جلدية', nameEn: 'Dermatology' },
  { id: 12, nameAr: 'أطفال', nameEn: 'Paediatrics' },
];

export const CLINICS = [
  { id: 1, nameAr: 'عيادة النيل', nameEn: 'Nile Clinic' },
  { id: 2, nameAr: 'عيادة النور', nameEn: 'Al Noor Clinic' },
  { id: 3, nameAr: 'عيادة الهرم', nameEn: 'Pyramids Clinic' },
];

/** The /me clinic grants: doctors.manage in each given clinic (names from CLINICS). */
export const managerOf = (...clinicIds: number[]): Partial<CurrentUser> => ({
  clinicPermissions: clinicIds.map((id) => {
    const clinic = CLINICS.find((c) => c.id === id) ?? { id, nameAr: `عيادة ${id}`, nameEn: `Clinic ${id}` };
    return { clinicId: id, clinicNameAr: clinic.nameAr, clinicNameEn: clinic.nameEn, permissions: ['doctors.manage'] };
  }),
});

export const hours = (
  periods: { dayOfWeek: number; start: string; end: string }[] = [],
  extras: Partial<WorkingHours> = {},
): WorkingHours => ({
  doctorId: 7,
  clinicId: 1,
  isActive: true,
  periods,
  rowVersion: 'AAAAAAAAAAI=',
  ...extras,
});
