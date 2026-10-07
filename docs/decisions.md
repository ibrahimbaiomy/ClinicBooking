# ClinicBooking — Decisions

Every decision binds. The decision text is the rule; **Why** is one or two lines
so the rule is applied in its spirit; **Rejected** names the alternatives that
were considered and must not be proposed again without a new decision.

The full, older text (implementation history, test lists, session notes) is in
`docs/archive/decisions-full-2026-10-06.md`. It is frozen: read it only when a
decision here is ambiguous, and never add to it.

**Status key:** `ACCEPTED` · `OPEN` · `SUPERSEDED`

**Format for a new decision** (D60):

```
### D<n> — <title>
`ACCEPTED` (refines / supersedes D<x>, if any)

- The rule, as bullets: names, numbers, keys, status codes.
**Why:** one or two lines.
**Rejected:** <alternative> (<reason in a few words>); ...
```

Add it to the index. Session reports, test lists and by-hand checks go to
`STATUS.md` or the commit, not here.

---

## Index

One line per decision. Format: `D<number> | <status> | <title> | <keywords>`. A
`*` after the number marks a **cross-cutting** decision: read it in full for
every task. The index is a map, not a summary. Sections below are grouped by
topic, not in numeric order.

```
D1* | ACCEPTED | Clean Architecture, four back-end projects | layering, projects, Domain, Application, Infrastructure, Api, DependencyInjection, project references
D2 | ACCEPTED | No repository pattern; IAppDbContext abstraction | repository, IAppDbContext, DbSet, EF Core, data access
D3 | ACCEPTED | Services, not CQRS / MediatR | services, feature folders, CQRS, MediatR
D4 | ACCEPTED | Attribute-routed controllers, not minimal APIs | controllers, routing, filters, minimal APIs
D5 | ACCEPTED | SOLID applied at service granularity | SOLID, dependency injection, one service per aggregate
D6 | ACCEPTED | long keys; protection through authorization | long id, 404 vs 403, existence leak, BookingReference, clinic-scoped, IClinicAccess
D7* | ACCEPTED | DTOs at every API boundary | DTO, entity, over-posting, controller, request, response
D8 | ACCEPTED | Manual mapping, no AutoMapper | mapping, Select, projection
D9* | ACCEPTED | Input validation and business rules | validation, FluentValidation, ValidationFilter, business rules, invariants
D10* | ACCEPTED | Errors: ProblemDetails carrying keys, never sentences | error keys, ProblemDetails, 400, 409, 422, 500, status codes, translation
D11 | SUPERSEDED | (merged into D10) | error keys
D12* | ACCEPTED | Time: UTC for storage, Cairo for rules and display | time, DateTimeOffset, UTC, Cairo, DST, TimeOnly, working hours, tzdata
D13 | ACCEPTED | SQL Server in Docker locally, Azure SQL in the cloud | SQL Server, Azure SQL, database, Docker
D14 | ACCEPTED | EF Core Migrations from the first commit | migrations, EF Core, schema
D15 | ACCEPTED | One container: API serves the built front end | Docker, Dockerfile, wwwroot, SPA, static files, CORS, image
D16 | ACCEPTED | Dockerfile and compose written on day one | Docker, Dockerfile, docker compose
D17 | ACCEPTED | Azure Container Apps, not App Service | Azure, Container Apps, ACR, scale to zero, deployment
D18 | ACCEPTED | GitHub Actions, not Azure DevOps Pipelines | CI, GitHub Actions
D19 | ACCEPTED | Secrets: .env locally, Key Vault in Azure | secrets, .env, User Secrets, Key Vault, appsettings
D20 | ACCEPTED | Two health endpoints | health, /health/live, /health/ready, probes
D21 | ACCEPTED | Serilog with structured JSON output | logging, Serilog, JSON, correlation id, PII
D22 | ACCEPTED | Angular + TypeScript | Angular, TypeScript, Vitest, zoneless, Node, versions
D23 | SUPERSEDED by D33 | TanStack Query for server state | server state, TanStack
D24 | ACCEPTED | TypeScript types generated from OpenAPI, committed | OpenAPI, openapi.json, schema.d.ts, gen:api, check:api, DTO change, generated types
D25* | ACCEPTED | Tailwind alone, logical properties only | Tailwind, RTL, logical properties, CSS, check:logical, icons, layout
D26* | ACCEPTED | Bilingual UI with Transloco from the first component | i18n, Transloco, translation keys, ar.json, en.json, RTL, check:i18n, language
D27 | ACCEPTED | Latin numerals and Gregorian dates in both languages | dates, numbers, Intl, intl pipe, Latin digits, formatting
D28 | ACCEPTED | Language of code vs. language of interface | English code, commit messages, documentation language
D29 | ACCEPTED | JWT authentication and refresh tokens | authentication, JWT, refresh token, cookie, login, lockout, rate limit
D30 | ACCEPTED | Integration tests with Testcontainers; no unit tests for CRUD | tests, Testcontainers, xunit, integration tests, WebApplicationFactory
D31 | ACCEPTED | Appointment concurrency and double-booking protection | concurrency, double booking, transaction, appointments, unique index
D32 | ACCEPTED | Multi-clinic model | doctor, DoctorClinic, working hours, multi-clinic, clinics
D33 | ACCEPTED | Front-end state: Signals + services, no state library | state, Signals, services, HttpClient, NgRx
D34* | ACCEPTED | Fine-grained, clinic-scoped permissions | permissions, clinic-scoped, authorization, policy, claims, global permissions, Permissions.cs
D35* | ACCEPTED | Soft delete | soft delete, IsDeleted, query filter, filtered unique index, hard delete, join tables
D36* | ACCEPTED | Auditing fields on the base entity | audit, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, interceptor
D37 | ACCEPTED | Bilingual data and Arabic search | Arabic, search, normalisation, NameAr, NameEn
D38 | ACCEPTED | Patient data | patient, PII, phone, E.164, national ID, privacy
D39 | ACCEPTED | Migrations in production | migrations bundle, Azure, deployment
D40 | ACCEPTED | CI to Azure without stored secrets | OIDC, Azure, CI, managed identity, secrets
D41 | ACCEPTED | Booking rules | appointment, status machine, past booking, overlap warning, breaks, leave, booking reference
D42 | SUPERSEDED by D60 | Documentation location and AI entry point | CLAUDE.md, docs
D43 | ACCEPTED | Fixed slots per doctor | slot, duration, appointment, unique index, slot_taken, working hours
D44 | ACCEPTED | Patients are shared across clinics | patient, phone, duplicate warning, patients.* permissions, global
D45 | ACCEPTED | Build, logging and runtime conventions | warnings as errors, GlobalUsings, Serilog, correlation id, domain exceptions, health ready, SQL image
D46* | ACCEPTED | Persistence choices | EF Core, collation, nvarchar, soft delete filter, SaveChanges interceptor, ExecuteUpdate ban, IUser, migrations, Testcontainers, retry
D47 | ACCEPTED | CI conventions | CI, GitHub Actions, web job, image job, Node version, check scripts, pinned actions, concurrency
D48 | ACCEPTED | Authentication and authorization specifics | password, lockout, refresh token, access token, cookie, seed, auth error keys, rate limit, Identity, permission handler
D49 | ACCEPTED | Arabic-aware search and uniqueness of names | Arabic, search, normalisation, unique names, sorting, SearchText, hamza
D50* | ACCEPTED | Specialties API and the pattern for later entities | secure by default, fallback policy, validation, concurrency, rowVersion, OpenAPI, paging, sort, conflict key, entity pattern, 409, Produces
D51 | ACCEPTED | Front-end foundation and hosting | Angular workspace, packages, scripts, static files, SPA fallback, cache headers, lint
D52 | ACCEPTED | Front-end authentication | session, interceptor, refresh, login form, returnUrl, guards, permissions.ts, parseApiError, error keys check, dev proxy
D53 | ACCEPTED | Front-end feature pattern (Specialties screens) | screens, list, URL state, form, Signal Forms, delete dialog, conflict, search debounce, translation scope
D54 | ACCEPTED | Azure deployment is postponed until the build is complete | Azure, deployment, deferred, CI, checklist, risk
D55 | ACCEPTED | Clinics back end | clinic, phone, PhoneNumber, address, E.164, seeder top-up, search, error keys, clinics.manage
D56 | ACCEPTED | Clinics screens (front end) | clinic screens, phone display, untouched guard, header, optional fields, Not set, shared code
D57 | ACCEPTED | User management and clinic-scoped permissions (back end) | users, clinic-scoped, UserClinicPermissions, doctors.manage, IClinicAccess, IClinicResolver, 404 vs 403, last administrator, seed at most once
D58 | ACCEPTED | Account state, change-password and admin reset (back end) | IsActive, MustChangePassword, disable, change-password, reset password, password, account gate, sessions, lockout, rate limit
D59 | ACCEPTED | User-management and change-password screens (front end) | users screens, change-password page, forced password change, mustChangePassword, session model, canIn, clinic-scoped UI, permission labels, check:permissions, temporary password, password fields, clinic picker, own account, guards, interceptor 403
D60 | ACCEPTED | Documentation layout for several assistants | AGENTS.md, CLAUDE.md, Codex, STATUS.md, release.md, guides, archive, decision format
D61 | ACCEPTED | Doctors back end | doctor, DoctorClinic, assignment, IsActive, DoctorSpecialty, working hours, WeeklyPeriod, slot duration, pending change, Cairo date, doctors.manage, all_clinics_required, in_use, IDoctorScheduleGuard, Phase 2 seam, shared code, NamedListQuery, split queries, periodIndex, conflictingClinicId
D62 | ACCEPTED | Doctors screens (front end) | doctor screens, list filters, IsActive filter, clinicPermissionInAnyGuard, canIn, canInAny, refreshUser, clinic picker, checkbox group, slot change, Cairo tomorrow, working hours editor, DayOfWeek, Saturday first, HH:mm, periodIndex, FeatureSession, scopeResolver, list-params, reference lists, incomplete note
O1 | OPEN | Azure region: West Europe vs UAE North | Azure, region, Container Apps, ACR, Key Vault, Azure SQL, price
O2 | OPEN | Custom domain and TLS, or the default Container Apps hostname | domain, TLS, hostname, Azure
```

---

## Architecture and back end

### D1 — Clean Architecture, four back-end projects
`ACCEPTED`

- `ClinicBooking.Domain`, `.Application`, `.Infrastructure`, `.Api`. The Angular
  app and the test project are not counted.
- Service implementations live in Application. Application and Infrastructure
  each expose a static `DependencyInjection` class with an `IServiceCollection`
  extension returning `IServiceCollection`, called from `Program.cs`.
- Application never references Infrastructure (D2).

### D2 — No repository pattern; `IAppDbContext` abstraction
`ACCEPTED`

- Services depend on `IAppDbContext` (Application): the `DbSet<T>` properties and
  `SaveChangesAsync`. `AppDbContext` (Infrastructure) implements it.

**Why:** `DbSet<T>` already is a repository; the interface exists only to keep
the dependency rule.
**Rejected:** a repository over EF Core (obstructs `Include`, projection and split queries).

### D3 — Services, not CQRS / MediatR
`ACCEPTED`

- Plain scoped service classes (`DoctorService`, ...) injected into controllers.
  `Features/<Name>` folders are for navigation only: no handlers, pipelines or
  request objects.

**Rejected:** CQRS / MediatR (read and write models do not diverge here).

### D4 — Attribute-routed controllers, not minimal APIs
`ACCEPTED`

**Why:** controllers group endpoints, carry filters and attributes cleanly, and
are what reviewers expect.
**Rejected:** minimal APIs.

### D5 — SOLID applied at service granularity
`ACCEPTED`

- One service per aggregate; constructor injection against interfaces; no
  service locator, no static state.

### D6 — `long` keys; protection through authorization
`ACCEPTED`

- Entities use `long Id`. Sequential ids are not a secret and not relied on as one.
- Protection: authorization on every access, scoped to the caller's clinics;
  ids exposed only where an endpoint needs them; `Appointment` also carries a
  `BookingReference` (short, human-readable, unique, e.g. `A7K2M9`) used in URLs
  and spoken to patients.
- **Precise rule (D57):** clinic in the route and permission missing there: 403.
  Resource addressed by its own id and no clinic-scoped permission in any of its
  clinics: **404 with the entity's not-found key**; some other clinic-scoped
  permission there: 403. A soft-deleted clinic grants nothing. Implemented by
  `IClinicAccess`.

**Rejected:** hiding ids as the main control (a secondary layer only).

### D7 — DTOs at every API boundary
`ACCEPTED`

- No EF entity is accepted or returned by a controller. Separate request and
  response DTOs per endpoint.

**Why:** entities carry navigation properties and internal ids; binding to them
is over-posting.

### D8 — Manual mapping, no AutoMapper
`ACCEPTED`

- Mapping by hand in the service, or `Select` projection in the query.

**Rejected:** AutoMapper (its configuration is longer than the mapping; `Select` gives better SQL).

### D9 — Input validation and business rules
`ACCEPTED`

- FluentValidation at the API boundary through a global MVC **action filter**
  (`ValidationFilter`, D50), not automatic MVC validation (`IEndpointFilter` is
  for minimal APIs only).
- Validators own input checks: required, length, format, range.
- Business invariants (overlap, working hours, state transitions) are enforced in
  Application/Domain and never assumed from DTO validation.

**Why:** invariants must hold for every caller, not only HTTP.

### D10 — Errors: `ProblemDetails` carrying keys, never sentences
`ACCEPTED` (merges the former D10 and D11)

- One exception-handling middleware turns every failure into RFC 7807
  `ProblemDetails`; `title` is an **error key**:
  `{ "title": "error.appointment.slot_taken", "status": 409, "traceId": "..." }`.
- 400 validation (field messages are keys), 409 conflict with existing data,
  422 business-rule violation, 401/403 in the same shape
  (`error.auth.unauthorized`, `error.auth.forbidden`), 500 with a correlation id
  and no internal detail. The front end resolves keys through Transloco.

**Rejected:** English sentences from the API (forces translation in the client, leaks English into the Arabic UI).

### D11 — *(merged into D10)*
`SUPERSEDED`

### D12 — Time: UTC for storage, Cairo for rules and display
`ACCEPTED`

- Instants: `DateTimeOffset` in UTC.
- Working hours: Cairo `TimeOnly` + day-of-week per (doctor, clinic), never
  absolute times (Egypt observes DST).
- The server converts UTC ↔ Cairo (`TimeZoneInfo`, `Africa/Cairo`) only inside
  Application, only to validate rules. Display formatting is the front end's.
- The runtime image must contain tzdata: the standard .NET runtime image, not
  Alpine or chiseled unless tzdata is added.
- Accepted risk: a future appointment shifts by an hour if Egypt changes its DST rules.

**Rejected:** storing local time plus zone for every appointment (cost not justified now).

### D13 — SQL Server in Docker locally, Azure SQL in the cloud
`ACCEPTED`

- Local: SQL Server container through `docker compose`. Cloud: Azure SQL
  Database, Basic tier (revisit before deploy, `release.md`).

### D14 — EF Core Migrations from the first commit
`ACCEPTED`

- Applied at startup in Development only; in production by a pipeline step (D39).

**Rejected:** `EnsureCreated` and generated create scripts (retrofitting migrations later costs far more).

### D15 — One container: API serves the built front end
`ACCEPTED`

- Multi-stage Dockerfile: `node:24-bookworm-slim` runs `npm ci` and
  `npm run build` (with the prebuild checks, so `scripts/` and `public/i18n` are
  copied); the .NET SDK builds the API (its layers copy only .NET projects, so a
  front-end change does not invalidate them); the final stage copies
  `dist/clinic-booking-web/browser` into `/app/wwwroot` and the API serves it
  with a SPA fallback (D51).

**Why:** one image, one deployment, one cost; same origin, so no CORS in production.
**Rejected:** deploying front end and API separately (two deployments, CORS).

### D16 — Dockerfile and compose written on day one
`ACCEPTED`

- `Dockerfile` and `docker-compose.yml` existed before the first screen and must
  keep working.

**Why:** containerisation problems found late are structural; found early they are trivial.

### D17 — Azure Container Apps, not App Service
`ACCEPTED`

- Target: ACR → Container Apps → Azure SQL, secrets in Key Vault via managed identity.

**Why:** exercises the container workflow end to end, scales to zero, current skill.
**Rejected:** App Service.

### D18 — GitHub Actions, not Azure DevOps Pipelines
`ACCEPTED`

**Why:** the repositories are on GitHub, the workflow lives with the code, a public green badge.
**Rejected:** Azure DevOps Pipelines.

### D19 — Secrets: `.env` locally, Key Vault in Azure
`ACCEPTED`

- No secret in `appsettings.json` or the repository, at any point in history.
- `docker compose`: `.env` (git-ignored; `.env.example` with dummy values is
  committed). `dotnet run`: User Secrets. Azure: Key Vault via managed identity.

**Why:** User Secrets do not exist in a container, and compose is the main local path.

### D20 — Two health endpoints
`ACCEPTED`

- `/health/live`: 200 while the process runs, no dependency checked (liveness).
- `/health/ready`: 200 only when the database is reachable (readiness); a custom
  `SELECT 1` check with a 3 s timeout that logs only the exception type (D45).

**Rejected:** one endpoint that checks the database (the platform would restart a healthy container during a brief database outage).

### D21 — Serilog with structured JSON output
`ACCEPTED`

- Console sink, JSON, a correlation id per request, never patient PII (D38).
  Details in D45.

**Why:** Log Analytics can query fields instead of grepping text.

---

## Front end

### D22 — Angular + TypeScript
`ACCEPTED`

- Angular, TypeScript strict, the CLI's default test runner.
- At creation (Oct 2026): Angular and CLI **22.2.1**; Node 24 for the project and
  the image (`engines` `^24.15.0`); **TypeScript 6.0.x** (7.x is not supported by
  Angular 22); **Vitest 5 with jsdom**; **zoneless** (no zone.js; state in Signals).

**Why:** the most used enterprise front-end framework in the .NET job market.
**Rejected:** Blazor WebAssembly (cheaper and shares DTOs, but Angular is the more marketable skill; D24 recovers most of the type sharing).

### D23 — TanStack Query for server state
`SUPERSEDED` by D33 (written for React; does not apply to Angular).

### D24 — TypeScript types generated from OpenAPI, committed
`ACCEPTED`

- `openapi-typescript` generates `schema.d.ts`; `openapi.json` and `schema.d.ts`
  live in `src/clinic-booking-web/src/api/`, are committed and never hand-edited.
- `openapi.json` is generated from the API inside the test host:
  `OpenApiDocumentTests` compares it with the committed file (line endings
  ignored) and fails with the regeneration command;
  `UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests` rewrites it (LF).
- `npm run gen:api` runs that test (needs the .NET SDK), then `openapi-typescript`.
  `npm run check:api` regenerates `schema.d.ts` in memory and fails on any
  difference (Node only, for CI). Output is LF and deterministic. `schema.d.ts`
  is excluded from lint.
- `package.json` carries an npm `overrides` entry so `openapi-typescript` uses
  TypeScript 6; remove it when the package supports TypeScript 6.

**Why:** the Docker build compiles Angular before the API exists.
**Rejected:** generating `openapi.json` at build time (it starts the app, so every build, including Docker's, would need a valid JWT key).

### D25 — Tailwind alone, logical properties only
`ACCEPTED`

- Use `ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`, `text-end`,
  `rounded-s-*`, `rounded-e-*`, `border-s`, `border-e`. Never `ml-`, `mr-`, `pl-`,
  `pr-`, `left-`, `right-`, `text-left`, `text-right`, `rounded-l-*`, `rounded-r-*`.
- `scripts/check-logical-properties.mjs` (no dependencies) catches physical
  utilities, also behind variants and with arbitrary values, physical corner and
  scroll utilities and physical CSS properties; it runs in `npm run lint` and
  `prebuild`. A line may carry `logical-ok` with a reason.
- Directional icons are mirrored with `rtl:-scale-x-100` (a review rule: no
  linter sees an icon's direction).

**Rejected:** Angular Material, PrimeNG, any component library (RTL must stay under our control).

### D26 — Bilingual UI with Transloco from the first component
`ACCEPTED`

- `@jsverse/transloco`, runtime switching, **Arabic default**. `LanguageService`
  holds the language as a signal, saved in `localStorage` (`clinicbooking.lang`;
  anything but `ar`/`en` falls back to Arabic; a failing storage never crashes)
  and mirrors it to `<html lang dir>` and Transloco.
- Files: `public/i18n/{ar,en}.json` (served as `/i18n/<lang>.json`); a scope in
  `public/i18n/<scope>/{ar,en}.json`, loaded with `provideTranslocoScope`, keys
  `<scope>.<key>`.
- Before first paint: `index.html` ships `lang="ar" dir="rtl"` and a tiny inline
  script that corrects both from the saved choice; an app initializer loads the
  language file before the first render. No fallback language.
- `npm run check:i18n` (in `prebuild`) fails on: a folder missing `ar.json` or
  `en.json`, differing key sets or `{{placeholders}}`, an empty or non-string
  value, a used key that does not exist, literal text in a template (also literal
  `title`/`alt`/`placeholder`/`aria-label` and interpolated literals), an inline
  `template`. A runtime key needs `i18n-keys: a.b, c.d` on its line. Back-end
  `error.*` keys are checked too (D52). Not detected: literal strings in `.ts`
  code. Unused keys are warnings.

**Rejected:** Angular built-in i18n (build-time: one build per language).

### D27 — Latin numerals and Gregorian dates in both languages
`ACCEPTED`

- The `intl` pipe (`{{ v | intl: 'date' }}`; kinds `date`, `time`, `datetime`,
  `number`, `percent`; optional `Intl` options) over `formatIntl`. Locales
  `ar-EG-u-nu-latn-ca-gregory` and `en-GB-u-nu-latn-ca-gregory` (day first,
  24-hour). Instants shown in `Africa/Cairo`; a date-only string is a calendar
  day shown as such (`date` kind only); `null` or invalid input renders `''`.
  The pipe is impure (follows the language).

**Why:** Egyptian clinical practice uses Latin digits.
**Rejected:** Angular `DatePipe` (follows `LOCALE_ID`); plain `ar-EG` (Arabic-Indic digits).

### D28 — Language of code vs. language of interface
`ACCEPTED`

- Code, schema, commit messages and documentation in English; only the UI is
  bilingual. Patient names and free text are stored as entered.

### D33 — Front-end state: Signals + services, no state library
`ACCEPTED`

- Server data through injectable services over `HttpClient` (`httpResource` /
  `rxResource` where they fit); local and derived state in Signals.

**Rejected:** NgRx, TanStack Query, any state library (nearly all state is server state).

---

## Security and data

### D29 — JWT authentication and refresh tokens
`ACCEPTED` (specifics: D48)

- JWT bearer; several users in ASP.NET Core Identity, framework-hashed passwords.
- Short-lived access tokens. Refresh tokens rotate on every use, are invalidated
  on logout or revocation; reuse of a rotated token revokes the token family; a
  short grace window tolerates concurrent refreshes.
- The refresh token lives in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie,
  never in `localStorage`. Login has lockout and rate limiting.
- Authentication establishes identity; authorization (D34) controls access.

**Why:** a realistic flow without an external provider, replaceable later without
touching authorization.
**Rejected (for now):** an external identity provider.

### D34 — Fine-grained, clinic-scoped permissions
`ACCEPTED`

- Claim-based, fine-grained names (`patients.create`, `doctors.manage`, ...).
- **Clinic-scoped** grants are stored per (user, clinic, permission) in
  `UserClinicPermissions` (D57), not in the JWT. The first is `doctors.manage`.
- **Global** permissions: `users.manage`, `specialties.manage`, `clinics.manage`
  (a clinic list cannot be scoped to a clinic that does not exist yet, D55) and
  all `patients.*` (patients are shared, D44). Stored as Identity user claims of
  type `permission` (D48). Every check reads the database.
- One policy and handler per permission; the handler resolves the clinic through
  an `IClinicResolver` (default: route value `clinicId`); a resource addressed by
  its own id is checked by `IClinicAccess` in the service (D57).
- Permission names are constants in one place in Domain.

**Why:** a doctor works in several clinics; a receptionist at one must not act on another.
**Rejected:** coarse roles (cannot express per-clinic access); permissions in the JWT (token size, delayed revocation).

### D35 — Soft delete
`ACCEPTED`

- Only **Specialties, Clinics, Doctors, Patients**: `IsDeleted`, `DeletedAt`,
  `DeletedBy`, hidden by a global EF query filter. Their unique indexes are
  filtered `WHERE IsDeleted = 0`, so a deleted record does not block re-creation.
- Appointments are never deleted (cancelled by status). Join tables,
  slot-duration history and refresh tokens are hard-deleted. Identity tables
  follow Identity's rules.
- Tiers in Domain: `BaseEntity` (`long Id`) → `AuditableEntity` (`IAuditable`,
  D36) → `SoftDeletableEntity` (`ISoftDeletable`).
- Deleting a doctor with upcoming appointments needs explicit UI confirmation
  that they will be cancelled; the cancellations and the delete run in one
  transaction.

### D36 — Auditing fields on the base entity
`ACCEPTED`

- `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, set by a `SaveChanges`
  interceptor (D46). The full audit trail (before/after) is Phase 2.

### D37 — Bilingual data and Arabic search
`ACCEPTED` (mechanism: D49)

- Reference data stores `NameAr` and `NameEn`. Arabic search folds أ/إ/ا, ة/ه
  and ى/ي through normalised columns computed in code.

**Rejected:** a collation (none folds these letters, D46).

### D38 — Patient data
`ACCEPTED`

- A patient stores a name and a phone only; any further field needs a new
  decision. Phone in E.164. No national ID. No patient PII in logs.
- Egypt's Personal Data Protection Law (Law 151 of 2020) is reviewed before
  Phase 4, or earlier if anything beyond contact details is stored.

### D39 — Migrations in production
`ACCEPTED`

- CI builds an EF migrations bundle and runs it as a Container Apps Job before
  the new revision receives traffic. Never by hand.

### D40 — CI to Azure without stored secrets
`ACCEPTED`

- GitHub Actions authenticates with OIDC federated credentials. The app reaches
  Azure SQL with Managed Identity / Entra where possible (no password in a
  connection string).

**Rejected:** a stored service-principal secret.

---

## Appointments and clinical model

### D30 — Integration tests with Testcontainers; no unit tests for CRUD
`ACCEPTED`

- Tests run against a real SQL Server (Testcontainers) through
  `WebApplicationFactory`. Unit tests only for logic with real branching
  (overlap, working hours, booking reference).

**Rejected:** unit tests that mock a `DbContext` (they test the mock).

### D31 — Appointment concurrency and double-booking protection
`ACCEPTED` (mechanism: D43)

- Booking and rescheduling run in an explicit transaction, guarded by a
  database-enforced unique index (D43). A conflict is a 409 `ProblemDetails`
  with a key. If an EF retry strategy is enabled, the whole transaction runs
  inside it.
- Integration tests: concurrent attempts for the same doctor and time produce at
  most one appointment.

**Rejected:** an application-only availability check (two requests both see the slot free).

### D32 — Multi-clinic model
`ACCEPTED`

- A doctor may work in several clinics (`DoctorClinic`). Working hours belong to
  a (doctor, clinic) pair and may hold several periods per day.
- No overlap applies to the doctor **across all clinics**; a doctor's periods in
  different clinics must not overlap on the same day (validated on save).
- Slot duration belongs to the doctor (D43).

### D41 — Booking rules
`ACCEPTED`

- Status machine: `Booked → Completed | Cancelled | NoShow`. Cancelled frees the
  slot; completed does not.
- No booking in the past.
- A patient's overlapping appointment (even with another doctor) is a
  **warning** the user confirms: not blocked, not enforced by the database.
- Doctor breaks and leave days are modelled so working hours can exclude them.
- Booking references are unique and collision-checked on insert. Slots: D43.

### D43 — Fixed slots per doctor
`ACCEPTED`

- One slot duration per doctor, in every clinic, stored as history rows
  `(DoctorId, SlotMinutes, EffectiveFrom)` (`EffectiveFrom` is a Cairo date).
- A new duration takes effect only on a date **after the doctor's last active
  appointment**; existing appointments are never touched.
- The grid is anchored on the working-hour period start: an appointment starts at
  `period start + n × duration` and ends inside the period (validated in
  Application). One appointment occupies **exactly one slot**; longer procedures
  need a new decision.
- A working-hours change that would leave an active future appointment outside
  its period or off the grid is rejected.
- **Database guard:** unique filtered index `(DoctorId, StartUtc) WHERE Status <>
  Cancelled`, per doctor (so also across clinics). Its violation is 409
  `error.appointment.slot_taken` (the `ConflictKey` mechanism, D50).

**Why:** one duration and one slot per appointment make a plain unique index a
complete overlap guard, with no locks.
**Rejected:** a per-clinic duration (one doctor on different grids, which the index cannot reconcile).

### D44 — Patients are shared across clinics
`ACCEPTED`

- A patient belongs to no clinic. Name and phone only (D38). `patients.*` are
  global; `patients.read` returns name and phone.
- On create and edit, a matching phone shows a **duplicate warning** with the
  matched name and phone; the user may continue. No unique constraint on phone
  (family members share numbers).
- Revisit when Phase 4 adds medical records: their visibility will probably be
  clinic-scoped.

**Rejected:** patients per clinic (duplicates of the same person); a separate "read all" permission (staff seeing different data is confusing).

---

## Repository, build and CI

### D42 — Documentation location and AI entry point
`SUPERSEDED` by D60.

### D45 — Build, logging and runtime conventions
`ACCEPTED`

- `TreatWarningsAsErrors` on; implicit usings off (`GlobalUsings.cs` only);
  xunit v2. Migrations are excluded from analyzer rules in `.editorconfig`;
  NU190x audit warnings are not fatal.
- Serilog: compact JSON (`RenderedCompactJsonFormatter`) to the console,
  configured in code. Correlation id: an incoming `X-Correlation-Id` is accepted
  only if it matches `[A-Za-z0-9_-]{1,64}`, otherwise a GUID; returned in the
  response header. Request logs: method, path, status, elapsed time only;
  `/health` at Debug.
- `ProblemDetails` carries `traceId` and `correlationId`. Framework keys:
  `error.http.<status>`, `error.validation.failed`, `error.validation.invalid`,
  `error.unexpected`, `error.health.not_ready`.
- Domain exceptions, each with a key: `InvalidRequestException` (400),
  `NotFoundException` (404), `ConflictException` (409),
  `BusinessRuleException` (422); D48 adds `Unauthorized` (401), `Forbidden`
  (403), `AccountLocked` (423).
- Exception messages never contain patient data; `EnableSensitiveDataLogging` stays off.
- Local SQL Server image pinned (`2022-CU27-ubuntu-22.04`), published on
  127.0.0.1 only. `TrustServerCertificate=True` is for the local container only.
  Runtime image: standard Debian (tzdata, D12).

### D46 — Persistence choices
`ACCEPTED`

- Collation: the server default (`SQL_Latin1_General_CP1_CI_AS`, also Azure
  SQL's). Arabic text is `nvarchar`, never `varchar`. No collation folds the
  Arabic letters (checked: `Arabic_CI_AI`, `Arabic_100_CI_AI`, `Arabic_CI_AS`), so
  folding is D49's normalised columns.
- Soft delete: a named EF query filter `"SoftDelete"` on every `ISoftDeletable`.
- `AuditSaveChangesInterceptor` (`TimeProvider`, `IUser`): create sets
  `Created*` (`UpdatedAt` stays null); update sets `Updated*`, never `Created*`;
  `Remove()` of a soft-deletable entity becomes an update setting `IsDeleted`,
  `DeletedAt`, `DeletedBy` and `Updated*`. Soft delete does not cascade; services do that.
- **`ExecuteUpdate` / `ExecuteDelete` are banned** in `src/` (they bypass the
  interceptor); a test scans the sources.
- `IUser` (Application) exposes `long? Id` (null = system or anonymous); Api
  implements it from the JWT. `*By` columns are nullable `bigint`.
- Migrations run at startup in Development only. `docker-compose.yml` is
  production-shaped; `docker-compose.override.yml` (loaded only by local
  `docker compose up`) sets Development. Database tests run in Development.
- No `EnableRetryOnFailure` yet: reconsider before deploy, with D31's wrapper.
- `dotnet-ef` pinned in `.config/dotnet-tools.json` (10.0.x);
  `Microsoft.EntityFrameworkCore.Design` lives in Api.
- Tests: one SQL Server container per run, a fresh database per test class,
  `TimeProvider` and `IUser` replaced. `dotnet test` needs Docker running.
- EF SQL command logging at Warning; migration messages at Information.

### D47 — CI conventions
`ACCEPTED`

- `global.json` pins the SDK (≥ 10.0.100, `rollForward: latestFeature`), used by
  CI and the Dockerfile.
- Every action pinned to a full commit SHA with a version comment; only
  `actions/*`, no `docker/*`; runner `ubuntu-24.04`; checkout with
  `persist-credentials: false`. CI relies on `Directory.Build.props` for warnings
  as errors (no `-warnaserror`).
- Three parallel jobs `test`, `web`, `image`; a later push job `needs` all three.
  `test` uploads a TRX artifact (7 days); no test-logger package.
- `web` steps in order: Node-version guard, `setup-node`, `npm ci`,
  `npm run lint`, `npm test`, `npm run check:i18n`, `npm run check:api`,
  `npm run build`. After a failure the later steps still run (only if `npm ci`
  succeeded); a failing `check:api` adds an annotation to run `npm run gen:api`.
- One Node major: `actions/setup-node` v7.0.0
  (`820762786026740c76f36085b0efc47a31fe5020`, the runner ships Node 22);
  `src/clinic-booking-web/.nvmrc` = `24`; `.npmrc` `engine-strict=true`;
  `.github/scripts/check-node-version.sh` fails when `.nvmrc`, the Dockerfile's
  `FROM node:<major>` and `engines` differ or cannot be parsed.
- No npm, NuGet or Docker-layer caching until a run exceeds about 5 minutes.
- Concurrency: workflow + ref, `cancel-in-progress: true`; revisit with a deploy job.
- The SQL Server image tag is in both `docker-compose.yml` and the Testcontainers
  fixture: keep them in sync.

**Rejected:** `docker/*` actions; `ubuntu-latest`; early caching.

### D48 — Authentication and authorization specifics
`ACCEPTED` (refines D29 and D34)

- **Layers.** `AuthService` (Application): login, rotation, reuse detection,
  logout; depends on `IAppDbContext`, `TimeProvider`, and on `IIdentityService`
  (wraps `UserManager`), `IAccessTokenService`, `IPermissionChecker`
  (Infrastructure).
- **Identity.** `ApplicationUser : IdentityUser<long>` (Infrastructure), audited,
  not soft-deletable. `AppDbContext` is an `IdentityUserContext`: users and
  claims, **no role tables**. Failed-login bookkeeping updates `UpdatedAt`
  (accepted until the Phase 2 trail).
- **Access token.** HS256, 15 minutes, claims `sub` and `jti` only (+ iat/nbf/exp).
  `Jwt:SigningKey` (compose: `JWT_SIGNING_KEY`), ≥ 32 bytes; the host **fails at
  startup** if it is missing or short, in every environment (tests supply a
  throwaway key). Clock skew 30 s. Inbound claim mapping off; `CurrentUser` reads
  `sub`. Lifetime checked against `TimeProvider` by a custom `LifetimeValidator`;
  `DateTime` only at the IdentityModel boundary.
- **Refresh token.** 32 random bytes, stored as SHA-256 only. 7 days sliding, a
  30-day absolute cap per family. Every use consumes it and issues a new one. A
  consumed token presented again **within 10 s** (`Auth:ReuseGraceSeconds`): 401,
  nothing revoked; **after** that: the whole family is revoked. A rowversion makes
  simultaneous rotations fail cleanly. On refresh the user must exist, be active
  and not locked out, or the family is revoked (D58). Logout revokes the family,
  idempotent. A user's long-expired rows are removed at that user's login (no
  purge job until Phase 5).
- **Cookie.** `refresh_token`, `HttpOnly`, `Secure` (always, also on
  `http://localhost`), `SameSite=Strict`, `Path=/api/auth`, no Domain. Refresh and
  logout are POST-only and reject an `Origin` that is neither this host (compared
  without scheme) nor in `Auth:AllowedOrigins`.
- **Lockout and rate limits.** 5 failures lock the account for 15 minutes. The
  lockout is checked **before** the password; an unknown user is checked against
  a dummy hash. Per client address, fixed one-minute window: 10 logins, 30
  refreshes (`RateLimiting`).
- **Password policy** (defined once, in the Identity options): ≥ 12 characters,
  upper-case, lower-case, digit, ≥ 4 distinct characters; no breached-password check.
- **Error keys.** 401 `error.auth.invalid_credentials` (wrong password and
  unknown user alike); 423 `error.auth.locked_out` + `Retry-After` (reveals that a
  locked account exists: accepted); 429 `error.auth.rate_limited` + `Retry-After`;
  401 `error.auth.invalid_refresh_token` (cookie cleared); 401
  `error.auth.unauthorized`; 403 `error.auth.forbidden` (missing permission or
  foreign origin).
- **Permissions.** `Domain/Permissions/Permissions.cs` with `Global`,
  `ClinicScoped` (D57) and `All`; one policy per permission, named after it;
  global grants are claims `permission`, read from the database on every check,
  so revocation is immediate. A disabled user fails every check.
- **Seeding.** `Seed:AdminUserName` / `Seed:AdminPassword` (compose:
  `SEED_ADMIN_USERNAME` / `SEED_ADMIN_PASSWORD`), at startup in every environment
  when both are set. With no user: create it with every global permission. With
  users: **top up** that user with the `Permissions.Global` it lacks, each **at
  most once** (marker claim `seeded_permission`, D57), so a removed permission
  stays removed. Never creates another user, touches a password or removes a
  permission; logs a count only. The seed variables are removed after the first
  deploy (`release.md`); then new global permissions go through user management.
- **Endpoints.** `POST /api/auth/login`, `/refresh`, `/logout`,
  `/change-password` (D58); `GET /api/auth/me` (id, user name, permissions,
  `mustChangePassword`, `clinicPermissions`).
- **Front end:** refresh is single-flight.

**Rejected:** `SignInManager` (cookie-oriented); role tables; the `__Host-` cookie prefix (needs `Path=/`).

### D49 — Arabic-aware search and uniqueness of names
`ACCEPTED` (completes D37)

- `SearchText.Normalize` (Domain) is the only normalisation, applied to stored
  values and to search terms. Stored in plain `nvarchar(100)` columns next to the
  display text (`NameArNormalized`, `NameEnNormalized`).
- Pipeline: NFKC; remove Arabic diacritics (U+064B–U+065F, U+0670), tatweel and
  invisible format characters (ZWJ, ZWNJ, LRM, RLM, BOM); fold `أ إ آ ٱ → ا`,
  `ة → ه`, `ى → ي`; fold Arabic-Indic and Persian digits to `0-9`; lower-case;
  collapse whitespace; trim. `ؤ` and `ئ` are **not** folded. English goes through
  the same function (no accent folding).
- Display names and normalised copies have private setters and are set together
  by `SetNames`. Display names are stored as entered (trimmed).
- Search: one `search` parameter; the normalised query is split on spaces and
  **every word** must `Contains`-match one of the two normalised columns
  (wildcards literal). A leading wildcard is fine for reference data; **Patients
  need a prefix or full-text approach**.
- Uniqueness over the **normalised** text, per language, among live rows, for
  **reference data only** (Specialties, Clinics). Patients get a normalised
  column for search and **no unique constraint on names**.
- Arabic sort order is the normalised text.

**Rejected:** a SQL computed column (duplicates the logic; stored value and query can drift).

### D50 — Specialties API and the pattern for later entities
`ACCEPTED`

Specialties is the reference implementation; Clinics, Doctors and Patients copy it
(`docs/guides/backend-entity.md`).

- **Endpoints** `/api/specialties`: `GET` list, `GET {id}`, `POST` (201 +
  `Location`), `PUT {id}`, `DELETE {id}` (204). Query `Search`, `Page` (1),
  `PageSize` (20, max 100), `SortBy` (`nameEn` default, `nameAr`, `createdAt`),
  `SortDirection` (`asc` default); bound case-insensitively, PascalCase in the
  OpenAPI document. Response `{ items, page, pageSize, totalCount }`; ties broken
  by `Id` (descending for newest-first). Responses carry id, both names,
  `createdAt`, `updatedAt`, `rowVersion`; never normalised columns or audit user ids.
- **Permissions.** Any signed-in user reads; `specialties.manage` writes.
- **Secure by default.** A fallback policy requires an authenticated user for any
  endpoint without authorization metadata **and for unmatched requests** (an
  anonymous unknown route is 401, not 404). Anonymous on purpose:
  `[AllowAnonymous]` on login, refresh, logout; `.AllowAnonymous()` on health and
  OpenAPI; static files and the SPA fallback (D51). A test proves an action
  without attributes returns 401.
- **Validation.** One validator per request DTO and per query object in
  `Application/Validators` (assembly scanning). `ValidationFilter` returns 400
  `error.validation.failed` with camelCase field names and key messages; a message
  that is not an `error.` key becomes `error.validation.invalid`. A reflection
  test fails if a production action's request DTO has no validator.
- **Concurrency.** `RowVersion` on every auditable entity. `PUT` sends it back; a
  stale one is 409 `error.concurrency.conflict` (checked in the service and by
  EF). `DELETE` needs none.
- **Duplicates.** The service pre-checks for a friendly 409
  (`error.specialty.name_ar_taken` / `name_en_taken`, Arabic first); the unique
  indexes are the real guard. A unique violation becomes a 409 **only** for an
  index carrying the `ClinicBooking:ConflictKey` annotation (its value is the key),
  matched by the ASCII index name in the SQL error; others are rethrown.
- **Error keys.** `error.specialty.name_ar_required|too_long|invalid` (and
  `name_en_*`), `error.specialty.not_found`, `error.paging.page_invalid`,
  `error.paging.page_size_invalid`, `error.sort.invalid`, `error.search.too_long`,
  `error.concurrency.row_version_required|invalid`, `error.concurrency.conflict`;
  login: `error.auth.user_name_required`, `error.auth.password_required`,
  `error.auth.field_too_long`.
- **Delete** is a soft delete through `Remove()`; a second delete is 404; the name
  can be re-created. When a Doctor uses a Specialty, delete returns 409
  `error.specialty.in_use` (no cascade; soft-deleted doctors keep the reference).
- **Queries** project with `Select` from one hand-written expression
  (`SpecialtyMapping`); updates and deletes load the entity.
- **OpenAPI.** `Microsoft.AspNetCore.OpenApi`; `/openapi/v1.json` only when
  `OpenApi:Enabled=true` (Development, tests); a transformer removes `servers` and
  non-JSON media types. Every action documents its success response with
  `[ProducesResponseType]` (D52). **Never `[Produces]`** on a controller (it
  overrides `application/problem+json`).

### D51 — Front-end foundation and hosting
`ACCEPTED` (implements D15, D22, D24–D27)

- Workspace `src/clinic-booking-web`: prefix `cb`, standalone, `strict` and
  `strictTemplates`, OnPush, no `any`, `inject()`, Signals, `templateUrl` only,
  no zone.js; CLI file naming (`app.ts`, no `.component` suffix).
- **Agreed packages** beyond the CLI's (rule 8): `tailwindcss`,
  `@tailwindcss/postcss`, `postcss` (Tailwind 4 through `.postcssrc.json`),
  `@jsverse/transloco`, `openapi-typescript`, `eslint`, `typescript-eslint`,
  `angular-eslint`. Check scripts and `gen:api` are plain Node, tested with
  `node --test`. `package-lock.json` committed; `npm ci` in the image.
- **Scripts:** `build` (`prebuild` runs `check:logical` and `check:i18n`), `lint`
  (angular-eslint then `check:logical`, plus `check:permissions`, D52), `test`
  (`ng test --no-watch`, then the Node script tests), `check:i18n`,
  `check:logical`, `check:permissions`, `gen:api`, `check:api`.
- **Hosting.** `UseStaticFiles` runs before logging, correlation and
  authorization; the SPA fallback endpoints are `AllowAnonymous`. The catch-all
  excludes a first segment of exactly `api`, `health` or `openapi` and any path
  with a file extension; `/` has its own fallback. Excluded paths answer
  ProblemDetails 401/404, never `index.html`. Cache: `index.html` and `/i18n/*`
  `no-cache`; fingerprinted `*.js`/`*.css` `public,max-age=31536000,immutable`.

**Rejected:** `MapStaticAssets` (knows only files present at publish; the bundle is copied in afterwards).

### D52 — Front-end authentication
`ACCEPTED` (implements D29 and D48 on the client; refined by D59)

- **Session.** `SessionService` keeps the access token in a private field, never
  in `localStorage` or `sessionStorage` (tested). State `unknown` →
  `authenticated` | `anonymous`, plus the user from `/me`.
- **Startup.** An app initializer calls refresh then `/me` before the first render;
  it never rejects and gives up after 10 s; 401, 429, 5xx, network failure or
  timeout mean anonymous at once, no retry. `index.html` shows a text-free spinner.
  Known edge (accepted): two tabs opened at the same moment; the loser shows login.
- **Interceptor.** `Authorization` only on same-origin `/api/` requests, never on
  login, refresh or logout. On a 401 `error.auth.unauthorized`: one shared
  refresh, one retry; a request carrying an older token retries with the current
  one without refreshing; a second rejection ends the session. A 403 or another
  401 passes through. Refresh is **reactive only**.
- **Grace window.** During an active session a refresh that gets 401
  `invalid_refresh_token` is retried **once** after 1 s + 0–500 ms jitter, inside
  the single flight. A second 401 ends the session:
  `/login?returnUrl=<current>` with a one-time "session expired" notice. 429, 5xx
  and network failures are transient: no logout, no retry.
- **returnUrl.** `safeReturnUrl`: a single leading `/`, no backslash or control
  characters, not `/login`, ≤ 2048 characters, the same after one decode;
  otherwise `/`. Router only, never `window.location`.
- **Routes.** `/login` (guest guard), `/forbidden`, `/` (auth guard), a catch-all
  running the auth guard first. `permissionGuard(name)` sends a user without the
  permission to `/forbidden`.
- **Permissions.** `permissions.ts` names what the UI asks about;
  `npm run check:permissions` (part of `lint`) fails when a name is not in
  `Permissions.cs`. `*cbCan` and `can()` are UX only.
- **API client and errors.** One method per endpoint; `src/api/types.ts` derives
  types from `schema.d.ts`. `parseApiError` is the only reader of ProblemDetails:
  `{kind, status, key, fieldErrors, correlationId, retryAfterSeconds}`; a title
  that is not a well-formed `error.…` key becomes `error.unexpected`; status 0 is
  `error.network`. `ErrorMessageService.keyFor` falls back to `error.unexpected`.
  The correlation id is shown only for unexplained failures.
- **Login form.** Signal Forms; required, ≤ 256; same message for unknown user and
  wrong password; 423/429 show "about N min" from `Retry-After`; the password is
  cleared after a failure.
- **Dev proxy.** `proxy.conf.json` forwards `/api` to `http://localhost:8080`
  (`changeOrigin: false`); the Origin check passes without back-end changes.
- **Back-end `error.*` keys.** `scripts/backend-error-keys.mjs` reads the C# under
  `src/ClinicBooking.*` (not tests, bin, obj, Migrations), skipping comments;
  `check:i18n` fails when a key lacks a translation. A prefix literal `"error."`
  must be declared in `scripts/backend-error-keys.json` (today `error.http.*`
  from `ProblemDetailsEnricher`, and `ValidationFilter`); undeclared or stale
  declarations fail. Without sources it warns; with `CI` set it fails.
- **OpenAPI guard.** The document is generated from a host without test
  controllers (`PlainApiFactory`); tests fail if an operation lacks a success
  schema (204 excepted), a test path appears, or the auth schemas are missing.

**Rejected:** proactive (timer) refresh (throttled timers, clock drift, multi-tab collisions); a stored "was signed in" hint (can drift); the token in browser storage.

### D53 — Front-end feature pattern (Specialties screens)
`ACCEPTED` (implements D50 on the client; the reference for every entity screen)

- **Layout:** `src/api/<entity>-api.ts`; `src/app/shared/ui/` (`confirm-dialog`,
  `pager`, no strings of their own); `src/app/features/<entity>/` with
  `<entity>.routes.ts`, `<entity>.scope.ts`, `list/` (+ `list-query.ts`), `form/`,
  `<entity>-session.ts`; strings in `public/i18n/<entity>/{ar,en}.json`.
- **Translations.** Screen strings in the scope; **the entity's `error.*` keys in
  the root files** (the back-end key check reads them). A resolver preloads the
  scope before render.
- **List: the URL is the state** (`?q=&page=&size=&sort=&dir=`, defaults omitted).
  `parseListState` clamps invalid values to defaults; `toQueryParams`;
  `toApiQuery` always explicit. Defaults in one constant: Arabic name ascending,
  page 1, 20 per page; sizes 10, 20, 50. `rxResource` cancels stale requests; the
  previous result stays (`aria-busy`); Retry calls `reload()`. Search: 300 ms
  debounce into a `replaceUrl` navigation, Enter at once, IME ignored, sent trimmed,
  blank omitted, `maxlength` 100; any change returns to page 1. A page past the end
  steps back. The client sends PascalCase query names; allowed sort values are
  mirrored in `list-query.ts`.
- **List layout:** a real `<table>` (caption, `th scope`, `aria-sort`) from md up,
  cards below, sharing the action template; one sort control; the UI-language
  name first; every name in a `<bdi>` with its `lang`/`dir`; interpolation only
  (stored HTML shows as text). Pager is text-only. Dates through `intl`.
- **Create and edit:** separate pages; Signal Forms mirroring the API (trimmed,
  required, ≤ 100) with its keys; each field with its own `lang`/`dir`. After
  save: back to the remembered list query, a one-time `role="status"` message,
  focus on the heading (every feature page focuses its `h2` on entry).
- **Server errors:** a 400's field keys and the name-taken 409s go on their field
  from `submit()`; anything else is a form-level `aria-live` message; 404 on save
  shows "not found".
- **Conflict (409 `error.concurrency.conflict`):** keep the input, a focused
  `role=alert` banner, **Save disabled until Reload**; Reload loads the latest
  values and `rowVersion` and lists the user's earlier entries in a dismissible
  panel; 404 on reload says the record is gone.
- **Delete:** `confirm-dialog` on native `<dialog>` (`showModal()`), focus on
  Cancel, both names shown, Esc ignored while pending; success reloads, shows a
  status message, focuses the heading; 404 = already deleted; other errors stay in
  the dialog, translated.
- **Shell:** a header link (`routerLinkActive`, `aria-current`).
- Known minor point: a lazy chunk is fetched before its guard runs for a
  signed-out deep link (nothing sensitive; `canMatch` would avoid it).

**Rejected:** a modal for create and edit (deep links, back button, focus, phones and RTL are simpler with pages).

### D54 — Azure deployment is postponed until the build is complete
`ACCEPTED` (replaces the old "no feature before Azure" rule; D16 and D17 stay as the target)

- Decided 4 Oct 2026 by the owner: deploy to Azure only when the build is
  complete; the owner declares that point.
- Meanwhile: CI green on every push to `main` and every PR, and green before the
  next step; every commit builds; `Dockerfile` and compose keep working; no
  secret in the repository; every postponed item stays on the checklist in
  `docs/release.md`.
- Phases 1 to 5 remain the scope, in order; Appointments (Phase 2) do not start
  before Phase 1 is complete. Not released until the checklist is done.
- O1 and O2 are deferred with the deployment and block no feature.

**Why:** a new Azure free account's credit lasts 30 days; spending it before there
is anything to deploy wastes it.
**Accepted risk:** Azure-only problems (ingress, identity, Key Vault, Azure SQL,
migrations) are found late.

### D55 — Clinics back end
`ACCEPTED` (follows D50; refines D34, D38, D48)

- Copies Specialties (soft delete, audit, `RowVersion`, normalised unique names,
  `Select`, validators, error keys).
- **Fields:** `NameAr`, `NameEn` (as Specialty); `Address` optional, trimmed,
  ≤ 300, `nvarchar(300)`, blank stored as null; `Phone` optional, `nvarchar(16)`.
  Unique filtered indexes `UX_Clinics_NameArNormalized` /
  `UX_Clinics_NameEnNormalized` with conflict keys. No index on phone.
- **Endpoints** `/api/clinics`, the Specialties shape; responses add `address`
  and `phone`. **`PUT` is a full replace:** an omitted or blank address or phone
  is cleared.
- **Search** matches both names only.
- **Permission:** global `clinics.manage` for create, edit, delete. Any signed-in
  user reads.
- **Error keys:** `error.clinic.name_ar_required|too_long|invalid|taken` (and
  `name_en_*`), `error.clinic.not_found`, `error.clinic.address_too_long`,
  `error.clinic.phone_invalid`. `error.clinic.in_use` arrives with Doctors.
- **`PhoneNumber`** (Domain/ValueObjects, no package; reused by Patients). Stored
  as E.164. Ignores spaces, hyphens, dots, parentheses, bidi and invisible format
  characters; folds Arabic-Indic and Persian digits. Accepts the international form
  (`+…` or `00…`, 8–15 digits, first digit not 0) and Egyptian national numbers
  with one leading 0 (mobile `01[0125]` + 8 digits; landline `0[2-9]` + 7 or 8
  digits), stored as `+20…`. Rejects anything else, a number with neither `+` nor
  a leading 0, and raw input over 32 characters. Blank = absent. One key:
  `error.clinic.phone_invalid`.
- **A value that slips past a validator is a 400, never a 500:**
  `Clinic.SetContact` and `PhoneNumber.Normalize` throw `InvalidRequestException`
  with the key. Every later entity follows this.
- **Seeder top-up:** see D48 (each global permission at most once, D57).
- Name rules are copied (`ClinicRules`), not shared with Specialties; a shared
  helper comes at the third copy.

**Rejected:** a unique index on phone (clinics may share a switchboard); searching address or phone (not normalised, surprising matches; moved to Later).

### D56 — Clinics screens (front end)
`ACCEPTED` (follows D53; implements D55 on the client)

- Copies the Specialties screens: `/clinics`, `/clinics/new`, `/clinics/:id/edit`,
  scope `clinics`; `permissionGuard(Permissions.ClinicsManage)` on the forms; a
  "Clinics" header link.
- **Copied, not shared.** `list-query.ts`, the session service and the list and
  form components are copies. **At the third entity (Doctors or Patients), decide
  what to extract** (list state, session service, scope resolver, form error
  mapping) from what the three copies share. Until then a fix in one copy is made
  in the others.
- **Phone display** (`formatPhone` and the `phone` pipe, `core/format`): pure,
  never throws; non-strings and blanks give `''`; never changes the stored value.
  After `+20`: mobile `1[0125]` + 8 → `010 1234 5678`; landline 9 digits starting
  2 or 3 → `02 2345 6789`; 8 digits starting 2–9 → `03 123 4567`; 9 digits
  starting 4–9 → `040 312 3456`. Anything else shown exactly as stored. Always in
  `<bdi dir="ltr" class="whitespace-nowrap">`; plain spaces.
- **`tel:` links on cards only** (the `href` is the stored E.164); none in the table.
- **List:** columns Clinic (both names), Phone, Address (`line-clamp-2`, full text
  in `title` and the DOM), Created (`lg` up), Actions. Cards show the full address
  (`break-words`, `<bdi dir="auto">`). Missing values show a "Not set" key. The
  search placeholder says it matches the clinic **name**.
- **Form:** address `<textarea dir="auto">` with a counter of the trimmed length
  (not a live region); phone `type="tel"`, `inputmode="tel"`, `dir="ltr"`, a hint
  with examples. Client rules: names as before, address ≤ 300 after trimming, phone
  raw input ≤ 32 only; everything else is the server's. Blank address and phone
  are sent as `null`.
- **Untouched-phone guard:** the form keeps the stored E.164 and the text shown;
  if the field still holds that text the stored value is sent back unchanged,
  otherwise exactly what was typed. Refreshed after a conflict Reload.
- **Header:** wraps (`flex-wrap`), user name truncated (`max-w-40 truncate`),
  smaller padding below `sm`; no horizontal overflow at 360 px (by-hand check).

**Rejected:** extracting shared code at two entities (a guess about what varies).

### D57 — User management and clinic-scoped permissions (back end)
`ACCEPTED` (refines D6, D34, D48, D55)

- An administrator with global `users.manage` creates users, disables and
  re-enables them, and grants global and per-clinic permissions. Users are
  **never deleted**, only disabled. No roles; the JWT carries no permission.
- **Layers.** `UserService` (Application) depends on `IUserAccounts`
  (Infrastructure, wraps `UserManager`), `IPermissionChecker`, `IAppDbContext`,
  `TimeProvider`. `IAppDbContext.InSerializableTransactionAsync` runs
  read-then-write rules; a deadlock victim (1205) becomes 409
  `error.concurrency.conflict`.
- **Data.** `AspNetUsers` gains `IsActive` (existing rows migrated to `true`) and
  `MustChangePassword` (`false`); no database defaults, every insert explicit.
  `UserClinicPermissions` (`AuditableEntity`): `UserId`, `ClinicId`, `Permission`
  (`varchar(64)`), unique `UX_UserClinicPermissions_User_Clinic_Permission`
  (conflict key `error.concurrency.conflict`), index on `ClinicId`; FK user
  cascade, clinic restrict. **Revoking deletes the row.** A soft-deleted clinic
  keeps its rows but grants nothing and is not listed; a disabled user fails every
  check. The normalised user-name index carries `error.user.user_name_taken`.
- **Permission model.** `Permissions.Global`: `users.manage`,
  `specialties.manage`, `clinics.manage` (later `patients.*`).
  `Permissions.ClinicScoped`: **`doctors.manage` only**. No `doctors.read`:
  reading doctors stays open to any signed-in user. `GET /api/permissions` returns
  `{ global, clinicScoped }` from the constants.
- **The seeded admin has no clinic-scoped permission**; it grants itself
  `doctors.manage` through the API like any user.
- **Checker.** `IPermissionChecker`: `HasClinicPermissionAsync`,
  `HasClinicPermissionInAnyAsync`, `HasAnyClinicPermissionInAnyAsync`,
  `GetClinicIdsWithPermissionAsync`, `GetClinicPermissionsAsync`; every check
  reads the database; global never satisfies clinic-scoped and the reverse.
- **Handler.** The policy builder uses `ClinicPermissionRequirement` for
  `ClinicScoped` names; `ClinicPermissionAuthorizationHandler` asks the
  `IClinicResolver`s in order (default `RouteClinicResolver`, route value
  `clinicId`). No clinic resolved, a missing or deleted clinic, a disabled user,
  or a grant only in another clinic: 403. A resource addressed by its own id:
  `IClinicAccess.RequireAsync(clinicIds, permission, notFoundKey)` in the service.
- **404 vs 403:** see D6 and `docs/guides/clinic-permissions.md`. A global
  administrator is not a clinic member.
- **Endpoints** (all need `users.manage`; every `PUT` is a full replace):
  `GET /api/users` (`Search`, `IsActive`, `Page`, `PageSize` ≤ 100, `SortBy`
  `userName`|`createdAt`, `SortDirection`); `GET /api/users/{id}` (summary,
  `globalPermissions`, `clinicPermissions`); `POST /api/users` (`userName`,
  `temporaryPassword`; starts with `MustChangePassword = true` and no permission);
  `POST /api/users/{id}/disable` and `/enable` (idempotent);
  `PUT /api/users/{id}/global-permissions`;
  `PUT /api/users/{id}/clinics/{clinicId}/permissions` (empty list removes all;
  unknown user 404 `error.user.not_found` checked first, unknown or deleted clinic
  404 `error.clinic.not_found`); `POST /api/users/{id}/reset-password` (D58);
  `GET /api/permissions`.
- **Lock-out safety** (inside the serializable transaction): no disabling
  yourself (422 `error.user.cannot_disable_self`); the last active holder of
  `users.manage` cannot be disabled nor lose it (422 `error.user.last_administrator`).
- **Validation.** User name 3–64, ASCII letters, digits, `.`, `_`, `-`
  (`error.user.user_name_required|too_short|too_long|invalid`), unique ignoring
  case (409 `error.user.user_name_taken`). Passwords: the validator checks only
  required and ≤ 128 (`error.password.required|too_long`); the policy's refusals
  come back as `error.password.too_short|requires_digit|requires_lowercase|
  requires_uppercase|requires_unique_chars` on the field. Permission names must be
  in the right list (`error.user.permission_unknown`).
- **Concurrency:** `ApplicationUser` has no row version, so permission
  replacements are last-write-wins (an exception to D50's `rowVersion` rule).
- **Logs:** one Information line per create, grant, revoke, disable, enable, reset
  and password change, with ids and permission names only: never a password, a
  user name or patient data (tested against real console output).

**Rejected (for now):** roles; renaming users; email/SMS/invitation links; two-factor; `doctors.read`.

### D58 — Account state, change-password and admin reset (back end)
`ACCEPTED` (refines D48 and D57)

- **Account gate.** `AccountStateMiddleware` (after authentication, before
  authorization) reads `IsActive` and `MustChangePassword` from the database on
  every authenticated, non-anonymous request (never a claim, never cached).
  Missing or disabled user: 401 `error.auth.unauthorized`. `MustChangePassword`:
  403 `error.auth.password_change_required`, except on
  `[AllowWhilePasswordChangeRequired]` endpoints — **`GET /api/auth/me` and
  `POST /api/auth/change-password` only** (a reflection test enforces it). The
  gate ignores lockout. The rate limiter runs after authentication.
- **Disabling.** Login treats a disabled account exactly like a wrong password
  (same 401 and timing; checked before lockout). Disabling revokes every refresh
  token of the user in the same transaction. Re-enabling restores no session.
- **Change-password.** `POST /api/auth/change-password`, `{ currentPassword,
  newPassword }`, 204. A wrong current password counts as a failed login (lockout
  423 as at login); a right one resets the counter. Own rate limit, per user id
  (`RateLimiting:ChangePasswordPerMinute`, default 10). Errors: 400
  `error.auth.current_password_incorrect` (on `currentPassword`), 400
  `error.auth.password_unchanged` (on `newPassword`), 400
  `error.validation.failed` with policy keys. Runs the `SameOriginFilter`. **Other
  sessions end, the current one survives** (found from the refresh cookie); if the
  cookie cannot name a session, all sessions end. Success clears
  `MustChangePassword`.
- **Admin reset.** `POST /api/users/{id}/reset-password` (`users.manage`,
  `{ temporaryPassword }`, 204): policy applied, new security stamp,
  `MustChangePassword = true`, lockout cleared, every refresh family revoked; a
  disabled user stays disabled; not on your own account (422
  `error.user.cannot_reset_own_password`); unknown user 404.
- **Passwords** (temporary, new, current) are accepted only in a request body and
  never returned, logged, put in an exception or echoed in an error (tested).

**Rejected:** caching the gate (loses the immediate cut-off); a session-id claim in the JWT; self-service "forgot password", password history, breached-password check (not built).

### D59 — User-management and change-password screens (front end)
`ACCEPTED` (implements D57 and D58; refines D52; follows D53 and D56)

- **Areas.** `/change-password` (scope `account`, any signed-in user) and
  `/users`, `/users/new`, `/users/:id` (scope `users`, `authGuard` +
  `permissionGuard(users.manage)` on the parent route). Types from `schema.d.ts`
  only (`users-api.ts`, `AuthApi.changePassword`).
- **Session model.** `CurrentUser` has `mustChangePassword` and
  `clinicPermissions`. `SessionService` adds `canIn(permission, clinicId)`,
  `canInAny(permission)`, `isCurrentUser(id)`, `refreshUser()`,
  `requirePasswordChange()`, `clearPasswordChangeRequired()`. **`can()` stays
  global-only.** Ids are compared through `Number()` (int64 arrives as number or
  string). `*cbCan="'doctors.manage'; clinic: id"`. No clinic-aware route guard yet.
- **Forced change.** While `mustChangePassword` is true the user reaches only
  `/change-password`, sign-out and the language switcher. `authGuard` and
  `permissionGuard` redirect to `/change-password?returnUrl=<url>`, so every route
  inherits it; the page uses `changePasswordGuard` (no loop). The interceptor, on a
  403 `password_change_required` from a same-origin `/api/` call (not login,
  refresh, logout), calls `requirePasswordChange()` once (shared navigation); the
  request still fails. After success: `refreshUser()` (if it fails, clear the flag
  locally), then the `safeReturnUrl` or `/`. The header hides navigation meanwhile.
- **Change-password page.** Current, new, confirm. Client checks: required,
  ≤ 128, confirm equals new (`account.mismatch`). Server errors on their fields;
  423 and 429 form-level with "about N minutes". Voluntary variant stays on the
  page with a focused `role="status"`. A visible policy hint (**update it together
  with D48**): "at least 12 characters, with an upper-case letter, a lower-case
  letter and a digit". `autocomplete` `current-password` / `new-password`.
- **Secrets.** Read once, sent once; the field is emptied **before** the answer
  comes back, the show toggle reset, the form pristine; never in the URL, router
  state, `history.state`, storage, a long-lived signal, a log or an error. Toggles:
  a fixed label with `aria-pressed`. Server messages for a field are kept as keys
  in a small signal.
- **Users list.** User name (always LTR), status in words, a "password change
  required" indicator, created date, an "Open" link; search by user name, status
  filter (all, active, disabled), sort (user name default, created). URL
  `?q=&status=&page=&size=&sort=&dir=`; `list-query.ts` copied from Clinics.
- **Create user.** User name (3–64, `^[A-Za-z0-9._-]+$`) and temporary password
  (required, ≤ 128); the name is kept after a failure, the password never; success
  opens the user's page with a message that never contains the password.
  `autocomplete="new-password"`.
- **User detail.** Enable (immediate), disable (confirm, focus on Cancel), reset
  password (a `confirm-dialog` with a field, focus on the field through
  `focusSelector`; every close empties it), then the two editors. **Your own row
  has no disable or reset.** Every save reloads the detail and shows the server's
  state. Saving your own permissions calls `refreshUser()`; losing `users.manage`
  leaves for `/`.
- **Permission editors.** Names from `GET /api/permissions`, never hard-coded
  twice. Labels: one object `users.permissions.<area>.<action>.{label,
  description}`, read with `translateObject`. `check:permissions` compares
  `Permissions` with `Permissions.Global` and `ClinicPermissions` with
  `Permissions.ClinicScoped`, and fails when a C# name lacks a label in
  `users/ar.json` or `users/en.json`. `check:i18n` exempts `users.permissions.`
  from the unused-key warning.
- **Per-clinic editor.** One card per clinic the user has (both names), each with
  its own Save (full replace, offered only when changed) and "Remove all"; unsaved
  edits of one card survive another's save. Adding a clinic: search over
  `GET /api/clinics` (`PageSize` 20, debounced, a "refine" hint, clinics already
  listed not offered).

### D60 — Documentation layout for several assistants
`ACCEPTED` (supersedes D42)

- **`AGENTS.md`** (repository root) is the single source of instructions for
  every assistant and the owner: reading protocol, non-negotiable rules,
  structure, conventions, domain rules, workflow. Codex reads it directly;
  **`CLAUDE.md`** only imports it (`@AGENTS.md`) and holds Claude-only notes.
- **`docs/STATUS.md`**: what is done, the next step, scope, by-hand checks,
  Later. Read at the start of every session, updated at the end.
- **`docs/decisions.md`**: decisions in the compact format at the top of this
  file. Implementation reports, test lists and "not verified" lists do not go here.
- **`docs/guides/`**: task playbooks, read only for the matching kind of task.
- **`docs/release.md`**: Definition of Done, CI coverage, the deferred-until-deployment checklist.
- **`docs/archive/`**: the full text of D1–D59 as of 2026-10-06, frozen.

**Why:** the owner works with Claude Code, Codex and alone; every one of them must
read the same rules, and a session should load only what its task needs.
**Rejected:** instructions only in `CLAUDE.md` (Codex does not read it); importing
every document into `CLAUDE.md` (imports load in full every session); dropping the
rejected alternatives (an assistant would propose them again).

### D61 — Doctors back end
`ACCEPTED` (refines D32, D35, D43, D50, D55, D57; refined by D62)

- **Doctor.** `NameAr`, `NameEn`: required, trimmed, ≤ 100, normalised copies for
  search (D49), **no unique constraint** (people share names). No phone, no other
  personal field, no link to a user account. Soft-deletable, audited, `RowVersion`.
  `PUT` is a full replace of names and specialties (D55); clinics have their own
  endpoints.
- **Endpoints** `/api/doctors`: `GET` list, `GET {id}`, `POST`, `PUT {id}`,
  `DELETE {id}`; `POST {id}/clinics/{clinicId}` (add), `POST
  {id}/clinics/{clinicId}/activate` and `/deactivate` (idempotent), `GET` and `PUT
  {id}/clinics/{clinicId}/working-hours`, `POST {id}/slot-durations`. Add,
  activate, deactivate and slot change return the doctor (200).
- **Reading** (list, get, working-hours `GET`) is open to any signed-in user (D57);
  the working-hours `GET` does not go through the policy.
- **List** as Specialties (D50), plus `ClinicId`, `SpecialtyId` and `IsActive`.
  `IsActive` applies only with `ClinicId` (that assignment's state); alone it is
  400 `error.doctor.is_active_requires_clinic`, never ignored. List and detail
  share one shape: id, both names, `specialties` (id, both names), `clinics`
  (clinicId, both names, isActive; live clinics only), `slotMinutes` (in effect
  today), `pendingSlotChange` (slotMinutes, effectiveFrom, or null), createdAt,
  updatedAt, rowVersion. Working hours are not in it.
- **Specialties** many-to-many (`DoctorSpecialties`, rows hard-deleted, D35): 1 to
  10, set by the doctor's `POST`/`PUT`. An unknown or soft-deleted specialty: 400
  `error.doctor.specialty_unavailable` on `specialtyIds`. Duplicate ids in any list
  are collapsed. Deleting a specialty used by a live doctor: 409
  `error.specialty.in_use` (D50).
- **Clinic assignments** (`DoctorClinics`, `IsActive`). **Rows are never deleted**
  (refines D35 for this join table): deactivating means the doctor stopped working
  there, reactivating that they returned; a wrong assignment is deactivated. New
  assignments start active. An existing assignment, active or not: 409
  `error.doctor.clinic_already_assigned`. Deleting a clinic with any assignment of a
  live doctor, active or inactive: 409 `error.clinic.in_use` (D55). Soft-deleted
  clinics are ignored everywhere (D57).
- **Authorization** (`doctors.manage`, clinic-scoped). Create: 1 to 20 clinics,
  the permission in **every** one (unknown, deleted or not held: 403
  `error.auth.forbidden`). Add, activate, deactivate, working-hours `PUT`: in that
  clinic (policy, route value `clinicId`). Edit names and specialties, slot change:
  in **any** of the doctor's clinics (`IClinicAccess`: 404 `error.doctor.not_found`
  when none). Delete: in **all** of them; only some: 403
  `error.doctor.all_clinics_required`. "The doctor's clinics" means every
  assignment, active and inactive, so a doctor with every assignment inactive stays
  manageable and can be reactivated.
- **404 order on `{id}/clinics/{clinicId}` routes**, after authorization: unknown
  or deleted doctor 404 `error.doctor.not_found`; unknown or deleted clinic 404
  `error.clinic.not_found` (working-hours `GET` only; the policy gives 403 on the
  others); not assigned 404 `error.doctor.clinic_not_assigned` (activate,
  deactivate, working hours).
- **Working hours** (D12, D32) per assignment and weekday, several periods a day,
  Cairo `TimeOnly`, whole minutes, start before end, never crossing midnight;
  breaks are the gaps (no break model). Body `{ periods: [{ dayOfWeek, start, end
  }], rowVersion }`, at most 50 periods, empty = no hours. **`dayOfWeek` is .NET
  `DayOfWeek` (0 = Sunday)**; the screens order the week from Saturday, a
  front-end concern. Times "09:00" or "09:00:00". Input faults are 400 on the
  field (`error.doctor.periods_required|periods_too_many|period_day_invalid|
  period_start_required|period_end_required|period_time_invalid|
  period_end_not_after_start`). 422: `error.doctor.periods_overlap` (same day,
  same clinic), `error.doctor.period_overlaps_other_clinic` (the doctor's periods in
  any **other active** assignment, same weekday), `error.doctor.
  period_shorter_than_slot` (against the duration **in effect today**, Cairo).
  Periods that only touch do not overlap. Saving is allowed on an inactive
  assignment and skips the cross-clinic check there; its periods are kept and
  ignored by that check. **Reactivating re-runs it** and is 422 when the kept
  periods now overlap. Full replace, rows hard-deleted; checks run in a
  serializable transaction.
- **Refinement (D62): the working-hours 422s name what they refuse.**
  `BusinessRuleException` carries numeric extension members only (no text can
  leak). `periodIndex` is the index in the request: the later of the first
  overlapping pair, the first period shorter than a slot, the first period
  overlapping another clinic. `conflictingClinicId` names the other clinic of an
  overlap. Reactivation sends only `conflictingClinicId` (no list was sent). The
  request accepts times as "HH:mm" (tested against the API).
- **Slot duration** (D43): history rows `(DoctorId, SlotMinutes, EffectiveFrom)`,
  5 to 120 minutes in steps of 5 (`error.doctor.slot_minutes_required|invalid`).
  Required at creation, effective from the Cairo creation day. A change needs a
  date **after today** in Cairo (422 `error.doctor.effective_from_not_future`;
  missing: 400 `error.doctor.effective_from_required`). **At most one pending
  change**; a new one replaces it by a hard delete. There is no cancel (Later).
  **Existing working-hour periods are not revalidated against a new duration.**
- **Concurrency.** Doctor `PUT`: the doctor's `rowVersion`; a change to the
  specialties alone still moves it (`IAppDbContext.MarkModified`). Working-hours
  `PUT`: the **assignment's** `rowVersion` (from the working-hours `GET`); saving
  hours, activating and deactivating move it. Add, activate, deactivate and slot
  change are `POST`s without a row version. Create, edit, hours, add, activate,
  slot change and the specialty/clinic deletes run read-then-write rules in
  serializable transactions (D57).
- **Phase 2 seam** `IDoctorScheduleGuard` (Phase 1: `NoAppointmentsScheduleGuard`,
  allows everything): deleting a doctor with upcoming appointments (D35),
  deactivating an assignment with future appointments, a slot change before the
  last active appointment (D43), a working-hours change leaving a future
  appointment outside its period or off the grid (D43). **Leave days** come with
  Appointments.
- **Shared code** (the third copy, D55): `CommonRules` (name, row version, list
  rules), `NamedListQuery` + `NameSortFields`, `NamedListing` (every-word search,
  ordering, paging) over the Domain interface `IBilingualName`, `ConcurrencyGuard`
  (row-version check, guarded save). Confirmed by the owner.
- **Persistence.** No cascade from `Doctor` (a soft delete keeps every row).
- **Split queries are the global default** (`UseQuerySplittingBehavior(SplitQuery)`
  in Infrastructure), because a doctor projects three collections. This changed
  the behaviour of **every existing query** that loads or projects a collection,
  not only Doctors; the full test suite passed after the change.
- **EF warning 10622 is ignored** (`PossibleIncorrectRequiredNavigationWithQuery
  FilterInteractionWarning`: a soft-delete-filtered principal at the required end
  of a relationship). Accepted **only** for `DoctorClinic → Clinic` and
  `DoctorSpecialty → Specialty`, where hiding the row of a deleted clinic or
  specialty is the intended rule (D57). The ignore is global, so **any new
  relationship that would raise this warning must be reviewed** on its own; it is
  not assumed covered by this decision.

**Why:** a doctor's history (where they worked, which hours, which durations) must
survive; authorization over every assignment keeps a doctor manageable whatever its
state.
**Rejected:** unique doctor names (people share names); deleting assignments (loses
history; a wrong one is deactivated); a separate break model (gaps are breaks);
several pending slot changes (one is enough until a need appears); `AsSplitQuery`
per query (needs the relational EF package in Application, rule 8).

### D62 — Doctors screens (front end)
`ACCEPTED` (follows D53, D56, D59; implements D61 on the client; refines D61)

- **Shared code at the third entity (D56).** Extracted only what Specialties,
  Clinics and Users had identically: `FeatureSession` (each feature keeps its own
  injectable subclass), `scopeResolver(scope)` (account too), and `list-params`
  (search, page, size, sort, direction, constants). Each feature keeps its own
  state, sort fields, filters and API mapping. Form error mapping differs between
  the copies and is not extracted.
- **Routes (UX only; the API decides).** `/doctors` (authGuard); `/doctors/new`
  with the new `clinicPermissionInAnyGuard(permission)` (`canInAny`, otherwise
  `/forbidden`; keeps the forced password change); `/doctors/:id`,
  `/doctors/:id/edit` and `/doctors/:id/clinics/:clinicId/hours` behind authGuard
  only, the page deciding after loading. A "Doctors" header link after Clinics.
- **Who sees what.** Edit and slot change: `canIn(doctors.manage, c)` for at least
  one of the doctor's clinics. Delete: for all of them. Per assignment row
  (working-hours link, Deactivate, Activate): for that clinic. Working hours are
  editable only with it for that clinic, otherwise read-only. The edit page for a
  user without it says so and shows no form.
- **List.** Columns: both names (D53), specialties, clinics (an inactive
  assignment marked in words), today's slot; cards below md; an "Open" link, no
  edit or delete in rows. URL `?q=&specialty=&clinic=&active=&page=&size=&sort=&dir=`;
  `active` is `all|active|inactive` (`all` omitted), enabled only with a clinic,
  reset when the clinic is cleared, never sent alone.
- **Reference lists.** Specialty and clinic selects are loaded once with
  `PageSize` 100, sorted by the UI-language name; when the API holds more, a
  visible "incomplete list" note (a searchable picker is under Later). The same
  applies to the specialties in the forms; the doctor's own specialties are always
  offered.
- **Clinic options** (create and "add a clinic") come from the session's
  `clinicPermissions` with `doctors.manage` (live clinics, both names), not from
  `/api/clinics`. **`refreshUser()` is called when the create page or the picker
  opens**, so a grant made after sign-in is offered at once. The picker leaves out
  clinics the doctor already has, active or inactive.
- **Create** (names, specialties and clinics as checkbox groups, slot select 5 to
  120 step 5, default 15): field keys on their fields
  (`error.doctor.specialty_unavailable` on specialties), 403 form-level; success
  opens the new doctor's detail with a one-time message. **Edit** (names and
  specialties, full replace with `rowVersion`, D53 conflict flow) returns to the
  detail with a one-time message.
- **Detail.** Summary with dates (`intl`); specialties and clinics sorted by the
  UI-language name (`Intl.Collator`, display order only: it does not fold hamza
  forms like the server's search). Activate is immediate; Deactivate has a
  confirm dialog (focus on Cancel). A refused reactivation (422) shows in its row
  and names the other clinic (`conflictingClinicId`, names from the doctor's
  clinics or the session; otherwise the generic message). Slot change: an inline
  form (minutes select, a date with `min` = tomorrow in Cairo from the browser
  clock), stating that it replaces the pending change; a 422 for the date goes on
  the date field. Delete: confirm dialog; `error.doctor.all_clinics_required` and
  other errors stay in it; success returns to the remembered list.
- **Working hours.** Doctor and clinic names; an "inactive" notice. **The API
  keeps .NET `DayOfWeek` (0 = Sunday); the screen shows Saturday to Friday**,
  mapped in one tested module (`week.ts`). Periods use `<input type="time">`
  (`dir="ltr"`, step 60) with add and remove; times are sent as **"HH:mm"**. The
  request is built in the displayed order (Saturday first, then by start), and
  one tested function maps request index to displayed period for both
  `periods[i].start|end` field errors and `periodIndex`. A 422 sits next to the
  period it names (an overlap names the other clinic); without an index it is
  form-level; 409 follows D53. The only client rule is "a time is entered". The
  dynamic list is kept in plain signals, not a Signal Forms tree. No "copy to
  other days" or templates.
- **Errors.** `parseApiError` reads `periodIndex` and `conflictingClinicId`
  (non-negative integers, number or digit string; anything else absent). A
  correlation id is shown only for unexplained failures (D52), through
  `supportReference(error)` in every form (Specialties, Clinics, Users, the global
  permissions editor, Doctors); lists, login and change-password already followed it.

**Why:** every control follows the clinic it concerns, so the screen never offers
what the API refuses; the API keeps its own weekday numbering and the screen owns
the local week order.
**Rejected:** a searchable picker for specialties and clinics now (100 is enough
today; Later); clinic options from `/api/clinics` (would offer clinics the user
cannot use); a native `<select multiple>` (poor on phones and with screen readers);
a route guard for edit (only the loaded doctor knows its clinics); form-level-only
422s (the owner wanted them next to the period).

---

## Open questions

O3, O4 and O5 are closed (D43, D44). O1 and O2 are deferred with the deployment
(D54) and block no feature work.

| # | Question | Status |
|---|---|---|
| O1 | Azure region — West Europe vs UAE North. Confirm Container Apps, ACR, Key Vault and Azure SQL Basic are all available there, then compare latency and price | OPEN |
| O2 | Custom domain and TLS, or the default Container Apps hostname | OPEN |
