import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryParams, RequestBody, ResponseBody } from './types';

export type Patient = ResponseBody<'/api/patients/{id}', 'get', 200>;
export type PatientPage = ResponseBody<'/api/patients', 'get', 200>;
export type PatientListQuery = QueryParams<'/api/patients', 'get'>;
export type CreatePatientRequest = RequestBody<'/api/patients', 'post'>;
export type UpdatePatientRequest = RequestBody<'/api/patients/{id}', 'put'>;

const BASE = '/api/patients';
const patientUrl = (id: number | string) => `${BASE}/${encodeURIComponent(String(id))}`;

/** One method per Patients endpoint (D63). Nothing else talks to /api/patients. */
@Injectable({ providedIn: 'root' })
export class PatientsApi {
  private readonly http = inject(HttpClient);

  /** `Search` is a name or a phone (D63): the server decides which. */
  list(query: PatientListQuery): Observable<PatientPage> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(name, String(value));
      }
    }
    return this.http.get<PatientPage>(BASE, { params });
  }

  get(id: number | string): Observable<Patient> {
    return this.http.get<Patient>(patientUrl(id));
  }

  /** 409 error.patient.phone_exists when the phone is in use; resend with confirmDuplicatePhone: true. */
  create(request: CreatePatientRequest): Observable<Patient> {
    return this.http.post<Patient>(BASE, request);
  }

  /** A full replace with the row version (D55); the duplicate warning applies only to a changed phone. */
  update(id: number | string, request: UpdatePatientRequest): Observable<Patient> {
    return this.http.put<Patient>(patientUrl(id), request);
  }

  delete(id: number | string): Observable<void> {
    return this.http.delete<void>(patientUrl(id));
  }
}
