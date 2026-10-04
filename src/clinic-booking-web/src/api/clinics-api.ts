import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryParams, RequestBody, ResponseBody } from './types';

export type Clinic = ResponseBody<'/api/clinics/{id}', 'get', 200>;
export type ClinicPage = ResponseBody<'/api/clinics', 'get', 200>;
export type ClinicListQuery = QueryParams<'/api/clinics', 'get'>;
export type CreateClinicRequest = RequestBody<'/api/clinics', 'post'>;
export type UpdateClinicRequest = RequestBody<'/api/clinics/{id}', 'put'>;

const BASE = '/api/clinics';

/** One method per Clinics endpoint (D55). Nothing else talks to /api/clinics. */
@Injectable({ providedIn: 'root' })
export class ClinicsApi {
  private readonly http = inject(HttpClient);

  list(query: ClinicListQuery): Observable<ClinicPage> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') {
        params = params.set(name, String(value));
      }
    }
    return this.http.get<ClinicPage>(BASE, { params });
  }

  get(id: number | string): Observable<Clinic> {
    return this.http.get<Clinic>(`${BASE}/${encodeURIComponent(String(id))}`);
  }

  create(request: CreateClinicRequest): Observable<Clinic> {
    return this.http.post<Clinic>(BASE, request);
  }

  /** The PUT is a full replace: a null address or phone clears it. `rowVersion` is the one received (D55). */
  update(id: number | string, request: UpdateClinicRequest): Observable<Clinic> {
    return this.http.put<Clinic>(`${BASE}/${encodeURIComponent(String(id))}`, request);
  }

  delete(id: number | string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(String(id))}`);
  }
}
