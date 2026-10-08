import { Patient, PatientPage } from '../api/patients-api';

export const PATIENTS_URL = '/api/patients';

/** A patient as the API returns it (phone in E.164). */
export const patient = (id: number, name: string, phone: string, extras: Partial<Patient> = {}): Patient => ({
  id,
  name,
  phone,
  createdAt: '2026-01-15T10:00:00Z',
  updatedAt: null,
  rowVersion: 'AAAAAAAAAAE=',
  ...extras,
});

export const pageOfPatients = (items: Patient[], totalCount = items.length, page = 1, pageSize = 20): PatientPage => ({
  items,
  page,
  pageSize,
  totalCount,
});

export const SARA = patient(1, 'سارة محمود', '+201012345678');
export const JOHN = patient(2, 'John Smith', '+442079460958');

export const ALL_PATIENT_PERMISSIONS = ['patients.read', 'patients.create', 'patients.edit', 'patients.delete'];
