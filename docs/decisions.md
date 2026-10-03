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

Deployment comes first in the build order (see Phase 0). Code quality and UX
are judged as seriously as the pipeline.

---

## Scope

### Phase 0 — Walking skeleton (deployed before anything else)
- Specialties (create, list, search, edit, soft delete) as the first vertical slice
- Login, JWT access tokens, refresh tokens (one seeded initial user)
- `/health/live` and `/health/ready`
- Bilingual UI (Arabic / English) with RTL, from the first component
- Docker + docker compose, CI/CD, deployment to Azure Container Apps

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
through an **endpoint filter**, not through automatic MVC validation.
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

### D27 — Latin numerals and Gregorian dates in both languages
`ACCEPTED`

Use `Intl.DateTimeFormat` / `Intl.NumberFormat` with explicit locale
extensions (`ar-EG-u-nu-latn-ca-gregory`), because plain `ar-EG` produces
Arabic-Indic digits. Angular's `DatePipe` follows `LOCALE_ID` and is **not**
used for display; a custom pipe over `Intl` is used instead.

**Why:** Egyptian clinical and administrative practice uses Latin digits.
Arabic-Indic numerals would look wrong to the intended user.

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
rotated token revokes that token family.

The refresh token is delivered in an `HttpOnly`, `Secure`, `SameSite=Strict`
cookie (front end and API share an origin, D15). It is never placed in
`localStorage`. Login has lockout and rate limiting.

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
  stays small and revocation takes effect immediately.
- Permissions that are not clinic-specific are global: for example
  `users.manage`, `specialties.manage`, and all `patients.*`, because
  patients are shared across clinics (D44).
- Each permission is enforced by an authorization policy and handler. The
  handler resolves the target clinic from the resource and checks the
  assignment.
- Permission names are constants defined in one place in Domain; no magic
  strings elsewhere.

**Why:** a doctor can work in several clinics, and a receptionist at one clinic
must not act on another. Coarse roles cannot express that.

### D35 — Soft delete
`ACCEPTED`

Every entity has `IsDeleted`, `DeletedAt`, `DeletedBy`, applied through a
global EF query filter. Unique indexes are filtered with `WHERE IsDeleted = 0`
so a deleted record does not block re-creation.

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
Arabic text normalises أ/إ/ا, ة/ه and ى/ي, through a suitable collation or
normalised search columns (mechanism decided when Phase 0 search is built and
recorded here).

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
  `(DoctorId, StartUtc) WHERE Status <> Cancelled AND IsDeleted = 0`. It is
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

## Open questions

O3, O4 and O5 are closed: see D44 and D43.

| # | Question | Status |
|---|---|---|
| O1 | Azure region — West Europe vs UAE North. Confirm Container Apps, ACR, Key Vault and Azure SQL Basic are all available there, then compare latency and price | OPEN |
| O2 | Custom domain and TLS, or the default Container Apps hostname | OPEN |

---

## Scope discipline

**No feature is added after work begins until the application is running on
Azure with a working pipeline.**

Ideas that arrive mid-build are written below under "Later", not implemented.
The deadline is real and the scope is the only variable under control.

## Later

*(empty)*
