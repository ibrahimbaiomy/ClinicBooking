# Instructions.md — ClinicBooking

Instructions for any AI assistant working in this repository.
Read this file and `docs/decisions.md` before writing code.

---

## What this project is

An outpatient appointment booking system for multiple clinics.

It has two goals, both required: a real, usable product, and a complete
deployment pipeline (container → registry → Azure Container Apps, kept
deployable through CI/CD) that doubles as a portfolio piece.

**The application is small on purpose.** Deployment is built first (Phase 0),
then features are added on top of a pipeline that already works.

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

**Infrastructure** — Docker, Azure Container Registry, Azure Container Apps,
Azure SQL, Key Vault, GitHub Actions (OIDC to Azure).

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
   on every action, `[ProducesResponseType]` for the error responses. Add the
   permission constants.
5. Regenerate `openapi.json` (below) and commit it. Tests: each endpoint with
   and without the permission, validation keys, duplicates, search, paging,
   audit with a real user.

### TypeScript / Angular
- Standalone components only (no NgModules).
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

- **Verify before claiming done.** `dotnet build`, `dotnet test`, and
  `npm run build` must all pass, plus the translation-key check. State what
  actually ran.
- **Report what was decided that the instructions did not cover.** Every such
  decision either goes into `decisions.md` or gets raised.
- **Prefer the smaller change.** If an existing pattern conflicts with a rule
  here, raise it rather than copying the pattern forward.
- **Do not refactor unrelated code** while implementing a feature.
- **Changed a DTO?** Run `npm run gen:api` and commit `openapi.json` and
  `schema.d.ts`. CI fails on any diff. Until the Angular step exists, regenerate
  only `openapi.json` with `UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests`;
  `dotnet test` (and so CI) fails when it is stale.
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

# front end dev server (Angular CLI)
cd src/clinic-booking-web && ng serve

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

# regenerate API types after changing a DTO (Angular step: runs the line above, then
# openapi-typescript into schema.d.ts)
npm run gen:api
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
  `specialties.*`, `patients.*`). A user with `appointments.create` at clinic
  A has no access to clinic B's data. An inaccessible resource returns 404.
- **Health endpoints:** `/health/live` has no dependency checks;
  `/health/ready` checks the database.

---

## Definition of done, for the project as a whole

1. Runs in Docker locally with one command.
2. Image builds and pushes to Azure Container Registry from CI (OIDC, no
   stored Azure secret).
3. Deploys to Azure Container Apps, reachable over HTTPS.
4. Migrations applied by the pipeline (migrations bundle), not by hand.
5. Secrets resolved from Key Vault via managed identity.
6. `/health/live` and `/health/ready` green.
7. Integration tests run in CI against a real database.
8. Both languages complete, RTL correct, no untranslated string; the
   translation-key check passes in CI.
9. CI fails if `openapi.json` / `schema.d.ts` are out of date.
10. Lint and front-end tests pass in CI.
11. `README.md` explains the architecture and shows the pipeline badge.

Anything beyond this list is out of scope until all eleven are true.
