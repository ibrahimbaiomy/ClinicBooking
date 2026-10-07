import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { parseApiError } from './api-error';

const response = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers) });

describe('parseApiError', () => {
  it('reads the key, correlation id and Retry-After from a ProblemDetails response', () => {
    const error = parseApiError(
      response(423, { title: 'error.auth.locked_out', status: 423, correlationId: 'c-1' }, { 'Retry-After': '900' }),
    );

    expect(error).toEqual({
      kind: 'api',
      status: 423,
      key: 'error.auth.locked_out',
      fieldErrors: {},
      correlationId: 'c-1',
      retryAfterSeconds: 900,
      periodIndex: null,
      conflictingClinicId: null,
    });
  });

  it('reads the period index and the conflicting clinic of a working-hours 422 (D61)', () => {
    const error = parseApiError(
      response(422, { title: 'error.doctor.period_overlaps_other_clinic', periodIndex: 3, conflictingClinicId: '42' }),
    );

    expect(error.key).toBe('error.doctor.period_overlaps_other_clinic');
    expect(error.periodIndex).toBe(3);
    expect(error.conflictingClinicId).toBe(42);
  });

  it.each([[-1], [1.5], ['x'], ['-2'], [null], [true], [{}], ['99999999999999999']])(
    'treats a period index or clinic id that is not a non-negative integer (%j) as absent',
    (value) => {
      const error = parseApiError(response(422, { title: 'error.doctor.periods_overlap', periodIndex: value, conflictingClinicId: value }));

      expect(error.periodIndex).toBeNull();
      expect(error.conflictingClinicId).toBeNull();
    },
  );

  it('reads field errors as keys and replaces anything else with error.validation.invalid', () => {
    const error = parseApiError(
      response(400, {
        title: 'error.validation.failed',
        errors: {
          userName: ['error.auth.user_name_required'],
          password: ['Password is required', 'error.auth.field_too_long'],
        },
      }),
    );

    expect(error.fieldErrors).toEqual({
      userName: ['error.auth.user_name_required'],
      password: ['error.validation.invalid', 'error.auth.field_too_long'],
    });
  });

  it('never exposes a title that is not an error key (server English text)', () => {
    expect(parseApiError(response(500, { title: 'An error occurred while processing your request.' })).key).toBe(
      'error.unexpected',
    );
    expect(parseApiError(response(500, { title: 'ERROR.AUTH.X' })).key).toBe('error.unexpected');
    expect(parseApiError(response(500, { title: 'error.' })).key).toBe('error.unexpected');
  });

  it.each([['<html>bad gateway</html>'], [null], [undefined], [42], [['x']]])(
    'treats a non-ProblemDetails body (%j) as error.unexpected with the status kept',
    (body) => {
      const error = parseApiError(response(502, body));

      expect(error.kind).toBe('api');
      expect(error.status).toBe(502);
      expect(error.key).toBe('error.unexpected');
      expect(error.correlationId).toBeNull();
    },
  );

  it('maps a network failure (status 0) to error.network', () => {
    const error = parseApiError(new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }));

    expect(error.kind).toBe('network');
    expect(error.key).toBe('error.network');
  });

  it('maps anything that is not an HttpErrorResponse to error.unexpected', () => {
    expect(parseApiError(new Error('boom')).key).toBe('error.unexpected');
    expect(parseApiError('x').key).toBe('error.unexpected');
  });

  it.each([['120', 120], [' 5 ', 5], ['Wed, 21 Oct 2026 07:28:00 GMT', null], ['-1', null], ['abc', null], ['', null]])(
    'reads Retry-After %j as %j',
    (header, expected) => {
      expect(parseApiError(response(429, { title: 'error.auth.rate_limited' }, { 'Retry-After': header })).retryAfterSeconds).toBe(
        expected,
      );
    },
  );
});
