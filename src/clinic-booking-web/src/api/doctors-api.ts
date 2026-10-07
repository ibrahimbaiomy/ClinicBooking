import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryParams, RequestBody, ResponseBody } from './types';

export type Doctor = ResponseBody<'/api/doctors/{id}', 'get', 200>;
export type DoctorPage = ResponseBody<'/api/doctors', 'get', 200>;
export type DoctorListQuery = QueryParams<'/api/doctors', 'get'>;
export type CreateDoctorRequest = RequestBody<'/api/doctors', 'post'>;
export type UpdateDoctorRequest = RequestBody<'/api/doctors/{id}', 'put'>;
export type DoctorClinic = Doctor['clinics'][number];
export type DoctorSpecialty = Doctor['specialties'][number];
export type WorkingHours = ResponseBody<'/api/doctors/{id}/clinics/{clinicId}/working-hours', 'get', 200>;
export type ReplaceWorkingHoursRequest = RequestBody<'/api/doctors/{id}/clinics/{clinicId}/working-hours', 'put'>;
export type ChangeSlotDurationRequest = RequestBody<'/api/doctors/{id}/slot-durations', 'post'>;

const BASE = '/api/doctors';

const doctorUrl = (id: number | string) => `${BASE}/${encodeURIComponent(String(id))}`;
const clinicUrl = (id: number | string, clinicId: number | string) =>
  `${doctorUrl(id)}/clinics/${encodeURIComponent(String(clinicId))}`;

/** One method per Doctors endpoint (D61). Nothing else talks to /api/doctors. */
@Injectable({ providedIn: 'root' })
export class DoctorsApi {
  private readonly http = inject(HttpClient);

  list(query: DoctorListQuery): Observable<DoctorPage> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(name, String(value));
      }
    }
    return this.http.get<DoctorPage>(BASE, { params });
  }

  get(id: number | string): Observable<Doctor> {
    return this.http.get<Doctor>(doctorUrl(id));
  }

  create(request: CreateDoctorRequest): Observable<Doctor> {
    return this.http.post<Doctor>(BASE, request);
  }

  /** Names and specialties, a full replace; `rowVersion` is the doctor's, as received (D61). */
  update(id: number | string, request: UpdateDoctorRequest): Observable<Doctor> {
    return this.http.put<Doctor>(doctorUrl(id), request);
  }

  delete(id: number | string): Observable<void> {
    return this.http.delete<void>(doctorUrl(id));
  }

  /** Schedules a new slot duration; it replaces a pending one. */
  changeSlotDuration(id: number | string, request: ChangeSlotDurationRequest): Observable<Doctor> {
    return this.http.post<Doctor>(`${doctorUrl(id)}/slot-durations`, request);
  }

  addClinic(id: number | string, clinicId: number | string): Observable<Doctor> {
    return this.http.post<Doctor>(clinicUrl(id, clinicId), null);
  }

  activateClinic(id: number | string, clinicId: number | string): Observable<Doctor> {
    return this.http.post<Doctor>(`${clinicUrl(id, clinicId)}/activate`, null);
  }

  deactivateClinic(id: number | string, clinicId: number | string): Observable<Doctor> {
    return this.http.post<Doctor>(`${clinicUrl(id, clinicId)}/deactivate`, null);
  }

  getWorkingHours(id: number | string, clinicId: number | string): Observable<WorkingHours> {
    return this.http.get<WorkingHours>(`${clinicUrl(id, clinicId)}/working-hours`);
  }

  /** The whole week, a full replace; `rowVersion` is the assignment's, from getWorkingHours (D61). */
  replaceWorkingHours(
    id: number | string,
    clinicId: number | string,
    request: ReplaceWorkingHoursRequest,
  ): Observable<WorkingHours> {
    return this.http.put<WorkingHours>(`${clinicUrl(id, clinicId)}/working-hours`, request);
  }
}
