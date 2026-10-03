import type { paths } from './schema';

/**
 * Request and response bodies read straight from the generated schema (D24), so a front-end type
 * can never drift from the API. Never write a model by hand when one of these can name it.
 */
type JsonContent<T> = T extends { content: { 'application/json': infer Body } } ? Body : never;

export type RequestBody<P extends keyof paths, M extends keyof paths[P]> = paths[P][M] extends {
  requestBody: infer Body;
}
  ? JsonContent<Body>
  : never;

export type ResponseBody<
  P extends keyof paths,
  M extends keyof paths[P],
  Status extends number,
> = paths[P][M] extends { responses: infer Responses }
  ? Status extends keyof Responses
    ? JsonContent<Responses[Status]>
    : never
  : never;

/** The query-string parameters of an operation, as the schema declares them. */
export type QueryParams<P extends keyof paths, M extends keyof paths[P]> = paths[P][M] extends {
  parameters: { query?: infer Query };
}
  ? NonNullable<Query>
  : never;
