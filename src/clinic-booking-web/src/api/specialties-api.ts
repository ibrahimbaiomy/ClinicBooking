import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryParams, RequestBody, ResponseBody } from './types';

export type Specialty = ResponseBody<'/api/specialties/{id}', 'get', 200>;
export type SpecialtyPage = ResponseBody<'/api/specialties', 'get', 200>;
export type SpecialtyListQuery = QueryParams<'/api/specialties', 'get'>;
export type CreateSpecialtyRequest = RequestBody<'/api/specialties', 'post'>;
export type UpdateSpecialtyRequest = RequestBody<'/api/specialties/{id}', 'put'>;

const BASE = '/api/specialties';

/** One method per Specialties endpoint (D50). Nothing else talks to /api/specialties. */
@Injectable({ providedIn: 'root' })
export class SpecialtiesApi {
  private readonly http = inject(HttpClient);

  list(query: SpecialtyListQuery): Observable<SpecialtyPage> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') {
        params = params.set(name, String(value));
      }
    }
    return this.http.get<SpecialtyPage>(BASE, { params });
  }

  get(id: number | string): Observable<Specialty> {
    return this.http.get<Specialty>(`${BASE}/${encodeURIComponent(String(id))}`);
  }

  create(request: CreateSpecialtyRequest): Observable<Specialty> {
    return this.http.post<Specialty>(BASE, request);
  }

  /** `request.rowVersion` must be the one received with the record (D50). */
  update(id: number | string, request: UpdateSpecialtyRequest): Observable<Specialty> {
    return this.http.put<Specialty>(`${BASE}/${encodeURIComponent(String(id))}`, request);
  }

  delete(id: number | string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(String(id))}`);
  }
}
