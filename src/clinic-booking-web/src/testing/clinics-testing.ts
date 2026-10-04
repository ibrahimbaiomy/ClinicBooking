import { Clinic, ClinicPage } from '../api/clinics-api';

export const CLINICS_URL = '/api/clinics';

/** A clinic as the API returns it. The fourth argument is a rowVersion, or the optional fields. */
export const clinic = (
  id: number,
  nameAr: string,
  nameEn: string,
  details: string | Partial<Pick<Clinic, 'address' | 'phone' | 'rowVersion'>> = {},
): Clinic => {
  const extras = typeof details === 'string' ? { rowVersion: details } : details;
  return {
    id,
    nameAr,
    nameEn,
    address: extras.address ?? null,
    phone: extras.phone ?? null,
    createdAt: '2026-01-15T10:00:00Z',
    updatedAt: null,
    rowVersion: extras.rowVersion ?? 'AAAAAAAB',
  };
};

export const pageOfClinics = (items: Clinic[], totalCount = items.length, page = 1, pageSize = 20): ClinicPage => ({
  items,
  page,
  pageSize,
  totalCount,
});

export const NILE = clinic(1, 'عيادة النيل', 'Nile Clinic', {
  address: '12 شارع النيل، الجيزة',
  phone: '+201012345678',
});
export const NOOR = clinic(2, 'عيادة النور', 'Al Noor Clinic');
