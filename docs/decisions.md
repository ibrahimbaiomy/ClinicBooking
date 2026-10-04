# ClinicBooking — Decisions Log

Living document. Every architectural decision is recorded here with its rationale.
Rejected alternatives are recorded too — the reasoning is the point, not the outcome.

**Status key:** `ACCEPTED` · `OPEN` · `SUPERSEDED`

---

## Purpose of this project

ClinicBooking is an outpatient appointment booking system for multiple
clinics. It has two goals, and both are required:

1. **A real, usable product** — clinics can manage patients, doctors and
   appointments in Arabic and English.
2. **A complete, working deployment pipeline** — source → container →
   registry → cloud → running application, with CI/CD — that doubles as a
   portfolio piece.

Deployment to Azure is postponed until the build is complete (D54). Code
quality and UX are judged as seriously as the pipeline.

---

## Scope

### Phase 0 — Walking skeleton (code done; deployment deferred, D54)
- Specialties (create, list, search, edit, soft delete) as the first vertical slice
- Login, JWT access tokens, refresh tokens (one seeded initial user)
- `/health/live` and `/health/ready`
- Bilingual UI (Arabic / English) with RTL, from the first component
- Docker + docker compose and CI (build, tests, image build): done
- CI/CD to Azure and deployment to Azure Container Apps: deferred until the
  build is complete (D54, checklist "Deferred until deployment")

### Phase 1 — Core CRUD and access control
- Clinics, Doctors (with clinic assignments, working hours and slot
  duration), Patients (name and phone only; create, list, search, edit,
  soft delete)
- User management
- Claim-based authorization with fine-grained, clinic-scoped permissions
- Auditing fields on every entity

### Phase 2
- Appointments (book, reschedule, cancel, list by doctor/day)
- Concurrency protection (D31)
- Audit trail (full change history)

### Phase 3
- SMS or email notifications
- Appointment reminders
- Patient self-service portal

### Phase 4
- Payments
- Medical records or prescriptions
- Reporting

### Phase 5
- Admin dashboard
- Caching
- Background jobs

---

## Architecture

### D1 — Clean Architecture, four back-end projects
`ACCEPTED`

`ClinicBooking.Domain`, `ClinicBooking.Application`,
`ClinicBooking.Infrastructure`, `ClinicBooking.Api`.

The Angular app (`clinic-booking-web`) and the test project
(`ClinicBooking.Tests`) are not counted: "four projects" refers to the
back-end layers only.

Service implementations live in Application. Application and Infrastructure
each expose a static `DependencyInjection` class with an extension method on
`IServiceCollection` that returns `IServiceCollection`, called from
`Program.cs`.

### D2 — No repository pattern; `IAppDbContext` abstraction
`ACCEPTED`

Services depend on `IAppDbContext`, declared in Application. It exposes the
`DbSet<T>` properties and `SaveChangesAsync`. `AppDbContext` in
Infrastructure implements it.

**Why:** `DbSet<T>` already is a repository with a richer interface than any
wrapper would expose; a repository over EF Core obstructs `Include`,
projection and split queries. The interface exists only so Application does
not reference Infrastructure, which keeps the Clean Architecture dependency
rule intact.

### D3 — Services, not CQRS / MediatR
`ACCEPTED`

Business logic lives in plain service classes (`AppointmentService`,
`PatientService`, `DoctorService`), registered as scoped, injected into
controllers. Inside Application, code is organised by feature folder
(`Features/Patients`, ...) for navigation only; there are no handlers,
pipelines or request objects.

**Why:** CQRS earns its complexity when read and write models genuinely
diverge. They do not here.

### D4 — Attribute-routed controllers, not minimal APIs
`ACCEPTED`

**Why:** controllers group related endpoints, carry filters and attributes
cleanly, and are what most .NET codebases a reviewer has seen look like.

### D5 — SOLID applied at service granularity
`ACCEPTED`

One service per aggregate. Dependencies via constructor injection against
interfaces (`IAppointmentService`). No service-locator, no static state.

### D6 — `long` keys; protection through authorization
`ACCEPTED`

Entities use `long Id`. Sequential IDs are not a secret and are not relied on
as one. Protection comes from:

- authorization on every resource access, scoped to the caller's clinics
  (a valid ID the caller has no permission on returns 404, not 403, so
  existence is not leaked);
- exposing IDs only where an endpoint needs them;
- `Appointment` additionally carries a `BookingReference` — a short,
  human-readable, unique string (e.g. `A7K2M9`) used in URLs and spoken to
  patients.

**Why:** an unauthenticated or under-privileged caller must never be able to
read a record by incrementing a number. Authorization is the real control;
hiding IDs is a secondary layer.

### D7 — DTOs at every API boundary
`ACCEPTED`

No EF entity is ever accepted as input or returned as output from a
controller. Separate request and response DTOs per endpoint.

**Why:** entities carry navigation properties and internal IDs, and binding
directly to them is an over-posting vulnerability.

### D8 — Manual mapping, no AutoMapper
`ACCEPTED`

Mapping is written by hand in the service layer, or projected with `Select`
directly in the query.

**Why:** at this size, explicit mapping is shorter to read than the
configuration it would replace, and `Select` projection produces better SQL.

### D9 — Input validation and business rules
`ACCEPTED`

Request DTOs are validated with FluentValidation at the API boundary, applied
through a filter, not through automatic MVC validation. `IEndpointFilter`
exists only for minimal APIs, and controllers were chosen (D4), so the filter
is a global MVC **action filter** (`ValidationFilter`, D50).
Validators own input and format checks: required fields, length limits, valid
formats, acceptable ranges.

Business invariants are enforced in the Application/Domain layer and are never
assumed to be satisfied because an HTTP request passed DTO validation.
Examples: appointment overlap detection, working-hours validation,
appointment state transitions.

**Why:** API validation and business rules serve different purposes. Keeping
input validation at the boundary avoids duplication, while enforcing
invariants in Application/Domain prevents invalid state from being created
through other callers or future integrations.

### D10 — Errors: `ProblemDetails` carrying keys, never sentences
`ACCEPTED`

*(Merges the former D10 and D11.)*

A single exception-handling middleware converts every failure into RFC 7807
`ProblemDetails`. The `title` field holds an **error key**, never an English
sentence:

```json
{ "title": "error.appointment.slot_taken", "status": 409, "traceId": "..." }
```

- Validation failures: 400, with per-field errors whose messages are also keys
  (`.WithMessage("error.patient.name_required")`).
- Conflicts (a state that clashes with existing data, e.g. `slot_taken`): 409.
- Business-rule violations (e.g. outside working hours): 422.
- 401 and 403 from the JWT middleware use the same shape and keys
  (`error.auth.unauthorized`, `error.auth.forbidden`).
- Unexpected exceptions: 500 with a correlation ID and no internal detail.

The front end resolves keys through Transloco.

**Why:** English sentences from the API would force translation logic in the
client and leak untranslated text into the Arabic interface. This is the
decision that makes a bilingual UI work.

### D11 — *(merged into D10)*
`SUPERSEDED`

### D12 — Time: UTC for storage, Cairo for rules and display
`ACCEPTED`

- All instants are stored as `DateTimeOffset` in UTC.
- Doctor working hours are stored as local Cairo `TimeOnly` plus day-of-week,
  per (doctor, clinic), never as absolute times. Egypt observes DST, and
  stored local times would break twice a year.
- **The server converts UTC ↔ Cairo** (`TimeZoneInfo`, Africa/Cairo) inside
  Application, only to validate rules such as "appointment falls inside
  working hours". Display formatting stays in the front end.
- The runtime image must contain tzdata. Use the standard .NET runtime image,
  not Alpine or chiseled, unless tzdata is added explicitly.
- Accepted risk: a future appointment stored in UTC shifts by an hour if
  Egypt changes its DST rules. The likelihood is low and the cost of
  mitigation (storing local time plus zone for every appointment) is not
  justified now.

### D13 — SQL Server in Docker locally, Azure SQL in the cloud
`ACCEPTED`

Local development runs SQL Server in a container via `docker compose`. The
cloud environment uses Azure SQL Database (Basic tier).

### D14 — EF Core Migrations from the first commit
`ACCEPTED`

No `EnsureCreated`, no generated create-scripts. Migrations are applied at
startup in Development, and by an explicit pipeline step in production (D39).

**Why:** retrofitting migrations after the schema exists costs far more than
starting with them.

### D15 — One container: API serves the built front end
`ACCEPTED`

The Dockerfile is multi-stage: stage one builds the Angular bundle with Node,
stage two builds the API with the .NET SDK, the final stage copies the bundle
into `wwwroot` and runs the API, which serves static files with a SPA
fallback.

**Why:** one image, one deployment, one cost. It also removes CORS from
production entirely — the front end and API share an origin.

**Implementation (D51).** The Node stage is `node:24-bookworm-slim`: `npm ci`
from the committed lock file, then `npm run build` (which runs the prebuild
checks, so `scripts/` and `public/i18n` are copied too). The final stage copies
`dist/clinic-booking-web/browser` into `/app/wwwroot`. The .NET layers copy only
the .NET projects, so a front-end change does not invalidate them.

### D16 — Dockerfile and compose written on day one
`ACCEPTED`

`Dockerfile` and `docker-compose.yml` exist and run before the first UI screen
is written.

**Why:** containerisation problems discovered late are structural. Discovered
early they are trivial.

### D17 — Azure Container Apps, not App Service
`ACCEPTED`

Target: Azure Container Registry → Azure Container Apps → Azure SQL, with
secrets in Key Vault via managed identity.

**Why:** Container Apps exercises the container workflow end to end, scales to
zero (keeping cost near nothing), and is the more current skill.

### D18 — GitHub Actions, not Azure DevOps Pipelines
`ACCEPTED`

**Why:** the repositories are already on GitHub, the workflow file lives with
the code, and a public green badge is visible to anyone reviewing the repo.

### D19 — Secrets: `.env` locally, Key Vault in Azure
`ACCEPTED`

No secret of any kind appears in `appsettings.json` or in the repository, at
any point in history.

- **Local, via docker compose:** secrets live in `.env` (SQL Server password,
  local JWT signing key). `.env` is in `.gitignore`; `.env.example` with dummy
  values is committed.
- **Local, via `dotnet run`:** User Secrets.
- **Azure:** Key Vault through managed identity.

**Why:** User Secrets do not exist inside a container, and `docker compose up`
is the primary local path (Definition of Done #1).

### D20 — Two health endpoints
`ACCEPTED`

- `/health/live` returns 200 if the process is running. No dependencies
  checked. Used as the Container Apps liveness probe.
- `/health/ready` returns 200 only if the database is reachable. Used as the
  readiness probe.

**Why:** a single endpoint that checks the database would make Container Apps
restart a healthy container whenever the database is briefly unavailable,
which fixes nothing.

### D21 — Serilog with structured JSON output
`ACCEPTED`

Console sink, JSON formatted, so Azure Log Analytics can query fields rather
than grep text. Every request is logged with a correlation ID. Patient PII is
never written to logs (D38).

---

## Front end

### D22 — Angular + TypeScript
`ACCEPTED`

Angular, latest stable version at project creation, with TypeScript strict
mode and the test runner that the Angular CLI ships by default at that
version.

**Why:** Angular is the most widely used enterprise front-end framework in
the .NET job market, and it provides components, routing, forms, dependency
injection and a TypeScript-first model without a separate meta-framework.

**Rejected:** Blazor WebAssembly. It would have shared DTOs directly and cost
less time — D24 recovers most of that benefit — but Angular is the more
marketable skill, and employability is one of the two goals of this project.

**Versions at creation (verified, Oct 2026).** Angular and CLI **22.2.1**; Node
`^22.22.3 || ^24.15.0 || >=26` (the project and the image use Node 24); the
CLI pins **TypeScript 6.0.x** (npm's "latest" 7.x is not supported by Angular
22 and is not used); the CLI's default test runner is **Vitest 5 with jsdom**
(`ng test`). Applications are **zoneless** by default (no zone.js), so state
lives in Signals and templates update through them. See D51.

### D23 — TanStack Query for server state
`SUPERSEDED` by D33

This decision was written with React in mind (`useState`, hooks). It does not
apply to Angular.

### D24 — TypeScript types generated from OpenAPI, committed
`ACCEPTED`

`openapi-typescript` generates `schema.d.ts` from the API's OpenAPI document.
Both `openapi.json` and `schema.d.ts` live in `clinic-booking-web/src/api/`
and are **committed**.

- A developer who changes a DTO runs `npm run gen:api` and commits the result.
- CI regenerates both files and **fails if there is any diff**.
- Generated files are never hand-edited.

**Refinement (D50).** `openapi.json` is generated from the API running inside
the test host, not by the build: the build-time generator starts the application,
which would need a valid JWT signing key for every `dotnet build`, including the
Docker build. `OpenApiDocumentTests` compares the committed file with the
generated document (line endings ignored) and fails with the regeneration
command, so `dotnet test` in CI already enforces "openapi.json is up to date".
`UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests` rewrites the file
(LF endings). `npm run gen:api` must call that first, then `openapi-typescript`;
the `schema.d.ts` diff check arrives with the Angular step.

**Implemented (D51).** Both files live in `src/clinic-booking-web/src/api/` and
are committed. `npm run gen:api` runs the regeneration test (it needs the .NET
SDK) and then `openapi-typescript`; `npm run check:api` regenerates
`schema.d.ts` in memory from the committed `openapi.json` and fails on any
difference, needing only Node (for CI). Both are plain Node, so they behave the
same in PowerShell, Git Bash and Linux. Output is LF and deterministic.
`schema.d.ts` is excluded from lint. `openapi-typescript` declares a TypeScript
5 peer, which clashes with the required TypeScript 6, so `package.json` carries
an npm `overrides` entry making it use the project's TypeScript; remove it when
the package supports TypeScript 6.

**Why:** the Docker build compiles Angular before the API exists, so the
OpenAPI document cannot be produced inside the same image build. Committing
the outputs and verifying them in CI restores end-to-end type safety without
reordering the Dockerfile.

### D25 — Tailwind alone, logical properties only
`ACCEPTED`

No component library (no Angular Material, no PrimeNG).

Use `ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`, `text-end`,
`rounded-s-*`, `rounded-e-*`, `border-s`, `border-e`. Never `ml-`, `mr-`,
`pl-`, `pr-`, `left-`, `right-`, `text-left`, `text-right`, `rounded-l-*`,
`rounded-r-*`. Directional icons (arrows, chevrons) must be mirrored in RTL.

**Why:** the layout must mirror correctly when direction flips to RTL.
Physical properties silently break Arabic layout. Tailwind alone keeps RTL
fully under our control.

**Enforcement.** `scripts/check-logical-properties.mjs` (no dependencies) scans
templates, components and stylesheets for physical utilities, also behind
variants (`md:ml-2`, `rtl:pr-3`) and with arbitrary values, for physical corner
and scroll utilities, and for physical CSS properties (`margin-left`, `left:`,
`text-align: left`, ...). It names file, line and the replacement. It runs in
`npm run lint` and as part of `prebuild`, so lint, `npm run build` and the Docker
build fail on a violation. A line may carry the comment `logical-ok` with a
reason. **Directional icons** (arrows, chevrons) are mirrored with
`rtl:-scale-x-100`; no linter can see an icon's direction, so this stays a
review rule.

### D26 — Bilingual UI with Transloco from the first component
`ACCEPTED`

Transloco provides runtime internationalization with Arabic and English
translation files. Arabic is the default. The selected language is persisted
in `localStorage`, and the document `dir` switches between `rtl` and `ltr`
when the language changes.

All user-facing strings come from translation keys. No literal user-facing
string is allowed in components, templates, services or other UI code.

A CI script fails the build if any key exists in one of `ar.json` / `en.json`
and not the other, or if a key used in code is missing. A missing key is a
build issue, never silently untranslated text.

**Why:** the application requires runtime language switching between Arabic
and English. Angular's built-in i18n resolves translations at build time (one
build per language), which does not fit. Transloco loads translations at
runtime, supports lazy-loaded scoped translations, and keeps translation
resources separate from code.

**Implementation.**
- **Files:** `public/i18n/ar.json` and `en.json`, served as `/i18n/<lang>.json`.
  A feature scope lives in `public/i18n/<scope>/{ar,en}.json` and loads lazily
  with `provideTranslocoScope`; its keys are `<scope>.<key>`. The package is
  `@jsverse/transloco` (the renamed `@ngneat/transloco`).
- **Language state:** `LanguageService` holds the language as a signal, saved in
  `localStorage` (`clinicbooking.lang`, validated: anything but `ar`/`en`
  falls back to Arabic; a failing storage never crashes the app), and mirrors it
  to `document.documentElement` `lang` and `dir` and to Transloco.
- **Before first paint:** `index.html` ships `lang="ar" dir="rtl"` plus a tiny
  inline script that corrects both from the saved choice; an app initializer
  loads the active language file before the first render, so raw keys never
  flash. (A future CSP needs a hash for that script.)
- **Missing keys:** there is no fallback language; the check below keeps a
  missing key from reaching users.

**The check (`npm run check:i18n`, part of `prebuild`).** It fails when: a folder
under `public/i18n` lacks `ar.json` or `en.json`, the key sets differ, the
`{{placeholders}}` of a key differ, or a value is empty or not a string; a key
used in a template or in code (`'key' | transloco`, `t('key')`,
`.translate/.selectTranslate('key')`) does not exist; a component template
contains literal text, a literal `title`/`alt`/`placeholder`/`aria-label`, or an
interpolation with a literal string; a component uses an inline `template`.
**Limits:** a key built at runtime cannot be verified, so that line must carry a
comment `i18n-keys: a.b, c.d` listing every key it can be (those are verified);
literal strings in `.ts` code are not detected (review and lint); the text
detection is a heuristic on the template source; unused keys are warnings. The
API's `error.*` keys are checked since D52: the check reads them from the C#
source and fails when one lacks an Arabic or English translation (D52 explains
how false positives and negatives are handled, and the limits).

### D27 — Latin numerals and Gregorian dates in both languages
`ACCEPTED`

Use `Intl.DateTimeFormat` / `Intl.NumberFormat` with explicit locale
extensions (`ar-EG-u-nu-latn-ca-gregory`), because plain `ar-EG` produces
Arabic-Indic digits. Angular's `DatePipe` follows `LOCALE_ID` and is **not**
used for display; a custom pipe over `Intl` is used instead.

**Why:** Egyptian clinical and administrative practice uses Latin digits.
Arabic-Indic numerals would look wrong to the intended user.

**Implementation.** The `intl` pipe (`{{ value | intl: 'date' }}`; kinds `date`,
`time`, `datetime`, `number`, `percent`; optional `Intl` options) over the pure
function `formatIntl`. Locales: Arabic `ar-EG-u-nu-latn-ca-gregory`, English
`en-GB-u-nu-latn-ca-gregory` (day first, 24-hour clock). Instants are shown in
**Africa/Cairo** (D12); a date-only string such as `2026-10-04` is a calendar
day, shown as such in UTC (never shifted) and valid only for the `date` kind.
`null`, invalid or unsupported input renders as an empty string. The pipe is
impure because its output depends on the active language. Tested for Latin
digits, Cairo winter (UTC+2) and summer (UTC+3), calendar days and invalid input.

### D28 — Language of code vs. language of interface
`ACCEPTED`

Code, database schema, commit messages and documentation are in English. Only
the user interface is bilingual. Patient names and free-text content are
stored as entered.

### D33 — Front-end state: Signals + services, no state library
`ACCEPTED`

Server data is fetched through injectable services wrapping `HttpClient`
(`httpResource` / `rxResource` where they fit). Local and derived state uses
Signals. No NgRx, no TanStack Query, no other state library.

**Why:** nearly all state is server state, and Angular's built-in primitives
cover it. This also respects the "no new package without agreement" rule.

---

## Security and data

### D29 — JWT authentication and refresh tokens
`ACCEPTED`

JWT bearer authentication with multiple users stored in ASP.NET Core
Identity, passwords hashed by the framework. Access tokens are short-lived.
Refresh tokens obtain new access tokens without re-login, are rotated on
every use, are invalidated on logout or revocation, and reuse of an already
rotated token revokes that token family. A short grace window tolerates
concurrent refreshes (D48).

The refresh token is delivered in an `HttpOnly`, `Secure`, `SameSite=Strict`
cookie (front end and API share an origin, D15). It is never placed in
`localStorage`. Login has lockout and rate limiting. Concrete values: D48.

Authentication establishes identity; authorization (D34) controls access.

**Why:** multiple users and refresh tokens provide a realistic flow without an
external identity provider, and the design can be replaced by one later
without changing the authorization model.

### D34 — Fine-grained, clinic-scoped permissions
`ACCEPTED`

Authorization is claim-based, using fine-grained permission names such as
`patients.create`, `doctors.edit`, `appointments.cancel`.

- A user is granted permissions **per clinic**. Assignments are stored in the
  database (user, clinic, permission), not packed into the JWT, so the token
  stays small and revocation takes effect immediately. The per-clinic table
  arrives with user management (Phase 1), before Doctors (D55).
- Permissions that are not clinic-specific are global: for example
  `users.manage`, `specialties.manage`, `clinics.manage` (managing the clinic
  list itself cannot be scoped to a clinic that does not exist yet, D55), and all
  `patients.*`, because
  patients are shared across clinics (D44). Global permissions are stored as
  Identity user claims of type `permission` (D48), also read from the database
  on every check.
- Each permission is enforced by an authorization policy and handler. The
  handler resolves the target clinic from the resource and checks the
  assignment.
- Permission names are constants defined in one place in Domain; no magic
  strings elsewhere.

**Why:** a doctor can work in several clinics, and a receptionist at one clinic
must not act on another. Coarse roles cannot express that.

### D35 — Soft delete
`ACCEPTED`

Soft delete applies to an explicit list of entities: **Specialties, Clinics,
Doctors, Patients**. They carry `IsDeleted`, `DeletedAt`, `DeletedBy`, hidden
by a global EF query filter. Their unique indexes are filtered with
`WHERE IsDeleted = 0` so a deleted record does not block re-creation.

Appointments are never deleted: they are cancelled by status. Join tables,
slot-duration history and refresh tokens are hard-deleted. Identity tables
follow Identity's own rules.

Three tiers in Domain, so an entity carries only what it needs:

- `BaseEntity`: `long Id`.
- `AuditableEntity : BaseEntity`: adds the audit fields (D36), via `IAuditable`.
- `SoftDeletableEntity : AuditableEntity`: adds the soft-delete fields, via
  `ISoftDeletable`.

Deleting a doctor who has upcoming appointments requires explicit
confirmation in the UI stating that those appointments will be cancelled; on
confirmation the cancellations and the delete happen in one transaction.

### D36 — Auditing fields on the base entity
`ACCEPTED`

A base entity carries `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`,
populated by a `SaveChanges` interceptor from Phase 0. The full audit trail
(who changed what, before/after) remains Phase 2.

### D37 — Bilingual data and Arabic search
`ACCEPTED`

Reference data such as Specialties stores `NameAr` and `NameEn`. Search over
Arabic text normalises أ/إ/ا, ة/ه and ى/ي. **Mechanism (D49): normalised
columns computed in code**, not a collation (none folds these letters, D46).

### D38 — Patient data
`ACCEPTED`

- A patient stores a name and a phone number only. Any further field needs
  a new decision.
- Phone numbers are stored in a single normalised format (E.164).
- National ID is not stored unless a later decision adds it.
- Patient PII is never written to logs.
- Egypt's Personal Data Protection Law (Law 151 of 2020) is reviewed before
  Phase 4, and earlier if any feature stores more than contact details.

### D39 — Migrations in production
`ACCEPTED`

CI builds an EF migrations bundle (`dotnet ef migrations bundle`) and runs it
as an Azure Container Apps Job before the new revision receives traffic.
Migrations are never applied by hand.

### D40 — CI to Azure without stored secrets
`ACCEPTED`

GitHub Actions authenticates to Azure with OIDC federated credentials, not a
stored service-principal secret. Where possible, the app connects to Azure SQL
using Managed Identity / Entra authentication, so no connection string
contains a password.

---

## Appointments

### D30 — Integration tests with Testcontainers; no unit tests for CRUD
`ACCEPTED`

Tests run against a real SQL Server container via Testcontainers and hit the
API through `WebApplicationFactory`. Unit tests are written only for logic
with real branching — overlap detection, working-hours validation, booking
reference generation.

**Why:** a unit test that mocks a `DbContext` to verify a `Select` tests the
mock, not the code.

### D31 — Appointment concurrency and double-booking protection
`ACCEPTED` (mechanism: D43)

Booking and rescheduling must be safe under concurrent requests. Checking for
an available slot in application code alone is not sufficient, because two
requests can both see the slot as free before either commits.

The booking operation uses an explicit transaction and a database-enforced
mechanism (a filtered unique index on the doctor's slot, D43) so conflicting
appointments cannot both commit. A concurrency
conflict is an expected business outcome, returned as a controlled
`ProblemDetails` with an error key (D10). If an EF execution strategy with
retries is enabled, the whole transaction is wrapped inside it.

Integration tests verify that concurrent booking attempts for the same doctor
and overlapping time range produce at most one successful appointment.

**Why:** the database is the final authority for persisted consistency; the
application is responsible for presenting conflicts as a controlled business
error.

### D32 — Multi-clinic model
`ACCEPTED`

A doctor may work in more than one clinic. `DoctorClinic` links them, and
working hours belong to a (doctor, clinic) pair and may contain several
periods in one day. The no-overlap rule applies to the **doctor across all
clinics**, because a doctor is one person. For the same reason, a doctor's
working-hour periods in different clinics must not overlap on the same day;
this is validated when working hours are saved. Slot duration belongs to the
doctor, not to the (doctor, clinic) pair (D43).

### D41 — Booking rules
`ACCEPTED`

- Status machine: `Booked → Completed | Cancelled | NoShow`. A cancelled
  appointment frees its slot; a completed one does not.
- No booking in the past.
- A patient with an overlapping appointment (even with a different doctor)
  triggers a **warning** that the user must confirm. It is not blocked and
  not enforced by the database.
- Doctor breaks and leave days are modelled so working hours can exclude
  them.
- Slot rules are defined in D43.
- Booking references are unique and collision-checked on insert.

### D43 — Fixed slots per doctor
`ACCEPTED`

- Each doctor has **one slot duration**, applied in every clinic they work
  in. It is stored as history rows: `(DoctorId, SlotMinutes, EffectiveFrom)`,
  where `EffectiveFrom` is a Cairo calendar date.
- A new duration can only take effect on a date **later than the doctor's last
  active appointment**. Each day therefore has exactly one duration, and
  existing appointments are never touched.
- The grid is anchored on the start of the working-hour period: an
  appointment must start at `period start + n × duration` and end inside the
  period. This is validated in Application.
- An appointment occupies **exactly one slot**. Procedures longer than one
  slot are not supported; if they appear, a new decision is needed.
- A change to working hours that would leave an active future appointment
  outside the period or off the grid is rejected.
- **Database enforcement:** a unique filtered index on
  `(DoctorId, StartUtc) WHERE Status <> Cancelled`. Appointments have no
  `IsDeleted` (D35). It is
  per doctor, not per clinic, so it also stops a doctor being booked in two
  clinics at the same time. A unique-violation error on booking or
  reschedule is translated to 409 `error.appointment.slot_taken`.
- Booking and rescheduling run in a transaction; the concurrency integration
  tests required by D31 target this index.

**Why:** with a single duration per doctor and one appointment per slot, a
plain unique index is a complete overlap guard, with no locks to reason
about. A per-clinic duration would give one doctor different grids in
different clinics, which the index could not reconcile.

### D44 — Patients are shared across clinics
`ACCEPTED`

- A patient belongs to no clinic. There is no patient-to-clinic link.
- A patient stores a name and a phone number only (D38).
- `patients.*` permissions are global (D34). `patients.read` returns the name
  and phone.
- On create and edit, a matching phone number triggers a **duplicate
  warning** showing the matched name and phone; the user may continue. There
  is no unique constraint on phone, because family members share numbers.

**Why:** the same person visits several clinics, and one record avoids
duplicates. A separate "read all" permission was rejected because different
staff seeing different data is confusing; with only a name and phone stored,
a single read permission is enough.

**Revisit:** when Phase 4 adds medical records, visibility of that data must
be decided separately and will probably be clinic-scoped.

---

## Repository conventions

### D42 — Documentation location and AI entry point
`ACCEPTED`

All documentation lives in `ClinicBooking/docs`. A short `CLAUDE.md` in the
repository root tells AI assistants to read `docs/Instructions.md` and
`docs/decisions.md` before writing code.

---
### D45 — Build, logging and runtime conventions
`ACCEPTED`

- `TreatWarningsAsErrors` is on; implicit usings are off (`GlobalUsings.cs`
  is the only source of usings); tests use xunit v2. Migrations are excluded
  from analyzer rules in `.editorconfig`; NU190x audit warnings are not fatal.
- Serilog writes compact JSON (`RenderedCompactJsonFormatter`) to the console,
  configured in code. Each request carries a correlation ID: an incoming
  `X-Correlation-Id` is accepted only if it matches `[A-Za-z0-9_-]{1,64}`,
  otherwise a GUID is generated; it is returned in the response header.
  Request logs contain method, path, status and elapsed time only.
  `/health` requests log at Debug.
- ProblemDetails carries both `traceId` and `correlationId`. Framework
  responses use keys: `error.http.<status>`, `error.validation.failed`,
  `error.validation.invalid`, `error.unexpected`, `error.health.not_ready`.
- Domain exceptions: `InvalidRequestException` (400), `NotFoundException`
  (404), `ConflictException` (409), `BusinessRuleException` (422); each
  carries an error key.
- Exception messages must never contain patient data, and
  `EnableSensitiveDataLogging` stays off (rule 10).
- Local SQL Server image is pinned (`2022-CU27-ubuntu-22.04`), published on
  127.0.0.1 only. `TrustServerCertificate=True` is for the local container
  only. The runtime image is the standard Debian one (tzdata, D12).
- `/health/ready` uses a custom check (`SELECT 1`, 3 s timeout) and logs only
  the exception type on failure.

### D46 — Persistence choices
`ACCEPTED`

- **Collation:** the database keeps the server default
  (`SQL_Latin1_General_CP1_CI_AS`, also Azure SQL's default). Arabic text
  columns are `nvarchar`, never `varchar`. No SQL Server collation folds
  أ/إ/ا, ة/ه or ى/ي (checked on SQL Server 2022 against `Arabic_CI_AI`,
  `Arabic_100_CI_AI`, `Arabic_CI_AS`), so D37's normalisation will come from
  normalised search data added with the search feature. (That gap, where a
  unique index on `NameAr` treated `أحمد` and `احمد` as different names, is
  closed by D49.)
- **Soft delete filter:** a named EF query filter, `"SoftDelete"`, applied by
  `AppDbContext` to every `ISoftDeletable` entity.
- **Save interceptor** (`AuditSaveChangesInterceptor`, uses `TimeProvider` and
  `IUser`): on create sets `CreatedAt`/`CreatedBy` (`UpdatedAt` stays null); on
  update sets `UpdatedAt`/`UpdatedBy` and never touches `Created*`; `Remove()`
  of a soft-deletable entity becomes an update that sets `IsDeleted`,
  `DeletedAt`, `DeletedBy` and `UpdatedAt`/`UpdatedBy`. Soft-deleting a parent
  does not cascade; services do that.
- **`ExecuteUpdate` / `ExecuteDelete` are banned** in `src/`: they bypass the
  interceptor. A test scans the sources and reports file and line.
- **`IUser`** (Application) exposes `long? Id`; null means system or
  anonymous. Api implements it from the JWT claims. Until the auth step there
  are no claims, so it is always null. `*By` columns are nullable `bigint`.
- **Migrations:** applied at startup in Development only. `docker-compose.yml`
  is production-shaped; `docker-compose.override.yml` (loaded automatically by
  `docker compose up`, never by CI or Azure) sets `ASPNETCORE_ENVIRONMENT=
  Development`. Tests that use a database run in Development so the real
  migrations are applied.
- **No retry strategy** (`EnableRetryOnFailure`) yet. It must be reconsidered
  before deployment, for Azure SQL transient faults, together with the
  transaction wrapper D31 requires.
- **Local tool:** `dotnet-ef` is pinned in `.config/dotnet-tools.json`
  (10.0.x, matching EF Core). `Microsoft.EntityFrameworkCore.Design` lives in
  Api, the startup project.
- **Tests (D30):** one SQL Server Testcontainers container per test run, one
  fresh database per test class (dropped afterwards), `TimeProvider` and
  `IUser` replaced in tests. `dotnet test` needs Docker running.
- EF's SQL command logging is raised to Warning; migration messages stay at
  Information.
---

### D47 — CI conventions
`ACCEPTED`

- `global.json` pins the SDK to at least 10.0.100 with
  `rollForward: latestFeature`. The CI setup step and the Dockerfile both use
  it, so local, CI and the image follow one rule.
- Every GitHub Action is pinned to a full commit SHA with a version comment.
  Only GitHub-owned actions (`actions/*`) are used; no `docker/*` actions.
  Runner is `ubuntu-24.04`, not `ubuntu-latest`. Checkout uses
  `persist-credentials: false`.
- CI relies on `Directory.Build.props` (D45) for warnings as errors and does
  not pass `-warnaserror`, so NU1901–NU1904 audit warnings stay non-fatal.
- Three parallel jobs, `test`, `web` and `image`; a later push job will `needs`
  all three. The test job uploads a TRX artifact (7 days). No test-logger package.
- **`web` job (front end).** Ordered steps: Node-version guard, `setup-node`,
  `npm ci`, `npm run lint` (angular-eslint and the logical-properties check),
  `npm test` (Vitest and the Node script tests), `npm run check:i18n`,
  `npm run check:api`, `npm run build`. After a failed step the later ones still
  run, so one run shows every problem, **but only if `npm ci` succeeded**; the job
  still fails. `check:api` needs only Node; on failure it adds a GitHub annotation
  telling the developer to run `npm run gen:api` and commit `openapi.json` and
  `schema.d.ts`. `npm run build` repeats the two prebuild checks (a second);
  `image` repeats `npm ci` and the build inside Docker (about a minute): accepted,
  because `image` proves the packaging (copy paths, Node base image, bundle into
  `wwwroot`) and the two jobs run in parallel.
- **One Node major.** `actions/setup-node` v7.0.0
  (`820762786026740c76f36085b0efc47a31fe5020`) is used because the runner image
  ships Node 22.23.3, which the project (`engines` `^24.15.0`) rejects.
  `src/clinic-booking-web/.nvmrc` (`24`) feeds `setup-node` and local version
  managers; `src/clinic-booking-web/.npmrc` sets `engine-strict=true`, so `npm ci`
  fails on a Node outside `engines` (locally, in CI and in the image build, where
  the Dockerfile copies `.npmrc` with the package files).
  `.github/scripts/check-node-version.sh` fails the build when the major in
  `.nvmrc`, the Dockerfile's `FROM node:<major>` and `engines` differ, **and also
  when any of the three cannot be parsed** (alias such as `lts/*`, no `FROM node`
  line, no or ambiguous `engines`); it takes paths as arguments so it can be tested.
- npm is not cached (`package-manager-cache: false`): a cold `npm ci` takes about
  25 seconds. Revisit with the 5-minute rule above.
- Concurrency: group is workflow + ref with `cancel-in-progress: true`. Must
  be revisited when a deploy workflow exists, because an in-flight deploy
  must not be cancelled.
- No caching (NuGet or Docker layers) until a run exceeds about 5 minutes.
- The SQL Server image tag is declared in both `docker-compose.yml` and the
  Testcontainers fixture; keep them in sync.

### D48 — Authentication and authorization specifics
`ACCEPTED` (refines D29 and D34)

**Layers.** `AuthService` (Application) holds the rules: login, rotation,
reuse detection, logout. It depends on `IAppDbContext`, `TimeProvider` and
three interfaces implemented in Infrastructure: `IIdentityService`
(credentials, lockout; wraps `UserManager`), `IAccessTokenService` (JWT) and
`IPermissionChecker`. `SignInManager` is not used (it is cookie-oriented).

**Identity.** `ApplicationUser : IdentityUser<long>` in Infrastructure,
audited (`IAuditable`) but not soft-deletable. `AppDbContext` is an
`IdentityUserContext`: users and their claims only, **no role tables**
(permissions are claims; roles are never checked, rule 9). Failed-login
bookkeeping updates `UpdatedAt`, accepted until the Phase 2 audit trail.

**Access token.** HS256 JWT, 15 minutes, claims `sub` and `jti` only (plus
iat/nbf/exp). Issuer and audience are non-secret settings. The signing key is
`Jwt:SigningKey` (compose maps `JWT_SIGNING_KEY`), at least 32 bytes; the host
**fails at startup** if it is missing or short, in every environment. Tests
supply a throwaway key; there is no Testing exemption. Clock skew 30 s.
Inbound claim mapping is off; `CurrentUser` reads `sub`, so `CreatedBy` holds
the real user id. Lifetime is validated against `TimeProvider` through a
custom `LifetimeValidator` (IdentityModel has no clock hook). IdentityModel's
token API takes `DateTime`; instants are converted at that boundary only.

**Refresh token.** 32 random bytes, stored only as a SHA-256 hash. Valid 7
days, sliding on each rotation, with a 30-day absolute cap per session
(family). Every use consumes the token and issues a new one in the same
family. A consumed token presented again **within 10 seconds**
(`Auth:ReuseGraceSeconds`) is rejected with 401 without revoking anything
(concurrent refreshes); **after** the window the whole family is revoked. A
rowversion makes simultaneous rotations fail cleanly (treated like the grace
case). On refresh the user must still exist and not be locked out, otherwise
the family is revoked. Logout revokes the family and is idempotent. A user's
long-expired rows are removed when that user logs in (no purge job until
Phase 5).

**Cookie.** `refresh_token`, `HttpOnly`, `Secure` (always set, also over
`http://localhost`, which Chrome and Firefox accept), `SameSite=Strict`,
`Path=/api/auth`, no Domain. The `__Host-` prefix is not used because it needs
`Path=/`. CSRF defence in depth: refresh and logout are POST-only and reject a
request whose `Origin` is neither this host (compared without scheme, so it
works behind a TLS-terminating proxy) nor in `Auth:AllowedOrigins` (the
Development file allows the Angular dev server).

**Lockout, rate limit, passwords.** 5 failed attempts lock the account for 15
minutes. The lockout is checked **before** the password, so the answer never
depends on whether the password was right; an unknown user is verified against
a dummy hash. The built-in rate limiter (per client address, fixed one-minute
window) allows 10 logins and 30 refreshes per minute, configurable under
`RateLimiting`. Password policy: at least 12 characters, one upper-case, one
lower-case, one digit, at least 4 distinct characters; no breached-password
check.

**Error keys.** Wrong password or unknown user: 401
`error.auth.invalid_credentials`. Locked out: 423 `error.auth.locked_out`
with `Retry-After` (this reveals that the account exists once it is locked;
accepted). Rate limited: 429 `error.auth.rate_limited` with `Retry-After`.
(D52 adds the front-end side: session, refresh and the grace-window retry.)
Refresh token missing, invalid, expired, revoked or reused: 401
`error.auth.invalid_refresh_token`, and the cookie is cleared. No or invalid
access token: 401 `error.auth.unauthorized`. Missing permission or foreign
origin: 403 `error.auth.forbidden`. Domain exceptions added: `Unauthorized`
(401), `Forbidden` (403), `AccountLocked` (423).

**Permissions.** Constants in one place (`Domain/Permissions/Permissions.cs`),
`Global` and `All` lists. One authorization policy per permission, named after
it, with a single `PermissionAuthorizationHandler`. Global grants are Identity
user claims (`permission`); the handler reads the database per check, so a
revoked permission applies at once (a locked-out user keeps an issued access
token until it expires). When Clinics arrive, the handler resolves the clinic
from `context.Resource` for non-global permissions and a clinic-scoped
checker reads the (user, clinic, permission) table; nothing of that exists yet.

**Seeding.** `Seed:AdminUserName` / `Seed:AdminPassword` (compose maps
`SEED_ADMIN_USERNAME` / `SEED_ADMIN_PASSWORD`). Runs at startup in every
environment when both are set. It creates the user only when no user exists, and
grants every global permission. When users already exist it only **tops up** the
user named by `Seed:AdminUserName`: it adds the permissions of
`Permissions.Global` that user lacks, so a permission added to the code later
reaches the seeded admin (D55). It never creates a user, never touches a password
or another user, never removes a permission, and logs only a count. The password
is never logged. **The seed variables must be removed from configuration after
the first successful deploy**, which also stops the top-up: from then on new
global permissions are granted through user management. A change-password flow
(Phase 1 user management) **must exist before any real data** is stored.

**Endpoints.** `POST /api/auth/login`, `POST /api/auth/refresh`,
`POST /api/auth/logout`, `GET /api/auth/me` (id, user name, permissions: the
token carries none, so a front end needs this). Until the FluentValidation
endpoint filter (D9) exists, login checks only that both fields are present
and at most 256 characters.

**Deploy-step items.** (1) Forwarded-headers handling behind the Azure
ingress, so rate limiting sees the client address and `Request.Host` is the
public host. (2) Key Vault for `Jwt__SigningKey` and the seed values.

**Front end.** The Angular step must call refresh **single-flight**: at most
one refresh request in flight, all waiting callers share its result.

### D49 — Arabic-aware search and uniqueness of names
`ACCEPTED` (completes D37)

**Mechanism.** `SearchText.Normalize` (Domain) is the one normalisation. It is
computed in code and stored in plain `nvarchar(100)` columns next to the
display text (`NameArNormalized`, `NameEnNormalized`); a SQL computed column
would duplicate the logic and let stored value and query drift. The same
function normalises the search term. The pipeline: Unicode NFKC; remove Arabic
diacritics (U+064B–U+065F, U+0670), tatweel and invisible format characters
(ZWJ, ZWNJ, LRM, RLM, BOM); fold `أ إ آ ٱ → ا`, `ة → ه`, `ى → ي`; fold
Arabic-Indic and Persian digits to `0-9` (consistent with D27); lower-case;
collapse whitespace; trim. `ؤ` and `ئ` are deliberately **not** folded (not in
D37). English text goes through the same function: case-insensitive, whitespace
collapsed, no accent folding.

**Entity shape.** The display names and the normalised copies have private
setters and are set together by `SetNames`, so a caller cannot forget one. The
display name is stored as entered (trimmed, D28).

**Search.** One `search` parameter matches either name. The normalised query is
split on spaces and **every word** must match one of the two normalised
columns (`Contains`, wildcard characters treated literally). A leading-wildcard
scan is acceptable for reference data of a few dozen rows; Patients will need a
prefix or full-text approach.

**Uniqueness.** Reference data (Specialties, Clinics) is unique over the
**normalised** text, per language, among live rows (`WHERE IsDeleted = 0`), so
`أحمد`/`احمد` or `Cardiology`/` cardiology ` are the same name. **This applies
to reference data only.** Patients (D44) get a normalised column for search
only and **no unique constraint on names**: different people share names.

**Sorting.** Arabic order is the normalised text (alphabetical, ignoring hamza
forms and diacritics), so no collation function is needed.

### D50 — Specialties API and the pattern for later entities
`ACCEPTED`

Specialties is the reference implementation; Clinics, Doctors and Patients
copy it.

- **Endpoints** (`/api/specialties`): `GET` (list), `GET {id}`, `POST`
  (201 + `Location`), `PUT {id}`, `DELETE {id}` (204). List query: `search`,
  `page` (1), `pageSize` (20, max 100), `sortBy` (`nameEn` default, `nameAr`,
  `createdAt`), `sortDirection` (`asc` default, `desc`) (the query object binds
  case-insensitively and the OpenAPI document names them `Search`, `Page`,
  `PageSize`, `SortBy`, `SortDirection`, which the front end sends, D53); response
  `{ items, page, pageSize, totalCount }`. Ties are broken by `Id` (by `Id`
  descending for newest-first). DTOs only: responses carry id, both names,
  `createdAt`, `updatedAt` and `rowVersion`; never the normalised columns or
  audit user ids.
- **Permissions.** Any signed-in user may read (every staff member needs the
  list); `specialties.manage` is required to create, edit and delete.
- **Secure by default.** A **fallback authorization policy** requires an
  authenticated user for any endpoint without authorization metadata, **also for
  requests that match no endpoint**, so an anonymous call to an unknown route is
  401, not 404. Anonymous endpoints say so explicitly: `[AllowAnonymous]` on
  login, refresh and logout, `.AllowAnonymous()` on health and on OpenAPI.
  Static files and the SPA fallback are anonymous too (D51). A test proves an
  action without attributes returns 401.
- **Validation.** One FluentValidation validator per request DTO and per query
  object, in `Application/Validators`, found by assembly scanning. `ValidationFilter`
  (global action filter) runs them and returns 400 `error.validation.failed`
  with camelCase field names and error-key messages; a message that is not an
  `error.` key is replaced by `error.validation.invalid` (rule 4). A reflection
  test fails if any request DTO of a production action has no validator.
- **Concurrency.** `RowVersion` (SQL `rowversion`) on every auditable entity.
  `PUT` must send it back; a stale value is 409 `error.concurrency.conflict`
  (checked in the service and again by EF at save). `DELETE` needs no version.
- **Duplicates.** The service pre-checks for a friendly 409
  (`error.specialty.name_ar_taken` / `name_en_taken`, Arabic reported first if
  both clash); the unique indexes are the real guard. A unique-index violation
  becomes a 409 `ConflictException` **only** for indexes carrying the
  `ClinicBooking:ConflictKey` annotation (the key to return); every other
  unique violation (Identity, refresh tokens) is rethrown unchanged. Matching
  uses the ASCII index name inside the SQL error text, not its language. D43's
  `slot_taken` uses the same mechanism.
- **Error keys.** `error.specialty.name_ar_required|too_long|invalid` (and
  `name_en_*`), `error.specialty.not_found` (404), `error.paging.page_invalid`,
  `error.paging.page_size_invalid`, `error.sort.invalid`,
  `error.search.too_long`, `error.concurrency.row_version_required|invalid`,
  `error.concurrency.conflict`. Login validation keys: `error.auth.user_name_required`,
  `error.auth.password_required`, `error.auth.field_too_long`.
- **Delete** is a soft delete through `Remove()` (D35); a second delete is 404
  and the name can be re-created. **Later**, when a Doctor uses a Specialty,
  deleting it returns 409 `error.specialty.in_use` (no cascade); soft-deleted
  doctors keep their reference, which stays valid.
- **Queries** project with `Select` into DTOs from one hand-written expression
  (`SpecialtyMapping`); updates and deletes load the entity.
- **OpenAPI.** `Microsoft.AspNetCore.OpenApi` generates the document;
  `/openapi/v1.json` is served only when `OpenApi:Enabled=true` (Development,
  tests). A transformer removes the machine-specific `servers` entry and
  non-JSON media types. See D24 for the regeneration command.
- **Do not use `[Produces]`** on controllers: it overrides the
  `application/problem+json` type of model-binding failures. The OpenAPI
  transformer keeps the document clean instead.

### D51 — Front-end foundation and hosting
`ACCEPTED` (implements D15, D22, D24–D27)

**Workspace.** `src/clinic-booking-web`, project `clinic-booking-web`, selector
prefix `cb`, standalone components only, `strict` and `strictTemplates`,
OnPush, no `any`, `inject()`, Signals, `templateUrl` only (the translation check
needs to read templates), no zone.js. File names follow the CLI's current style
(`app.ts`, `language-switcher.ts`; no `.component` suffix). The CLI's demo
template, README and `.vscode` are not kept.

**Packages beyond the CLI's** (rule 8, agreed): `tailwindcss`,
`@tailwindcss/postcss` and `postcss` (Tailwind 4 is wired through
`.postcssrc.json` and `@import "tailwindcss"`), `@jsverse/transloco`,
`openapi-typescript`, `eslint`, `typescript-eslint` and `angular-eslint`. The
checks and the `gen:api` script are plain Node with no dependency, and their
own tests use Node's built-in `node --test`. `package-lock.json` is committed
and the image uses `npm ci`; `engines` requires Node `^24.15.0`.

**Scripts.** `build` (with a `prebuild` that runs `check:logical` and
`check:i18n`), `lint` (angular-eslint, then `check:logical`), `test`
(`ng test --no-watch`, then the Node script tests), `check:i18n`,
`check:logical`, `gen:api`, `check:api`. Lint config: `eslint.config.mjs`.

**Hosting.** The API serves the bundle from `wwwroot`. `UseStaticFiles` runs
before logging, correlation and authorization, so the fallback authorization
policy (D50) never sees static files; the SPA fallback endpoints are explicitly
`AllowAnonymous`. The fallback catch-all excludes paths whose first segment is
**exactly** `api`, `health` or `openapi` (`/apiary` is still a client route) and
any path with a file extension (a missing `/x.js` is never HTML); the root `/`
is mapped by its own fallback because a constrained catch-all does not match the
empty path. Excluded paths keep today's behaviour: ProblemDetails 401
(anonymous) or 404 (signed in), never `index.html`. `MapStaticAssets` is not
used: it only knows files present at publish time, and the bundle is copied in
afterwards. Cache headers: `index.html` and `/i18n/*` are `no-cache`;
fingerprinted `*.js`/`*.css` are `public,max-age=31536000,immutable`. Tests use a
temporary web root, so production code carries nothing test-only.

**Shell.** A header (title, language switcher) and a router outlet with a
placeholder; unknown client routes redirect to it. The document title follows
the language.

**Verification.** `npm ci`, `npm run build`, `npm run lint`, `npm test`,
`npm run check:api`, and the .NET tests. The CI `web` job runs the front-end
ones (D47); the `image` job builds the front end again through Docker.

**Deferred (done in D52).** The dev proxy for `ng serve` and the back-end
`error.*` coverage check came with the login step.

---

### D52 — Front-end authentication
`ACCEPTED` (implements D29 and D48 on the client; refines D26 and D51)

**Session.** `SessionService` keeps the access token in a private field:
**never** in `localStorage` or `sessionStorage` (a test checks both stay
empty). State is `unknown` → `authenticated` | `anonymous` (Signals), plus the
user and permission set from `GET /api/auth/me`. The refresh cookie is
`HttpOnly`, so scripts never see it.

**Startup.** An app initializer calls `POST /api/auth/refresh` then `/me` before
the first render, so a signed-in user never sees a flash of the login page.
It never rejects and gives up after 10 s; a 401, 429, 5xx, network failure or
timeout all mean **anonymous at once, with no retry** (a logged-out visitor
must not wait). `index.html` shows a text-free CSS spinner meanwhile (literal
text there would break D26). No "was signed in" hint is stored: the one extra
401 for a logged-out visitor is cheaper than a flag that can drift.
*Known edge:* two tabs opened at the same moment both refresh at startup; the
loser gets 401 and shows the login page although the other tab is signed in.
Accepted: it is rare, and a reload fixes it (the cookie is then valid).

**Interceptor.** The `Authorization` header goes **only** on same-origin
`/api/` requests, and never on `login`, `refresh` or `logout`. Translation
files, other origins and any other URL get no header and never trigger a
refresh. On a 401 titled `error.auth.unauthorized` the request is refreshed
once and retried once: concurrent 401s share **one** refresh; a request that
carried an older token than the current one retries with the current token and
no new refresh; a retry rejected again ends the session. A 403, or a 401 with
another key, is passed through. A request sent without a token never
refreshes. Refresh is **reactive only**: a 401 path must exist anyway, timers
are throttled in hidden tabs, client and server clocks differ, and proactive
refresh in several tabs would create the very collisions the grace window
absorbs.

**Grace window and two tabs.** Tabs share the cookie jar. Within
`Auth:ReuseGraceSeconds` (10 s) a refresh that loses the race gets 401
`invalid_refresh_token` without any revocation, while the winner's new cookie is
already in the jar. During an **active session** the client therefore retries
the refresh **once** after 1 s plus 0–500 ms of jitter (inside the shared
single flight, so callers still see one logical refresh). A second 401 means the
session really ended: state is cleared and the user goes to
`/login?returnUrl=<current>` with a one-time "session expired" notice. 429, 5xx
and network failures are **transient**: no logout, no retry, the request fails
with a typed error. Tested with fake timers (exact delay, never a third refresh).

**returnUrl.** `safeReturnUrl` accepts only a single leading `/`, no backslash,
no control characters, not `/login`, at most 2048 characters, with the same
checks again after one decode (`/%2F%2Fevil.com`). Anything else becomes `/`.
It is used only with the router, never `window.location`.

**Routes.** `/login` (guest guard: a signed-in user goes on to the returnUrl),
`/forbidden`, `/` (auth guard), and a catch-all that runs the auth guard first,
so a signed-out deep link goes to login and returns after sign-in, while a
signed-in user lands on `/`. `permissionGuard(name)` sends a signed-in user
without the permission to `/forbidden`.

**Permissions** come from `/me` as strings. `permissions.ts` names the ones the
UI asks about (`users.manage`, `specialties.manage`); `npm run check:permissions`
(plain Node, part of `lint`) fails when one is not defined in `Permissions.cs`.
`*cbCan` and `SessionService.can()` are **UX only**: the API enforces every
permission.

**API client and errors.** `src/api/auth-api.ts` has one method per auth
endpoint; `src/api/types.ts` derives request and response types from
`schema.d.ts` (no hand-written models). `parseApiError` is the only place a
ProblemDetails is read; it returns `{kind, status, key, fieldErrors,
correlationId, retryAfterSeconds}`. A title that is not a well-formed
`error.…` key becomes `error.unexpected` (server text is never shown); status 0
becomes `error.network`. `ErrorMessageService.keyFor` falls back to
`error.unexpected` when the active language lacks the key. The correlation id
is shown only for unexplained failures, so a user can quote it. `error.unexpected`
tells the user to contact the system administrator (there is no support channel).

**Login form.** Typed Signal Forms (`@angular/forms/signals`): the official docs
mark `form()` "stable since v22.0" and the installed Angular is 22.2.1. Rules:
required and at most 256 characters (as the API). Messages are the back-end
keys. Labels, `autocomplete="username"`/`"current-password"`, `aria-invalid`, an
`aria-live` alert region that receives focus on a server error, the first
invalid field focused on submit, the button disabled with `aria-busy` while
pending. Unknown user and wrong password show the same message. 423 and 429
show their message plus "about N min" from `Retry-After`. The password is
cleared after a failure.

**Dev proxy.** `proxy.conf.json` forwards `/api` to `http://localhost:8080`
(`changeOrigin: false`), wired in `angular.json`; `npm start` uses it. The
browser sends `Origin: http://localhost:4200` and the proxy keeps
`Host: localhost:4200`, so the Origin check (D48) passes **without any back-end
change** (`appsettings.Development.json` also lists the origin). A foreign
Origin still gets 403 through the proxy. The `Secure` cookie works on
`http://localhost` in Chrome and Firefox; Safari and non-localhost HTTP hosts do
not store it.

**Back-end `error.*` keys (D26).** `scripts/backend-error-keys.mjs` reads the C#
under `src/ClinicBooking.*` (not `tests/`, `bin`, `obj`, `Migrations`) with a
small scanner that understands C# strings and skips comments, collects literals
shaped `error.<reason>[.<reason>…]`, and `check:i18n` fails when one is missing
in `ar.json` or `en.json`, naming the C# file and line. *False positives:*
comments are skipped and the shape is strict. *False negatives:* a key built at
runtime cannot be read, so any file with a prefix literal (`"error."`) must be
declared in `scripts/backend-error-keys.json` with the keys it can produce (today
`error.http.400/404/405/406/415` from `ProblemDetailsEnricher`, plus
`ValidationFilter`, which only tests a prefix); an undeclared prefix literal or a
stale declaration fails. Without the back-end sources (the Docker `web` stage)
the scan warns and is skipped; **with `CI` set it fails**, so CI cannot skip it.
*Limits:* keys assembled another way (resource files, a database) are invisible;
a new dynamic family must be declared by hand. (While writing it, the first
pattern wrongly required three segments and missed `error.unexpected`; the
tests now pin the two-segment shape.)

**API document fix (separate commit).** The committed `openapi.json` lacked every
200 response schema (an action with any `ProducesResponseType` stops inferring
`ActionResult<T>`) and leaked the test-only controllers. The 200 attributes are
added, the document is generated from a host without test controllers
(`PlainApiFactory`), and tests fail if any operation lacks a documented success
schema (204 excepted), a test path appears, or the auth schemas are missing.

**Deploy-step items (not done here).** The startup refresh counts against the
refresh rate limit (30 per minute per client address), so correct
forwarded-headers handling behind the Azure ingress matters even more: without
it every visitor shares the ingress address and a busy minute can lock silent
sign-in out for all. The Origin check compares the request host, which needs the
same forwarded-headers setup, and the deployed host belongs in
`Auth:AllowedOrigins` if it differs.

**Not verified in a browser.** The credentialed checks (successful login shows
the shell, reload keeps the session, logout returns to login, returnUrl after
login) were not driven in the browser, because the pane could not take a pasted
secret and typing it would expose it. They are covered by unit tests and by real
requests against the running stack (login cookie flags, `/me`, rotation, logout,
and the same through the dev proxy).

---

### D53 — Front-end feature pattern (Specialties screens)
`ACCEPTED` (implements D50 on the client; the reference for Clinics, Doctors and Patients)

**Layout.**
```
src/api/specialties-api.ts            one method per endpoint, types from schema.d.ts
src/app/shared/ui/                    confirm-dialog, pager (no strings of their own)
src/app/features/<entity>/
  <entity>.routes.ts                  lazy routes; the parent route carries the Transloco scope
  <entity>.scope.ts                   scope name + a resolver that preloads the scope file
  list/<entity>-list.ts|html, list-query.ts    the screen; URL <-> query as pure functions
  form/<entity>-form.ts|html          create and edit on one page
  <entity>-session.ts                 last list query + a one-time status message
public/i18n/<entity>/{ar,en}.json     the screen's own strings (keys <scope>.<key>)
```
`/specialties` is behind `authGuard` only (any signed-in user may read, D50);
`/specialties/new` and `/specialties/:id/edit` add `permissionGuard`. The
controls use `*cbCan`. Both are UX: the API enforces permissions.

**Translations.** The screen's own strings live in the scope. **The entity's
`error.*` keys stay in the root `ar.json`/`en.json`**: the back-end key check
(D52) reads the root files, and the error handling that shows them is shared
code. (Instructions.md used to say the keys go in the scope; corrected.) A
resolver loads the scope for the active language before the page renders, so no
raw key flashes; the pipe follows later language changes.

**The list: the URL is the state.** `?q=&page=&size=&sort=&dir=`, defaults
omitted. `parseListState` reads it with clamping (an invalid value becomes the
default, so a bad link never breaks the page or reaches the API); `toQueryParams`
writes it; `toApiQuery` builds the API query, always explicit. Reload and
back/forward need no extra code because the component reads `queryParamMap`.
- **Defaults live in one constant** (`DEFAULT_LIST_STATE`): Arabic name
  ascending (Arabic-first UI), page 1, 20 per page; tests pin the default URL
  and the default API query. Page sizes 10, 20, 50.
- **Loading and stale responses:** an `rxResource` over the parsed query: a newer
  query cancels the older request, so a late response is never shown. The
  previous result stays on screen (`aria-busy`) while the next loads. Retry
  calls `reload()`: the same query, the page is not rebuilt.
- **Search:** a local signal, debounced **300 ms** (fast enough to feel live, slow
  enough not to send a request per keystroke) into a `replaceUrl` navigation so
  back is not polluted; Enter searches at once; input during IME composition is
  ignored until it ends. The raw text is sent, trimmed (the API normalises
  Arabic, D49); blank is omitted; `maxlength` 100. Any search, sort or size
  change returns to page 1.
- **A page past the end** (after a delete or a hand-edited URL) steps back to
  the last page; with no rows at all the "no specialties" state shows.
- **Query names.** The generated types name the list parameters in PascalCase
  (`Search`, `Page`, `PageSize`, `SortBy`, `SortDirection`) because the query
  object binds case-insensitively; the client sends the typed names. D50's
  lowercase spelling was wording only and is updated. `SortBy`/`SortDirection`
  are plain strings in the schema, so the allowed values are mirrored in
  `list-query.ts`.
- **Layout.** A real `<table>` (caption, `th scope`, `aria-sort`) from md up and a
  card list below; both share the action template, and the hidden one is
  `display:none`. One sort control (select + direction button) serves both. The
  name in the UI language is shown first and prominent, the other secondary;
  every name sits in a `<bdi>` with its own `lang`/`dir`. Names are interpolated
  (never `innerHTML`), so stored HTML is shown as text (D28; tested). Pager is
  text-only (no icon to mirror). Dates use the `intl` pipe.

**Create and edit: separate pages, not a modal.** Deep-linkable, back works,
focus is simple, no focus-trap code, and they behave on a phone and in RTL.
Signal Forms; rules mirror the API (trimmed value required, at most 100) with
the back-end keys; values are sent trimmed. Each field carries its own
`lang`/`dir`. After a save the app navigates to the remembered list query
(`<entity>-session`) and the list shows a one-time `role="status"` message and
focuses its heading (every feature page focuses its `h2` on entry).

**Server errors on a form.** A 400's `errors.<field>` keys and the key on a 409
`name_ar_taken`/`name_en_taken` become server errors on the right field, returned
from the `submit()` action, so they render like client errors and clear when the
user edits that field. Anything else is a form-level `aria-live` message through
`ErrorMessageService` (never server text or a raw key). A 404 on save shows
"not found" with a way back.

**Concurrency (409 `error.concurrency.conflict`).** The form keeps what the user
typed and shows a banner (focused, `role=alert`); **Save stays disabled until
the user presses Reload**, so nothing is overwritten silently. Reload fetches the
latest record, puts its values and new `rowVersion` in the form, and lists the
user's earlier entries in a panel they can dismiss, to re-apply by hand. If the
fetch returns 404 the page says the record no longer exists.

**Delete.** A reusable `confirm-dialog` on the native `<dialog>` (`showModal()`:
focus trap, inert background, Esc, focus restoration), focus starting on Cancel,
naming the record in both languages; Esc is ignored while the request runs. On
success it reloads the list, shows a status message and focuses the heading. A
404 means already deleted: the same outcome with its own message. Any other
error stays inside the dialog, translated, so the later 409
`error.specialty.in_use` needs only its translation (the back-end key check
will demand it).

**Shell.** A "Specialties" link in the header for signed-in users
(`routerLinkActive`, `aria-current`).

**Test notes.** jsdom lacks `<dialog>.showModal()`/`close()`: a stand-in lives in
`src/testing/dialog-polyfill.ts`. `whenStable()` hangs while an HTTP request is
pending (resources hold a pending task), so specs settle by hand. rxjs
`debounceTime` reads `Date.now()`, so fake timers must fake `Date` too.

**Known minor points.** The Specialties chunk (about 9 kB) is requested by a
signed-out visitor following a deep link, because the router loads a lazy route
before its guard runs; nothing sensitive is in it, and `canMatch` would avoid it
at the cost of a different fallback. `check:i18n` now ignores the format name in
`| intl: 'date'` (it is not user text).

**Not verified in a browser.** Every signed-in flow (list, search, paging, sort,
create, edit, conflict, delete, focus restoration after the dialog closes) was
not driven in the browser, because that needs the seed password. They are covered
by the component, route and logic specs; focus behaviour that depends on a real
browser (native dialog focus restoration) is the part tests cannot prove.

---

### D54 — Azure deployment is postponed until the build is complete
`ACCEPTED` (replaces the old "no feature before Azure" rule of Scope discipline and the "deployment first" ordering of Phase 0; D16 and D17 stay in force as the target)

**Decision.** On 4 October 2026 the project owner decided to postpone deployment
to Azure until the build is complete, and to deal with any deployment problems
when that point is reached. The owner accepted the risk below knowingly.

**Why.** A new Azure free account gives credit for 30 days. Creating the account
now would spend that time before there is anything worth deploying.

**Risk (accepted).** Problems that only appear on Azure (the ingress, identity,
Key Vault, Azure SQL, migrations) are discovered late and may be more expensive
to fix: this is the argument of D16. The pipeline is only partly proven today:
CI builds, tests and builds the Docker image, but nothing is pushed to a
registry, nothing is deployed, there is no migrations bundle and no Key Vault.

**What stays mandatory meanwhile.**
- CI stays green on every push to `main` and on every pull request (the
  triggers of `ci.yml`), and it is green before the next step starts.
- Every commit builds. The `Dockerfile` and `docker compose up --build` keep
  working (the `image` job and local runs prove it).
- No secret enters the repository (rule 6, D19).
- Each item that is postponed stays on the checklist "Deferred until
  deployment" below, so nothing is forgotten.

**"The build is complete"** is declared by the owner. No phases or dates are
invented here. Phases 1 to 5 in "Scope" remain the scope, worked in order;
Appointments (Phase 2) must not start before Phase 1 is complete. The project is
not called released until every item of "Deferred until deployment" is done.

**Effect on other decisions.** Phase 0 is done in code and deferred in
deployment (see "Scope"). O1 and O2 are deferred with the deployment: no feature
work depends on them. D16 is unchanged: the `Dockerfile` and compose exist and
run; what is late is the cloud half of the pipeline.

---

### D55 — Clinics back end
`ACCEPTED` (follows D50 exactly; refines D34, D38 and D48)

Clinics is the second reference-data entity and copies Specialties: soft delete
(D35), audit fields (D36), `RowVersion` concurrency, names unique after
normalisation (D49) with the annotated unique-index mechanism, projection by
`Select`, one validator per request DTO and query object, error-key
ProblemDetails. No Angular screens yet (the next step).

**Fields.** `NameAr` and `NameEn` (required, trimmed, at most 100, as Specialty);
`Address` (optional free text, trimmed, at most 300, `nvarchar(300)`; blank is
stored as null); `Phone` (optional, `nvarchar(16)`: `+` and up to 15 digits). The
normalised name columns are `nvarchar(100)`; unique filtered indexes
`UX_Clinics_NameArNormalized` / `UX_Clinics_NameEnNormalized` (`WHERE [IsDeleted] =
0`) carry the conflict keys. There is **no** index on the phone: clinics may share
a switchboard.

**Endpoints** (`/api/clinics`, same shape as Specialties): `GET` list, `GET {id}`,
`POST` (201 + `Location`), `PUT {id}`, `DELETE {id}` (204). List query `Search`,
`Page` (1), `PageSize` (20, max 100), `SortBy` (`nameEn` default, `nameAr`,
`createdAt`), `SortDirection`; ties broken by `Id`. Responses carry id, both names,
address, phone, `createdAt`, `updatedAt` and `rowVersion`. **`PUT` is a full
replace:** an omitted or blank address or phone is cleared.

**Search** matches **both names only** (D49). Address and phone are not searched:
addresses are not normalised, and street names would give surprising matches;
phone search belongs to a digit-prefix match, later.

**Permission.** A new **global** permission, `clinics.manage` (create, edit,
delete). It is global because it cannot be scoped to a clinic that does not exist
yet; it is in `Permissions.Global`, so the policy and the handler needed no
change. **Reading (list and get) is allowed for any signed-in user**, like
Specialties. *Revisit:* when per-clinic permission assignments arrive (with user
management, before Doctors) the list may have to be filtered for scoped users, and
D6's "an inaccessible resource returns 404" will then apply; nothing of that
exists yet.

**Error keys** (11 new, translated in the root `ar.json`/`en.json`):
`error.clinic.name_ar_required|too_long|invalid|taken` and the same four for
`name_en_*`, `error.clinic.not_found` (404), `error.clinic.address_too_long`,
`error.clinic.phone_invalid`. Paging, sort, search and concurrency keys are
reused. **Not built:** `error.clinic.in_use` (409 when doctors or appointments use
a clinic); the delete path stays generic, as for Specialties.

**Phone numbers (`PhoneNumber`, Domain/ValueObjects, no package).** One
normalisation for every entity that stores a phone (patients will reuse it, D38,
D44); the stored form is E.164. Ignored: spaces, hyphens, dots, parentheses, bidi
marks and other invisible format characters; Arabic-Indic and Persian digits are
folded to 0-9. Accepted: the international form (`+…` or `00…`, 8 to 15 digits,
first digit not 0), and Egyptian national numbers with a single leading 0 (mobile
`01[0125]` + 8 digits, 11 in total; landline `0[2-9]` + 7 or 8 digits, 9 or 10 in
total), stored as `+20…`. Rejected: anything else, including a number with
neither `+` nor a leading 0 (ambiguous) and raw input longer than 32 characters
(checked before parsing). Blank means absent. One key for every failure:
`error.clinic.phone_invalid`.
**A value that slips past a validator is a 400, never a 500:** the entity
(`Clinic.SetContact`) and `PhoneNumber.Normalize` throw `InvalidRequestException`
with the key (the address limit too); a test calls the service directly with a bad
phone. Later entities follow this: a Domain method that normalises or limits a
value reports the failure with an `InvalidRequestException`, not a generic
exception.

**Seeder top-up (refines D48).** `IdentitySeeder` used to act only when no user
existed, so a permission added to the code later never reached the seeded admin
on an existing database. Now, when users already exist and the seed credentials
are configured, it adds the permissions of `Permissions.Global` that the user
named by `Seed:AdminUserName` lacks. Limits: only `Permissions.Global`; only that
one user; it never creates a user, never touches a password or another user,
never removes a permission; it logs only a count. Permissions are read from the
database on every call, so no re-login is needed. **It depends on the `Seed__*`
variables, which D48 removes after the first deploy: from then on a new global
permission must be granted through user management** (Phase 1), not by the seeder.
On a developer machine the only step is `docker compose up --build` (the API
restarts and tops the admin up); `docker compose down -v` is the heavier fallback
that also deletes all data.

**Tests.** `PhoneNumberTests` (valid table incl. Persian digits and bidi marks,
invalid table incl. raw input over 32 characters and short hotline numbers, the
entity throwing `InvalidRequestException`), `ClinicsAuthorizationTests`,
`ClinicsCrudTests` (validation keys, CRUD and row versions, full replace, audit
with real users, duplicates across Arabic variants, a deleted name re-created, the
parallel-create race), `ClinicsSearchTests` (hamza, ta marbuta, ya, diacritics,
digits, wildcards literal, paging, sorting, address and phone not searched), seeder
top-up tests, and OpenAPI assertions. Existing guards that cover the new code with
no change: the validator-coverage reflection test, the fallback authorization
policy, the unique-violation translation (generic over the annotation), the
`ExecuteUpdate`/`ExecuteDelete` scan, the seed test that compares the seeded
admin's permissions with `Permissions.Global`, and the OpenAPI checks (no test
paths, every operation documents a success schema).

**Variations from the Specialties pattern.** Only the new fields (address, phone,
the shared `PhoneNumber`), the full-replace `PUT`, and `InvalidRequestException`
from the entity. The name rules are copied (`ClinicRules`) rather than shared with
Specialties, to avoid touching unrelated code; a shared helper is natural at the
third copy.

**Later.** Short hotline numbers (for example 16xxx and 19xxx) are not accepted
yet; they need their own rule. Searching by address or phone. `error.clinic.in_use`
with Doctors. Filtering the clinic list by the caller's clinics with per-clinic
permissions.

---

## Open questions

O3, O4 and O5 are closed: see D44 and D43. O1 and O2 are deferred with the
deployment (D54) and block no feature work.

| # | Question | Status |
|---|---|---|
| O1 | Azure region — West Europe vs UAE North. Confirm Container Apps, ACR, Key Vault and Azure SQL Basic are all available there, then compare latency and price | OPEN |
| O2 | Custom domain and TLS, or the default Container Apps hostname | OPEN |

---

## Deferred until deployment

Everything postponed by D54, so nothing is forgotten. Tick an item only when it
is done and working on Azure. The project is not released until all are ticked.

**Definition of Done items that need Azure** (see Instructions.md)
- [ ] Image build pushed to Azure Container Registry from CI (item 2, D17).
- [ ] GitHub Actions authenticates to Azure with OIDC federated credentials, no stored service-principal secret (item 2, D40).
- [ ] Deployed to Azure Container Apps and reachable over HTTPS (item 3, D17).
- [ ] Migrations applied by the pipeline: an EF migrations bundle run as a Container Apps Job before a new revision gets traffic, never by hand (item 4, D39).
- [ ] Secrets resolved from Key Vault through a managed identity (item 5, D19).
- [ ] The app connects to Azure SQL with Entra / managed identity where possible, so no connection string holds a password (D40).
- [ ] `/health/live` and `/health/ready` are green as the Container Apps probes (item 6, D20); the endpoints exist and are tested locally.

**Running behind the Azure ingress**
- [ ] Forwarded-headers handling, so rate limiting sees the client address and `Request.Host` is the public host for the Origin check. The startup refresh counts against the refresh rate limit (30 per minute per client address): without this fix every visitor shares the ingress address (D48, D52).
- [ ] `Auth:AllowedOrigins` contains the deployed host if it differs from the request host (D48, D52).
- [ ] The Azure SQL connection string does not use `TrustServerCertificate=True`, which is for the local container only (D45).
- [ ] Probe timeouts for `/health/live` and `/health/ready` are set explicitly: the readiness check takes about 4 s to answer when the database is down (D20, D45).
- [ ] Scale to zero (D17): confirm a cold start does not exceed the 10 s limit of the startup silent refresh, which would show the login page to a signed-in user (D52).
- [ ] The structured JSON logs reach Log Analytics and can be queried by field (D21).
- [ ] If a CSP is added, it needs a hash for the inline pre-paint script in `index.html` (D26).

**Secrets and data**
- [ ] Remove the `Seed__*` variables from configuration after the first successful deploy, and move the JWT signing key and the seed values to Key Vault (D48). The seeder top-up of the admin's permissions depends on these variables (D55), so after this point **new global permissions must be granted through user management**, which has to exist by then.
- [ ] A change-password flow exists before any real data is stored; it belongs to Phase 1 user management (D48).
- [ ] An EF retry strategy for Azure SQL transient faults, together with the transaction wrapper D31 requires (D46, D31).

**CI and repository**
- [ ] Add a deploy job that `needs` `test`, `web` and `image`, and revisit `cancel-in-progress` in the CI concurrency setting so an in-flight deploy is never cancelled (D47).
- [ ] Add Dependabot for the pinned GitHub Actions and for the NuGet and npm dependencies (D47).

**Decisions and account**
- [ ] Close O1: choose the region (West Europe or UAE North) after confirming Container Apps, ACR, Key Vault and Azure SQL are all available there.
- [ ] Close O2: a custom domain with TLS, or the default Container Apps hostname.
- [ ] Revisit D13 (Azure SQL Basic tier), including whether a free Azure SQL offer is worth using.
- [ ] Create the Azure account only when ready to deploy, and set a budget alert immediately.

---

## Scope discipline

The scope is Phases 1 to 5 above, worked in order. Deployment to Azure is
postponed until the build is complete (D54); it no longer gates features. The
owner declares when the build is complete. Appointments (Phase 2) do not start
before Phase 1 is complete.

Ideas that arrive mid-build are written below under "Later", not implemented.
The deadline is real and the scope is the only variable under control.

## Later

- Short hotline phone numbers (for example 16xxx and 19xxx) are not accepted by `PhoneNumber` yet (D55).
- Search clinics by address or phone (D55).
