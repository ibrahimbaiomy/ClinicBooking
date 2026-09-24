# ClinicBooking — Decisions Log

Living document. Every architectural decision is recorded here with its rationale. 
Rejected alternatives are recorded too — the reasoning is the point, not the outcome.

**Status key:** `ACCEPTED` · `OPEN` · `SUPERSEDED`

---

## Purpose of this project

ClinicBooking is an outpatient appointment booking system. It exists to
demonstrate a complete, working deployment pipeline: source → container →
registry → cloud → running application, with CI/CD.

The deployment is the deliverable.

---

## Scope

### Phase 1
- Patients (create, list, search, edit, Soft delete)
- Doctors (create, list, search, edit, Soft delete, working hours)
- Clinics (create, list, search, edit, Soft delete)
- Specialties (create, list, search, edit, Soft delete)
- Claim-based authorization

### Phase 2
- Appointments (book, reschedule, cancel, list by doctor/day)
- Bilingual UI (Arabic / English)
- Audit trail 

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

## D1 — Clean Architecture
`ACCEPTED`

The solution has 4 projects: 
`ClinicBooking.Domain` 
`ClinicBooking.Application`
`ClinicBooking.Infrastructure`
`ClinicBooking.Api`

## D2 — No repository pattern
`ACCEPTED`

Services depend on `AppDbContext` directly. `DbSet<T>` already is a
repository with a richer interface than any wrapper would expose.

**Why:** a repository over EF Core adds indirection without adding
capability, and it obstructs `Include`, projection and split queries.

## D3 — Services, not CQRS / MediatR
`ACCEPTED`

Business logic lives in plain service classes (`AppointmentService`,
`PatientService`, `DoctorService`), registered as scoped, injected into
controllers.

**Why:** CQRS earns its complexity when read and write models genuinely
diverge. They do not here.

## D4 — Attribute-routed controllers, not minimal APIs
`ACCEPTED`

**Why:** controllers group related endpoints, carry filters and attributes
cleanly, and are what most .NET codebases a reviewer has seen look like.

## D5 — SOLID applied at service granularity
`ACCEPTED`

One service per aggregate. Dependencies via constructor injection against
interfaces (`IAppointmentService`). No service-locator, no static state.

---

## D6 — `long` primary keys internally, public booking reference externally
`ACCEPTED`

Entities use `long Id`. `Appointment` additionally carries a
`BookingReference` — a short, human-readable, unique string (e.g. `A7K2M9`)
used in URLs and spoken to patients.

**Why:** sequential integer IDs must never be enumerable from outside. A
reference code is also what a receptionist can read over the phone.

## D7 — DTOs at every API boundary
`ACCEPTED`

No EF entity is ever accepted as input or returned as output from a
controller. Separate request and response DTOs per endpoint.

**Why:** entities carry navigation properties and internal IDs, and binding
directly to them is an over-posting vulnerability.

## D8 — Manual mapping, no AutoMapper
`ACCEPTED`

Mapping is written by hand in the service layer, or projected with `Select`
directly in the query.

**Why:** at this size, explicit mapping is shorter to read than the
configuration it would replace, and `Select` projection produces better SQL.

## D9 — Input Validation and Business Rules
`ACCEPTED`

Request DTOs are validated at the API boundary using FluentValidation. 
Validators are responsible for input and format validation such as required fields, 
length limits, valid formats, and acceptable ranges.

Business invariants are enforced in the Application/Domain layer and are 
never assumed to be satisfied merely because an HTTP request passed DTO validation.

Examples of business rules include appointment overlap detection, 
working-hours validation, appointment state transitions, 
and other rules that must remain valid regardless of how the application service is invoked.

**Why:** API validation and business rules serve different purposes. 
Keeping input validation at the API boundary avoids duplicating DTO validation, 
while enforcing business invariants in the Application/Domain layer prevents 
invalid state from being created through other callers or future integrations.

## D10 — `ProblemDetails` for every error response
`ACCEPTED`

A single exception-handling middleware converts exceptions into RFC 7807
`ProblemDetails`. Validation failures return 400 with the per-field errors;
domain rule violations return 409 or 422; unexpected exceptions return 500
with a correlation ID and no internal detail.

## D11 — Error messages are keys, not sentences
`ACCEPTED`

The API returns `"error.appointment.slot_taken"`, never
`"This slot is already booked"`. The front end resolves the key through
i18n.

**Why:** this is the single decision that makes a bilingual UI work. English
sentences from the API would force translation logic in the client and would
leak untranslated text into the Arabic interface.

## D12 — `DateTimeOffset` in UTC for storage, Africa/Cairo for display
`ACCEPTED`

All timestamps stored as `DateTimeOffset` in UTC. Conversion to Cairo time
happens in the front end only. Doctor working hours are stored as local
`TimeOnly` plus day-of-week, not as absolute times.

**Why:** Egypt observes DST. Storing local times makes recurring schedules
break twice a year.

---

## D13 — SQL Server in Docker locally, Azure SQL in the cloud
`ACCEPTED`

Local development runs SQL Server in a container via `docker compose`. The
cloud environment uses Azure SQL Database (Basic tier).

## D14 — EF Core Migrations from the first commit
`ACCEPTED`

No `EnsureCreated`, no generated create-scripts. Migrations are applied at
startup in Development, and by an explicit pipeline step in production.

**Why:** learned on the ERP platform — retrofitting migrations after the
schema exists costs far more than starting with them.

## D15 — One container: API serves the built front end
`ACCEPTED`

The Dockerfile is multi-stage: stage one builds the Angular bundle with Node,
stage two builds the API with the .NET SDK, the final stage copies the bundle
into `wwwroot` and runs the API, which serves static files with a SPA
fallback.

**Why:** one image, one deployment, one cost. It also removes CORS from
production entirely — the front end and API share an origin.

## D16 — Dockerfile and compose written on day one
`ACCEPTED`

`Dockerfile` and `docker-compose.yml` exist and run before the first UI
screen is written.

**Why:** containerisation problems discovered late are structural. Discovered
early they are trivial.

## D17 — Azure Container Apps, not App Service
`ACCEPTED`

Target: Azure Container Registry → Azure Container Apps → Azure SQL, with
secrets in Key Vault via managed identity.

**Why:** Container Apps exercises the container workflow end to end, scales
to zero (keeping cost near nothing), and is the more current skill.

## D18 — GitHub Actions, not Azure DevOps Pipelines
`ACCEPTED`

**Why:** the repositories are already on GitHub, the workflow file lives with
the code, and a public green badge is visible to anyone reviewing the repo.

## D19 — Secrets: User Secrets locally, Key Vault in Azure
`ACCEPTED`

No secret of any kind appears in `appsettings.json` or in the repository, at
any point in history.

## D20 — `/health` endpoint from day one
`ACCEPTED`

`/health` returns 200 with a database connectivity check. Container Apps
probes it.

## D21 — Serilog with structured JSON output
`ACCEPTED`

Console sink, JSON formatted, so Azure Log Analytics can query fields rather
than grep text. Every request logged with a correlation ID.

---

## D22 — Angular + TypeScript
`ACCEPTED`

The frontend is built with Angular and TypeScript.

**Why:** Angular is the frontend framework selected for this project 
and provides the required component architecture, routing, forms, 
dependency injection, and TypeScript-first development model without 
introducing a separate meta-framework. 

**Rejected:** Blazor WebAssembly. It would have shared DTOs directly and cost
less time — D24 recovers most of that benefit — but Angular is the more
marketable skill and the reason this project exists is employability.

## D23 — TanStack Query for server state; no global state library
`ACCEPTED`

TanStack Query owns everything fetched from the API. Local UI state uses
`useState`. No Redux, no Zustand.

**Why:** nearly all state in this application is server state. A global store
would mostly duplicate the query cache.

## D24 — TypeScript types generated from OpenAPI
`ACCEPTED`

`openapi-typescript` generates client types from the API's OpenAPI document
as a build step. Models are never hand-written twice.

**Why:** this restores the end-to-end type safety that a shared C# project
would have given, without the shared project.

## D25 — Tailwind with logical properties only
`ACCEPTED`

Use `ms-`, `me-`, `ps-`, `pe-`, `text-start`, `text-end`. Never `ml-`, `mr-`,
`pl-`, `pr-`, `text-left`, `text-right`.

**Why:** the layout must mirror correctly when direction flips to RTL.
Physical properties silently break Arabic layout.

## D26 — Bilingual UI with Transloco from the first component
`ACCEPTED`

The Angular application uses Transloco for runtime internationalization 
with Arabic and English translation files.
Arabic is the default language. The selected language is persisted in localStorage, 
and the document direction (dir) switches between rtl and ltr when the language changes.

All user-facing strings must come from translation keys. No literal user-facing string 
is allowed in Angular components, templates, services, or other UI code.

Translation files are maintained separately for Arabic and English, 
and missing translation keys must be treated as a build or review 
issue rather than silently introducing untranslated UI text.

**Why:** the application requires runtime language switching between Arabic and English. 
Transloco provides a simple Angular-native workflow for runtime translations while keeping 
translation resources separate from application code.

## D27 — Latin numerals and Gregorian dates in both languages
`ACCEPTED`

`Intl.DateTimeFormat` with Latin digits regardless of locale.

**Why:** Egyptian clinical and administrative practice uses Latin digits.
Arabic-Indic numerals would look wrong to the intended user.

## D28 — Language of code vs. language of interface
`ACCEPTED`

Code, database schema, commit messages and documentation are in English.
Only the user interface is bilingual. Patient names and free-text content are
stored as entered.

---

## D29 — JWT Authentication and Refresh Tokens
`ACCEPTED`

The application uses JWT bearer authentication with multiple authenticated users. 
Users are stored using ASP.NET Core Identity, with passwords hashed using the 
framework's password hashing implementation.

Access tokens are short-lived JWT bearer tokens. Refresh tokens are used to obtain 
new access tokens without requiring the user to sign in again.

Authentication and authorization are separate concerns. Authentication establishes 
the identity of the caller, while claim-based authorization controls access 
to protected operations.

Refresh tokens must be stored and handled securely, rotated when used, 
and invalidated when required, such as during logout or token revocation.

**Why:** multiple users and refresh tokens provide a realistic authentication 
flow without introducing the complexity of an external identity provider 
at this project scope. The design can be replaced by an external identity 
provider later without changing the application's authorization model.

## D30 — Integration tests with Testcontainers; no unit tests for CRUD
`ACCEPTED`

Tests run against a real SQL Server container via Testcontainers and hit the
API through `WebApplicationFactory`. Unit tests are written only for logic
with real branching — overlap detection, working-hours validation, booking
reference generation.

**Why:** a unit test that mocks a `DbContext` to verify a `Select` tests the
mock, not the code.

## D31 — Appointment Concurrency and Double-Booking Protection

ACCEPTED

Appointment booking and rescheduling must be safe under concurrent requests. 
Checking for an available slot in application code alone is not sufficient, 
because two requests may observe the same slot as available before either 
transaction commits.

The booking operation must therefore use an explicit transaction/concurrency 
strategy and a database-enforced constraint or equivalent mechanism to ensure 
that conflicting appointments cannot be committed successfully.

The application must treat a concurrency conflict as an expected business 
outcome and return a controlled API error using the existing ProblemDetails 
and error-key conventions.

Integration tests must verify that concurrent booking attempts for the same 
doctor and overlapping time range result in at most one successful appointment.

**Why:** availability checks performed only in application code are vulnerable 
to race conditions. The database is the final authority for persisted consistency, 
while the application is responsible for presenting concurrency conflicts as 
a controlled business error.
---

## Open questions

| # | Question | Status |
|---|---|---|
| O1 | Azure region — West Europe vs UAE North, weighed on latency and price | OPEN |
| O2 | Custom domain and TLS, or default Container Apps hostname | OPEN |

---

## Scope discipline

**No feature is added after work begins until the application is running on
Azure with a working pipeline.**

Ideas that arrive mid-build are written at the bottom of this file under
"Later", not implemented. The deadline is real and the scope is the only
variable under control.

