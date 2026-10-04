# Instructions.md — ClinicBooking

Instructions for any AI assistant working in this repository.
Read this file in full, then read `docs/decisions.md` as `CLAUDE.md` says (its index
first, then the decisions that apply).

---

## What this project is

An outpatient appointment booking system for multiple clinics.

It has two goals, both required: a real, usable product, and a complete
deployment pipeline (container → registry → Azure Container Apps, kept
deployable through CI/CD) that doubles as a portfolio piece.

**The application is small on purpose.** Deployment to Azure is postponed until
the build is complete (D54). Until then CI builds, tests and builds the Docker
image on every push, and the postponed steps are on the checklist "Deferred
until deployment" in `decisions.md`.

---

## Non-negotiable rules

1. **Never add a feature that is not in scope** (see `decisions.md`). If a
   change seems to need one, stop and say so instead of implementing it.
2. **Never write a user-facing string literal in a component.** Every string
   goes through Transloco and into both `ar.json` and `en.json`.
3. **Never use physical CSS direction utilities.** `ms-`/`me-`/`ps-`/`pe-`/
   `start-`/`end-`/`text-start`/`text-end`/`rounded-s`/`rounded-e`/
   `border-s`/`border-e` only.
4. **Never return an English sentence from the API.** `ProblemDetails.title`
   and validation messages are keys: `error.appointment.slot_taken`.
5. **Never return or accept an EF entity in a controller.** DTOs only.
6. **Never put a secret in `appsettings.json`** or anywhere else in the repo.
   Local secrets live in `.env` (git-ignored) or User Secrets.
7. **Never use `DateTime`.** `DateTimeOffset` for instants, `TimeOnly` for
   working hours.
8. **Never introduce a new NuGet or npm package** without saying why and
   getting agreement first.
9. **Never check access by role name or magic string.** Use the permission
   constants and authorization policies; every clinic-scoped resource must be
   authorized against the caller's clinics.
10. **Never log patient PII** (name, phone, national ID, free text).

---

## Structure

```
ClinicBooking/
├── CLAUDE.md                          points to docs/ (read first)
├── ClinicBooking.sln
├── src/
│   ├── ClinicBooking.Domain/          Entities, Enums, ValueObjects, Exceptions, Permissions
│   ├── ClinicBooking.Application/     Features, DTOs, Interfaces (incl. IAppDbContext), Validators, service implementations
│   ├── ClinicBooking.Infrastructure/  Persistence (AppDbContext), Identity, EntityConfigurations, Interceptors
│   ├── ClinicBooking.Api/             Controllers, middleware, filters, Program.cs, wwwroot
│   └── clinic-booking-web/            Angular + TypeScript
│       └── src/api/                   openapi.json + schema.d.ts (generated, committed)
├── tests/
│   └── ClinicBooking.Tests/           integration tests (Testcontainers)
├── docs/
│   ├── Instructions.md
│   └── decisions.md
├── Dockerfile                         multi-stage: node → dotnet sdk → runtime
├── docker-compose.yml                 api + sqlserver
├── .env.example                       dummy values; real .env is git-ignored
└── .github/workflows/ci.yml
```

- Application and Infrastructure each contain a static `DependencyInjection`
  class with an extension method `AddApplication(this IServiceCollection)` /
  `AddInfrastructure(this IServiceCollection, IConfiguration)` returning
  `IServiceCollection`, called from `Program.cs` in Api.
- Every project has a `GlobalUsings.cs` for commonly used namespaces.
- Application never references Infrastructure. Services depend on
  `IAppDbContext`; `AppDbContext` implements it.

## Stack

**Back end** — .NET 10, ASP.NET Core Web API, attribute-routed controllers,
EF Core + SQL Server, FluentValidation, Serilog (JSON), JWT bearer, ASP.NET
Core Identity.
  
**Front end** — Angular (latest stable), TypeScript strict, Tailwind alone
(no component library), Transloco.

**Infrastructure** — Docker and GitHub Actions today; Azure Container Registry,
Azure Container Apps, Azure SQL, Key Vault and OIDC to Azure are the target,
deferred until the build is complete (D54).

---

## Conventions

### C#
- File-scoped namespaces, nullable enabled, `var` for obvious types.
- Async everywhere; suffix `Async`; always pass `CancellationToken`.
- Services: interface + implementation, scoped registration.
- Queries: project with `Select` into DTOs rather than loading entities.
- No `.Result`, no `.Wait()`, no `async void`.
- Never use `ExecuteUpdate` / `ExecuteDelete`: they bypass the audit and
  soft-delete interceptor. Load the entity and call `SaveChangesAsync`. A test
  fails the build if they appear in `src/`.
- FluentValidation runs through a global MVC action filter (`ValidationFilter`),
  not automatic MVC validation (`IEndpointFilter` is minimal-API only, D4/D50).
  Validators check input only; business rules live in Application/Domain.
  Every validator message is an error key (`error.<area>.<reason>`).
- Every request DTO and query object of an action needs a validator in
  `Application/Validators`; a test fails if one is missing.
- Never put `[Produces]` on a controller (it overrides `application/problem+json`).
- Convert UTC ↔ Cairo time only inside Application, only to validate rules.
- Permission names are constants in Domain (`patients.create`, ...). Protect an
  endpoint with `[Authorize(Policy = Permissions.X.Y)]`: there is one policy per
  permission, named after it. Never compare permission or role names as literals.
  A permission is either global (`Permissions.Global`) or clinic-scoped
  (`Permissions.ClinicScoped`, granted per clinic, D57); see "Clinic-scoped
  permissions" below.
- Errors from auth use the keys in D48 (`error.auth.*`).
- Endpoints are secure by default: a fallback policy requires a signed-in user
  where no attribute says otherwise. An anonymous endpoint must say so
  (`[AllowAnonymous]` / `.AllowAnonymous()`), deliberately (D50).

### Adding an entity (follow Specialties, D49/D50)
1. Derive from `SoftDeletableEntity` (only Specialties, Clinics, Doctors,
   Patients, D35). Private setters; a `Create`/`SetNames`-style method sets the
   display text **and** its normalised copy (`SearchText.Normalize`).
2. Configuration in `Infrastructure/EntityConfigurations`: `nvarchar` for text;
   unique filtered indexes (`WHERE [IsDeleted] = 0`) over the **normalised**
   column for reference data only (Patients: normalised column for search, no
   unique constraint on names), each with the `ClinicBooking:ConflictKey`
   annotation naming its 409 error key. Add a migration.
3. Application: interface in `Interfaces`, implementation in `Features/<Name>`
   (scoped), request/response DTOs, one validator per request DTO and query
   object, a `Select` projection (never return an entity), error keys as
   constants. Updates take a `rowVersion` and answer 409
   `error.concurrency.conflict` when it is stale.
4. Api: attribute-routed controller, `[Authorize]` / `[Authorize(Policy = ...)]`
   on every action, `[ProducesResponseType]` for the success response (200/201
   with its type: without it the OpenAPI document has no schema, D52) and the
   error responses. Add the permission constants to `Permissions` and to `Global`
   (or `ClinicScoped`, see below); a global one reaches the seeded admin at the
   next startup, once (seeder top-up, D55, D57: restart the API, no re-seed needed;
   removing it later sticks). A value an entity normalises or limits (a phone,
   an address) must throw `InvalidRequestException` with an error key, never a
   generic exception, so a value that skips the validator is a 400 (D55).
5. Regenerate `openapi.json` and `schema.d.ts` (`npm run gen:api`) and commit
   both. Tests: each endpoint with and without the permission, validation keys,
   duplicates, search, paging, audit with a real user.

**Front end for an entity** (after the API above): one service method per
endpoint in `src/api/`, typed from `schema.d.ts`; a lazy route with its own
translation scope (`ar.json` and `en.json` for the screen's strings; the entity's
`error.*` keys stay in the **root** files, where the back-end key check reads
them, D53); screens with logical utilities only; `rowVersion` sent back on every edit
and a 409 `error.concurrency.conflict` shown as "reload"; dates through the
`intl` pipe; unit tests for the logic; then `npm run lint`, `npm test`,
`npm run build`, `npm run check:i18n` and `npm run check:api`.

**New-screen checklist (front end).**
1. Route: lazy, behind `authGuard`; add `permissionGuard(Permissions.X)` when it
   needs a permission. Use `*cbCan` to hide controls (UX only: the API decides).
2. Data: one method per endpoint in `src/api/`, types from `src/api/types.ts`
   (`RequestBody`/`ResponseBody`) over `schema.d.ts`; never hand-write a model.
   A new permission name goes into `core/auth/permissions.ts` (checked by
   `check:permissions`).
3. Errors: catch with `parseApiError`, show `ErrorMessageService.keyFor(error.key)`
   through the `transloco` pipe (comment `i18n-keys: error.unexpected` on that
   line); field errors from `error.fieldErrors`; never show server text or a raw key.
4. Forms: Signal Forms (`@angular/forms/signals`), typed, validation messages as
   keys; real `<label for>`, `autocomplete` where it applies, `aria-invalid`, an
   `aria-live` error region, focus the first invalid field, disable and set
   `aria-busy` while submitting.
5. Strings: keys in a translation scope (`ar.json` and `en.json`); a new back-end
   `error.*` key needs its translation in the same change or `check:i18n` fails.
6. Layout: logical utilities only; mirror directional icons (`rtl:-scale-x-100`).
7. Tests: logic with Vitest; HTTP with `HttpTestingController` and
   `provideAuthTesting()` (`src/testing/auth-testing.ts`).

**New entity screen (copy the Specialties screens, D53).**
1. `src/api/<entity>-api.ts`: one method per endpoint (list with its query, get,
   create, update with `rowVersion`, delete), typed with `RequestBody`,
   `ResponseBody` and `QueryParams`. The generated query names are PascalCase.
2. `features/<entity>/`: lazy routes behind `authGuard` (`<entity>.routes.ts`),
   `permissionGuard` on create and edit, a scope provider and the scope resolver,
   `list/`, `form/` and the session service for the last list query and the
   status message.
3. List: the URL is the state (`q`, `page`, `size`, `sort`, `dir`; defaults in one
   constant, omitted from the URL, invalid values clamped); `rxResource` so a
   newer query cancels the older; 300 ms debounce with `replaceUrl`, Enter at
   once, IME composition ignored; loading, empty, no-results and error states;
   Retry re-issues the query; a page past the end steps back; table from md up,
   cards below; both names with their own `lang`/`dir`, the UI language first.
4. Form: a separate page; Signal Forms with the API's rules and keys; map a 400's
   field errors and name-taken 409s onto fields from `submit()`; anything else
   as a form-level message; 409 `error.concurrency.conflict` disables Save until
   Reload and shows the earlier entries; 404 shows "not found".
5. Delete: `confirm-dialog`, focus on Cancel, 404 = already deleted, other errors
   stay in the dialog; reload the list and focus the heading afterwards.
6. Strings: the screen's keys in `public/i18n/<entity>/{ar,en}.json`; the entity's
   `error.*` keys in the **root** files; Arabic wording reviewed by the owner.
7. Controls: `*cbCan` for create, edit and delete; add the permission to
   `core/auth/permissions.ts`. A header navigation entry.
8. Tests: the API service, URL parsing, search debounce, stale response, paging
   and sorting, delete (confirm, cancel, Esc, 404, last row of the last page),
   form errors and conflict, permission-aware rendering, a name containing HTML
   shown as text. Then `npm run lint`, `npm test`, `npm run check:i18n`,
   `npm run check:api` and `npm run build`.
9. Optional fields (Clinics, D56): a blank value is sent as `null` (the `PUT` is a
   full replace); a missing value shows a "Not set" key, not a bare dash. A stored
   value shown in a friendlier form (a phone through `formatPhone` / the `phone`
   pipe, always in `<bdi dir="ltr">`) needs the **untouched guard**: keep the
   stored value and the text shown, and send the stored value back when the field
   still holds that text, otherwise exactly what was typed; test both directions.
   The client keeps only trivial guards (a length); the server's rules and keys
   are the authority, and a key that names its field goes on that field.
10. A new header link: the header must still wrap with no horizontal overflow at
    360 px in RTL and LTR (jsdom cannot show layout: a by-hand check). Copying
    the Specialties screens copies `list-query.ts`, the session service and the
    list/form logic; do not extract shared code before the third entity (D56).

**The back-end error-key check.** `check:i18n` scans `src/ClinicBooking.*/**/*.cs`
(comments skipped) for `"error.<area>.<reason>"` literals and fails when one is
missing from `ar.json` or `en.json`, naming the C# file and line. A key built at
runtime (a `"error."` prefix) must be declared in
`scripts/backend-error-keys.json` with the keys it can produce, or the check
fails; a stale declaration fails too. It needs the C# sources: with `CI` set it
fails without them, otherwise it warns and skips (the Docker `web` stage).

### Clinic-scoped permissions (D34, D57)
**Adding one.** (1) A constant in a nested class of `Permissions` (for example
`Doctors.Manage = "doctors.manage"`) and an entry in `Permissions.ClinicScoped`
(`All` and the policies follow; `Global` stays for permissions held once for the
whole system). (2) Add the name to `src/clinic-booking-web/src/app/core/auth/permissions.ts`
when a screen uses it: `check:permissions` compares it with the C#. (3) Nothing else
is needed for assignment: `GET /api/permissions` and
`PUT /api/users/{id}/clinics/{clinicId}/permissions` read the same list. The seeder
never grants clinic-scoped permissions: an administrator grants them through the API.

**Protecting a clinic-scoped endpoint.**
- The clinic is in the route (`/api/clinics/{clinicId}/doctors`): use
  `[Authorize(Policy = Permissions.Doctors.Manage)]`. The route value must be named
  `clinicId` (`RouteClinicResolver`); when it is not, register an `IClinicResolver`.
  A missing clinic, a deleted clinic or a grant held only in another clinic is 403.
- The resource is addressed by its own id (`/api/doctors/{id}`): load it in the
  service, then call `IClinicAccess.RequireAsync(clinicIds, permission, notFoundKey)`.
  No clinic-scoped permission in any of its clinics is **404** with the entity's own
  not-found key (never a 403, so existence is not leaked); another permission there is
  403 (D6, D57). Put `[Authorize]` on the action; the check is the service's.
- Lists and creation: `IPermissionChecker.GetClinicIdsWithPermissionAsync` gives the
  clinics to filter by or to validate the target clinic against.
- A global permission never satisfies a clinic-scoped one. A test per endpoint: the
  right clinic, another clinic, a deleted clinic, no grant, and the 404/403 rule (see
  `ClinicScopedAuthorizationTests` and the test-only `ClinicScopedController`).

**Account state (D58).** Every authenticated request passes `AccountStateMiddleware`
(disabled user: 401; temporary password: 403 `error.auth.password_change_required`). Never
add `[AllowWhilePasswordChangeRequired]` to another endpoint: a test allows exactly
`GET /api/auth/me` and `POST /api/auth/change-password`. A password (temporary, new or
current) is never returned, logged or put in an error; keep it out of exception messages.

### TypeScript / Angular
- Standalone components only (no NgModules). Selector prefix `cb`, OnPush,
  `templateUrl` (never an inline `template`), no zone.js.
- Every user-facing string is a translation key: `{{ 'area.key' | transloco }}`
  in templates, `translate('area.key')` in code. A key built at runtime needs the
  comment `i18n-keys: a.b, c.d` on its line listing every possible key. Add each
  key to **both** `public/i18n/ar.json` and `en.json`; a feature's keys go in a
  scope folder (`public/i18n/<scope>/`) loaded with `provideTranslocoScope`.
  `npm run check:i18n` enforces this (also during `npm run build`).
- Layout uses logical utilities only (`ms-` `me-` `ps-` `pe-` `start-` `end-`
  `text-start` `text-end` `rounded-s/e` `border-s/e`). `check:logical` fails lint
  and the build on `ml-`, `pr-`, `left-`, `text-left` and the like. Mirror a
  directional icon with `rtl:-scale-x-100`.
- Display dates and numbers with the `intl` pipe (`{{ v | intl: 'date' }}`), never
  `DatePipe`; instants show in Cairo time.
- Signals for local state; `HttpClient`-based services for server data
  (`httpResource` / `rxResource` where they fit); RxJS for streams and events.
- No state library (no NgRx, no TanStack Query).
- API types come from `src/clinic-booking-web/src/api/schema.d.ts`, generated
  from the committed `openapi.json` — never hand-edited.
- One dedicated service method per endpoint, kept in `src/api/`.
- Strict mode: no `any`. Prefer `inject()` over constructor injection.
- Every `PUT` sends back the `rowVersion` it received. A 409
  `error.concurrency.conflict` means someone else changed the record: tell the
  user to reload it, never retry silently or overwrite (D50).
- Static files and the SPA fallback must be mapped anonymous (the API denies
  anonymous requests by default, D50).
- Token refresh is **single-flight**: at most one refresh request in flight;
  every caller that needs a new access token waits for and shares its result
  (the refresh token rotates on every use, D48).
- Dates and numbers are formatted through the custom `Intl` pipe with
  `ar-EG-u-nu-latn-ca-gregory`, never Angular's `DatePipe`.

### Git
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `test:`.
- English commit messages.
- Commit after each verified step, not in large batches.
- A commit that does not build is never pushed.

---

## Workflow expectations

- **CI must be green before starting the next step.**
- **Verify before claiming done.** `dotnet build`, `dotnet test`, and
  `npm run build` must all pass, plus `npm run lint`, `npm test`,
  `npm run check:i18n` and `npm run check:api`. State what actually ran.
- **Never run `docker` or `docker compose`.** No `up`, `down`, `build`, volume or
  `prune` command, never `-v`. The project owner runs Docker. Verification that
  needs Docker (running the stack, `curl` against the running containers, the Docker
  image build) is listed in the report as **"by-hand checks for the owner"**, with
  the exact commands to run. The one exception: `dotnet test` starts SQL Server
  containers through Testcontainers, which is expected; if Docker is not running,
  say so instead of starting it. The Docker image build is also proven by the CI
  `image` job.
- **Report what was decided that the instructions did not cover.** Every such
  decision either goes into `decisions.md` or gets raised.
- **Prefer the smaller change.** If an existing pattern conflicts with a rule
  here, raise it rather than copying the pattern forward.
- **Do not refactor unrelated code** while implementing a feature.
- **Changed a DTO?** Run `npm run gen:api` (in `src/clinic-booking-web`) and
  commit `openapi.json` and `schema.d.ts`. CI fails on any diff: `dotnet test`
  fails when `openapi.json` is stale, and `npm run check:api` when `schema.d.ts` is.
- **Open questions** (O-numbers in `decisions.md`) are not answered silently:
  raise them before building anything that depends on them.

---

## Build and run

```bash
# one-time: copy and fill local secrets
cp .env.example .env
# .env must contain a JWT_SIGNING_KEY of at least 32 characters (the API refuses to
# start without it) and, to get a first user, SEED_ADMIN_USERNAME / SEED_ADMIN_PASSWORD
# (password: 12+ chars with upper, lower and a digit). Never commit .env.

# full stack, local
docker compose up --build

# API alone (SQL Server must already be running; uses User Secrets, which must
# provide ConnectionStrings:Default and Jwt:SigningKey, plus Seed:* for a first user)
dotnet run --project src/ClinicBooking.Api

# log in locally (the refresh token comes back only as an HttpOnly cookie)
curl -i -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"userName":"<SEED_ADMIN_USERNAME>","password":"<SEED_ADMIN_PASSWORD>"}'

# front end (Node 24: src/clinic-booking-web/.nvmrc; `npm ci` refuses another major)
cd src/clinic-booking-web
npm ci                 # exact install from the committed lock file
npm start              # dev server on http://localhost:4200; /api is proxied to
                       # http://localhost:8080 (start the API first, e.g. `docker compose up`
                       # from the repository root; proxy.conf.json, D52). Use :4200, not :8080
npm run build          # runs check:logical and check:i18n first, then ng build
npm run lint           # angular-eslint + the logical-properties check
npm test               # Vitest unit tests + the Node tests of the check scripts
npm run check:i18n     # ar.json/en.json parity, keys used exist, no literal text,
                       # every back-end error.* key (read from the C#) is translated
npm run check:permissions # permission names in the UI exist in Permissions.cs
npm run check:api      # schema.d.ts matches openapi.json (Node only)

# tests (needs Docker running: Testcontainers starts SQL Server)
dotnet test

# add an EF Core migration (the connection string is a dummy: nothing connects)
ConnectionStrings__Default="Server=design-time;Database=ClinicBooking;User Id=sa;Password=x;TrustServerCertificate=True" \
  dotnet dotnet-ef migrations add <Name> \
  --project src/ClinicBooking.Infrastructure \
  --startup-project src/ClinicBooking.Api \
  --output-dir Persistence/Migrations

# regenerate openapi.json after changing a controller or DTO (writes
# src/clinic-booking-web/src/api/openapi.json with LF endings; commit the result).
# Without the variable the same test only checks that the file is up to date.
UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests
# (PowerShell: $env:UPDATE_OPENAPI=1; dotnet test --filter OpenApiDocumentTests)

# regenerate API types after changing a DTO: refreshes openapi.json (the line above;
# needs the .NET SDK), then writes schema.d.ts. Commit both. Run it twice: the second
# run must change nothing.
cd src/clinic-booking-web && npm run gen:api
```

---

## Domain rules that are easy to get wrong

- **An appointment may not overlap another for the same doctor**, across all
  clinics the doctor works in. Check on create and on reschedule, excluding
  the appointment being moved.
- **An appointment must fall inside the doctor's working hours** for that
  clinic and day of the week, and outside breaks and leave.
- **No booking in the past.** If the patient already has an overlapping
  appointment, show a warning the user must confirm; never block it.
- **A cancelled appointment frees its slot**; a completed one does not.
- **Status machine:** `Booked → Completed | Cancelled | NoShow`; no other
  transitions.
- **Working hours are local Cairo times** stored as `TimeOnly` + day-of-week
  per (doctor, clinic). Never store them as absolute timestamps — Egypt
  observes DST.
- **Booking references are unique and collision-checked on insert.**
- **Concurrent bookings** must be stopped by the database, not only by an
  application-level check (D31): a unique filtered index on
  `(DoctorId, StartUtc) WHERE Status <> Cancelled`.
- **One slot duration per doctor**, the same in every clinic. An appointment
  occupies exactly one slot and starts on the grid
  (`period start + n × duration`). A duration change takes effect only on a
  date after the doctor's last active appointment (D43).
- **A doctor's working-hour periods in different clinics must not overlap**
  on the same day.
- **Patients are shared across clinics** and store only a name and a phone.
  A matching phone shows a duplicate warning, never a hard error (D44).
- **Soft delete applies to Specialties, Clinics, Doctors and Patients only**
  (D35). Their unique indexes are filtered on `IsDeleted = 0`. Appointments are
  cancelled by status; join tables, slot-duration history and refresh tokens
  are hard-deleted. Deleting a doctor with upcoming appointments needs
  confirmation and cancels those appointments in the same transaction.
- **Permissions are per clinic**, except global ones (`users.*`,
  `specialties.*`, `clinics.manage`, `patients.*`). A user with `appointments.create` at clinic
  A has no access to clinic B's data. An inaccessible resource returns 404.
- **Health endpoints:** `/health/live` has no dependency checks;
  `/health/ready` checks the database.

---

## Definition of done, for the project as a whole

Deployment to Azure is postponed until the build is complete (D54), so the
eleven items are split in two. None is dropped.

### Done, and kept green by CI
1. Runs in Docker locally with one command.
7. Integration tests run in CI against a real database.
8. Both languages complete, RTL correct, no untranslated string; the
   translation-key check passes in CI.
9. CI fails if `openapi.json` / `schema.d.ts` are out of date.
10. Lint and front-end tests pass in CI.
11. `README.md` explains the architecture and shows the pipeline badge.

### Required before release (deferred, see D54)
2. Image builds and pushes to Azure Container Registry from CI (OIDC, no
   stored Azure secret).
3. Deploys to Azure Container Apps, reachable over HTTPS.
4. Migrations applied by the pipeline (migrations bundle), not by hand.
5. Secrets resolved from Key Vault via managed identity.
6. `/health/live` and `/health/ready` green. (The endpoints exist and are
   tested locally; what is deferred is running them as the Azure probes.)

The scope of the project is Phases 1 to 5 of `decisions.md`, worked in order;
the deferred items above, and the checklist "Deferred until deployment" in
`decisions.md`, must all be true before the project is called released.

### What CI covers today (the `test`, `web` and `image` jobs, D47)
Once the first run is green:

- **Item 8, the parts CI can check.** `check:i18n`: `ar.json`/`en.json` parity,
  every key used in a template or in code exists, every back-end `error.*` key
  (read from the C#, D52) is translated, no literal text in templates.
  `check:logical`: no physical direction classes or CSS properties (RTL at code
  level). **Not covered:** how RTL looks in a browser; literal strings in `.ts`
  code; the quality of the Arabic messages (the check proves they exist, not that they
  read well).
- **Item 9, fully.** `openapi.json` against the real API by
  `OpenApiDocumentTests` (the `test` job); `schema.d.ts` against `openapi.json`
  by `npm run check:api` (the `web` job, Node only).
- **Item 10, fully for what exists.** `npm run lint` and `npm test` (Vitest and
  the Node script tests), plus the production build. There are no end-to-end or
  browser tests.
- Item 7 (integration tests against a real database) is the `test` job. Items
  2–5, and item 6 as the Azure probes, are deferred (D54).
