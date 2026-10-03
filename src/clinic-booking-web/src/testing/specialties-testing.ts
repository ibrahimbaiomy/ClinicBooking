import { Specialty, SpecialtyPage } from '../api/specialties-api';

export const SPECIALTIES_URL = '/api/specialties';

export const item = (id: number, nameAr: string, nameEn: string, rowVersion = 'AAAAAAAB'): Specialty => ({
  id,
  nameAr,
  nameEn,
  createdAt: '2026-01-15T10:00:00Z',
  updatedAt: null,
  rowVersion,
});

export const pageOf = (items: Specialty[], totalCount = items.length, page = 1, pageSize = 20): SpecialtyPage => ({
  items,
  page,
  pageSize,
  totalCount,
});

export const CARDIOLOGY = item(1, 'القلب', 'Cardiology');
export const DERMATOLOGY = item(2, 'الجلدية', 'Dermatology');
