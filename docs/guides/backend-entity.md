# Guide — adding a back-end entity or endpoint

Follow Specialties (D49, D50); Clinics shows the variations (D55). For a
clinic-scoped entity (Doctors) also read `clinic-permissions.md`.

## Steps

1. **Domain.** Derive from `SoftDeletableEntity` (only Specialties, Clinics,
   Doctors, Patients, D35), otherwise `AuditableEntity`. Private setters; a
   `Create`/`SetNames`-style method sets the display text **and** its normalised
   copy (`SearchText.Normalize`, D49). A value the entity normalises or limits
   (a phone through `PhoneNumber`, an address) throws `InvalidRequestException`
   with an error key, so a value that skips the validator is a 400 (D55).
2. **Infrastructure.** Configuration in `Infrastructure/EntityConfigurations`:
   `nvarchar` for text; unique filtered indexes (`WHERE [IsDeleted] = 0`) over the
   **normalised** column for reference data only (Patients: a normalised column
   for search, no unique constraint on names), each with the
   `ClinicBooking:ConflictKey` annotation naming its 409 key. Add a migration
   (`build-and-run.md`).
3. **Application.** Interface in `Interfaces`, implementation in
   `Features/<Name>` (scoped), request/response DTOs, one validator per request
   DTO and query object, a `Select` projection from one hand-written expression
   (never return an entity), error keys as constants. Updates take a
   `rowVersion` and answer 409 `error.concurrency.conflict` when it is stale.
   Duplicates: a friendly pre-check, the index is the real guard (D50).
4. **Api.** Attribute-routed controller; `[Authorize]` / `[Authorize(Policy =
   ...)]` on every action; `[ProducesResponseType]` for the success response
   (200/201 with its type, or the OpenAPI document has no schema) and the error
   responses; never `[Produces]`.
5. **Permissions.** Add the constants to `Permissions` and to `Global` (or
   `ClinicScoped`, see `clinic-permissions.md`). A global one reaches the seeded
   admin at the next API start, once (restart the API, no re-seed; removing it
   later sticks, D55, D57). Every permission needs its label in
   `public/i18n/users/{ar,en}.json` (`permissions.<area>.<action>`), or
   `check:permissions` fails (D59).
6. **Types.** `npm run gen:api` and commit `openapi.json` and `schema.d.ts`.
7. **Tests.** Each endpoint with and without the permission, validation keys,
   duplicates (including Arabic variants), search, paging, sorting, row versions,
   audit with a real user.

## Shape to copy (D50)

`GET` list, `GET {id}`, `POST` (201 + `Location`), `PUT {id}` (full replace, with
`rowVersion`), `DELETE {id}` (204, soft delete through `Remove()`; a second delete
is 404). List query `Search`, `Page` (1), `PageSize` (20, max 100), `SortBy`,
`SortDirection`; response `{ items, page, pageSize, totalCount }`; ties broken by
`Id`. Responses never carry normalised columns or audit user ids.

## The back-end error-key check

`check:i18n` scans `src/ClinicBooking.*/**/*.cs` (comments skipped) for
`"error.<area>.<reason>"` literals and fails when one is missing from the root
`ar.json` or `en.json`, naming the C# file and line. A key built at runtime (a
`"error."` prefix literal) must be declared in `scripts/backend-error-keys.json`
with the keys it can produce, or the check fails; a stale declaration fails too.
Without the C# sources it warns and skips (the Docker `web` stage); with `CI` set
it fails (D52).
