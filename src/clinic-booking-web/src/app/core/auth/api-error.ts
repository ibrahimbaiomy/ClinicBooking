import { HttpErrorResponse } from '@angular/common/http';

/** Front-end keys for failures the API never reports (network) or reports unreadably. */
export const NETWORK_ERROR_KEY = 'error.network';
export const UNEXPECTED_ERROR_KEY = 'error.unexpected';
export const VALIDATION_INVALID_KEY = 'error.validation.invalid';

const KEY_PATTERN = /^error(?:\.[a-z0-9_]+)+$/;

export interface ApiError {
  kind: 'api' | 'network';
  status: number;
  /** A translation key, always well-formed: server text is never shown (D26). */
  key: string;
  /** Field name (as the API sends it) to translation keys. */
  fieldErrors: Record<string, string[]>;
  correlationId: string | null;
  retryAfterSeconds: number | null;
  /**
   * Working-hours 422s (D61, D62): the index, in the request that was sent, of the period the rule
   * names; null when the response names none.
   */
  periodIndex: number | null;
  /** The other clinic an overlapping period collides with (its id), or null. */
  conflictingClinicId: number | null;
}

export function isErrorKey(value: unknown): value is string {
  return typeof value === 'string' && KEY_PATTERN.test(value);
}

/** The single place a ProblemDetails response is read (D52). Never throws. */
export function parseApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return build('api', 0, UNEXPECTED_ERROR_KEY);
  }

  if (error.status === 0) {
    return build('network', 0, NETWORK_ERROR_KEY);
  }

  const body: unknown = error.error;
  const problem = isRecord(body) ? body : {};
  const result = build('api', error.status, isErrorKey(problem['title']) ? problem['title'] : UNEXPECTED_ERROR_KEY);

  result.correlationId = typeof problem['correlationId'] === 'string' ? problem['correlationId'] : null;
  result.retryAfterSeconds = parseRetryAfter(error.headers.get('Retry-After'));
  result.periodIndex = readCount(problem['periodIndex']);
  result.conflictingClinicId = readCount(problem['conflictingClinicId']);

  const errors = problem['errors'];
  if (isRecord(errors)) {
    for (const [field, messages] of Object.entries(errors)) {
      const list = Array.isArray(messages) ? messages : [];
      result.fieldErrors[field] = list.map((m) => (isErrorKey(m) ? m : VALIDATION_INVALID_KEY));
    }
  }

  return result;
}

function build(kind: ApiError['kind'], status: number, key: string): ApiError {
  return {
    kind,
    status,
    key,
    fieldErrors: {},
    correlationId: null,
    retryAfterSeconds: null,
    periodIndex: null,
    conflictingClinicId: null,
  };
}

/** A non-negative safe integer, sent as a number or (int64) as a digit string; anything else is absent. */
function readCount(value: unknown): number | null {
  const parsed =
    typeof value === 'number' ? value : typeof value === 'string' && /^\d{1,16}$/.test(value) ? Number(value) : NaN;
  return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Only the delay-seconds form; an HTTP-date (never sent by this API) counts as absent. */
function parseRetryAfter(header: string | null): number | null {
  if (header === null || !/^\d{1,9}$/.test(header.trim())) {
    return null;
  }
  return Number(header.trim());
}

/**
 * The reference to quote to support, shown only for failures nobody can explain to the user (D52): an
 * unexpected or network error. A failure with its own key never shows one.
 */
export function supportReference(error: ApiError): string | null {
  return error.key === UNEXPECTED_ERROR_KEY || error.key === NETWORK_ERROR_KEY ? error.correlationId : null;
}
