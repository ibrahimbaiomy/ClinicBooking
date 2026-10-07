# AGENTS.md — ClinicBooking

Instructions for any AI assistant (Claude Code, Codex, ...) and for the owner
working in this repository. Claude Code loads this file through `CLAUDE.md`.

An outpatient appointment booking system for multiple clinics, with two
required goals: a real, usable product (Arabic and English) and a complete
deployment pipeline (container → registry → Azure Container Apps, through
CI/CD) that doubles as a portfolio piece. The application is small on purpose.
Azure deployment is postponed until the build is complete (D54).

---

## Before writing any code

1. Read `docs/STATUS.md` (where the project is, what is next, the scope).
2. Read the index at the top of `docs/decisions.md`. Then read in full every
   decision marked `*`, every decision whose keywords match the task, and every
   decision those reference. When unsure whether one is relevant, read it. Read
   an `OPEN` question in full when the task depends on it.
3. Read the guide for the kind of task:

   | Task | Guide |
   |------|-------|
   | Back-end entity, endpoint, error keys | `docs/guides/backend-entity.md` |
   | Clinic-scoped permission or endpoint | `docs/guides/clinic-permissions.md` |
   | Front-end screen | `docs/guides/frontend-screen.md` |
   | Running, migrations, regenerating API types | `docs/guides/build-and-run.md` |
   | CI, Docker image, Azure, release | `docs/release.md` |

4. In your plan, list the decision numbers and guides you consulted, so
   coverage can be checked.

`docs/archive/` holds the full, older text of the decisions. Do not read it
unless a decision in `decisions.md` is ambiguous or silent on something it
clearly covers; if you do, say which part you read.

If a task conflicts with a rule below, or depends on an `OPEN` question, stop
and say so instead of guessing.

## After a task

- Update `docs/STATUS.md`: what is done, what is next, by-hand checks for the owner.
- A decision that the documents did not cover goes into `docs/decisions.md`
  (format at the top of that file) or is raised. Never decide silently.
- Session reports, test lists and "not verified" lists go into `STATUS.md` or
  the commit/PR, not into `decisions.md` (D60).

---

## Non-negotiable rules

1. **Never add a feature that is not in scope** (`docs/STATUS.md`). If a change
   seems to need one, stop and say so. Ideas go under "Later" in `STATUS.md`.
2. **Never write a user-facing string literal in a component.** Every string goes
   through Transloco and into both `ar.json` and `en.json`.
3. **Never use physical CSS direction utilities.** `ms-`/`me-`/`ps-`/`pe-`/
   `start-`/`end-`/`text-start`/`text-end`/`rounded-s`/`rounded-e`/
   `border-s`/`border-e` only. Mirror directional icons with `rtl:-scale-x-100`.
4. **Never return an English sentence from the API.** `ProblemDetails.title` and
   validation messages are keys: `error.appointment.slot_taken`.
5. **Never return or accept an EF entity in a controller.** DTOs only.
6. **Never put a secret in `appsettings.json`** or anywhere in the repo. Local
   secrets live in `.env` (git-ignored) or User Secrets.
7. **Never use `DateTime`.** `DateTimeOffset` for instants, `TimeOnly` for
   working hours.
8. **Never introduce a new NuGet or npm package** without saying why and getting
   agreement first.
9. **Never check access by role name or magic string.** Use the permission
   constants and policies; every clinic-scoped resource is authorized against
   the caller's clinics.
10. **Never log patient PII** (name, phone, national ID, free text), and never a
    password of any kind.
11. **Never run `docker` or `docker compose`** (no `up`, `down`, `build`, volume,
    `prune`, never `-v`). The owner runs Docker. Report Docker-dependent checks
    as **"by-hand checks for the owner"** with the exact commands. Exception:
    `dotnet test` starts SQL Server through Testcontainers; if Docker is not
    running, say so instead of starting it.

---

## Structure

```
ClinicBooking/
├── AGENTS.md                          this file (entry point for every assistant)
├── CLAUDE.md                          imports AGENTS.md for Claude Code
├── ClinicBooking.sln
├── src/
│   ├── ClinicBooking.Domain/          Entities, Enums, ValueObjects, Exceptions, Permissions
│   ├── ClinicBooking.Application/     Features, DTOs, Interfaces (incl. IAppDbContext), Validators, services
│   ├── ClinicBooking.Infrastructure/  Persistence (AppDbContext), Identity, EntityConfigurations, Interceptors
│   ├── ClinicBooking.Api/             Controllers, middleware, filters, Program.cs, wwwroot
│   └── clinic-booking-web/            Angular + TypeScript
│       └── src/api/                   openapi.json + schema.d.ts (generated, committed)
├── tests/ClinicBooking.Tests/         integration tests (Testcontainers)
├── docs/
│   ├── STATUS.md                      progress, next step, scope, Later
│   ├── decisions.md                   decisions (index first)
│   ├── release.md                     Definition of Done, CI coverage, deferred-until-deployment checklist
│   ├── guides/                        task playbooks
│   └── archive/                       full older text of the decisions (frozen)
├── Dockerfile                         multi-stage: node → dotnet sdk → runtime
├── docker-compose.yml                 api + sqlserver
├── .env.example                       dummy values; real .env is git-ignored
└── .github/workflows/ci.yml
```

- Application and Infrastructure each have a static `DependencyInjection` class
  (`AddApplication(this IServiceCollection)` / `AddInfrastructure(this
  IServiceCollection, IConfiguration)`, returning `IServiceCollection`), called
  from `Program.cs`.
- Every project has a `GlobalUsings.cs` (implicit usings are off).
- Application never references Infrastructure. Services depend on `IAppDbContext`.

## Stack

**Back end:** .NET 10, ASP.NET Core Web API, attribute-routed controllers, EF Core
+ SQL Server, FluentValidation, Serilog (JSON), JWT bearer, ASP.NET Core Identity.
**Front end:** Angular 22 (zoneless), TypeScript strict, Tailwind alone (no
component library), Transloco, Vitest.
**Infrastructure:** Docker and GitHub Actions today; ACR, Container Apps, Azure
SQL, Key Vault and OIDC are the target, deferred (D54).

---

## Conventions

### C#
- File-scoped namespaces, nullable enabled, `var` for obvious types.
- Async everywhere, suffix `Async`, always pass `CancellationToken`. No `.Result`,
  `.Wait()` or `async void`.
- Services: interface + implementation, scoped. One service per aggregate.
- Queries project with `Select` into DTOs; updates and deletes load the entity.
- Never `ExecuteUpdate` / `ExecuteDelete` (they bypass the audit and soft-delete
  interceptor; a test fails the build).
- Validation: one validator per request DTO and query object in
  `Application/Validators`, run by the global `ValidationFilter` (a test fails if
  one is missing). Validators check input only; business rules live in
  Application/Domain. Every message is a key `error.<area>.<reason>`.
- A Domain method that normalises or limits a value throws
  `InvalidRequestException` with a key, never a generic exception (D55).
- Never put `[Produces]` on a controller. Every action has
  `[ProducesResponseType]` for its success response (D52).
- Convert UTC ↔ Cairo only inside Application, only to validate rules.
- Permissions: constants in Domain, `[Authorize(Policy = Permissions.X.Y)]`, one
  policy per permission. Global vs clinic-scoped: see
  `docs/guides/clinic-permissions.md`.
- Secure by default: a fallback policy requires a signed-in user. Anonymous
  endpoints say so deliberately (`[AllowAnonymous]` / `.AllowAnonymous()`, D50).
- Never add `[AllowWhilePasswordChangeRequired]` to another endpoint: a test
  allows exactly `GET /api/auth/me` and `POST /api/auth/change-password` (D58).
- A new `"error.*"` literal in C# needs its translation in the **root**
  `ar.json`/`en.json` in the same change, or `check:i18n` fails.

### TypeScript / Angular
- Standalone components, selector prefix `cb`, OnPush, `templateUrl` only, no
  zone.js, strict, no `any`, `inject()`.
- Every user-facing string is a key (`{{ 'area.key' | transloco }}`,
  `translate('area.key')`). A key built at runtime carries
  `i18n-keys: a.b, c.d` on its line. A feature's keys live in
  `public/i18n/<scope>/`; an entity's `error.*` keys live in the root files.
- Dates and numbers through the `intl` pipe (`ar-EG-u-nu-latn-ca-gregory`),
  never `DatePipe`; instants show in Cairo time.
- Signals for local state; `HttpClient` services for server data
  (`httpResource` / `rxResource`). No state library.
- API types come from `schema.d.ts` (never hand-edited); one service method per
  endpoint in `src/api/`.
- Every `PUT` sends back its `rowVersion`; a 409 `error.concurrency.conflict`
  means "reload", never a silent retry or overwrite.
- Token refresh is single-flight (D48, D52).

### Git
- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`), in English.
- Commit after each verified step. A commit that does not build is never pushed.

---

## Domain rules that are easy to get wrong

- No two appointments for the same doctor overlap, across all clinics. Checked on
  create and reschedule (excluding the one being moved) **and** enforced by a
  unique filtered index on `(DoctorId, StartUtc) WHERE Status <> Cancelled` (D31, D43).
- An appointment falls inside the doctor's working hours for that clinic and
  weekday, outside breaks and leave, on the slot grid
  (`period start + n × duration`), occupying exactly one slot.
- One slot duration per doctor, the same in every clinic; a change takes effect
  only on a date after the doctor's last active appointment (D43).
- A doctor's working-hour periods in different clinics must not overlap on the
  same day. Working hours are Cairo `TimeOnly` + weekday, never absolute times.
- No booking in the past. A patient's overlapping appointment is a warning the
  user confirms, never a block.
- Status machine: `Booked → Completed | Cancelled | NoShow` only. A cancelled
  appointment frees its slot; a completed one does not.
- Booking references are unique and collision-checked on insert.
- Patients are shared across clinics and store only a name and a phone. A
  matching phone is a warning, never an error (D44).
- Soft delete applies to Specialties, Clinics, Doctors and Patients only (D35).
  Deleting a doctor with upcoming appointments needs confirmation and cancels
  them in the same transaction.
- Permissions are per clinic except the global ones (`users.*`, `specialties.*`,
  `clinics.manage`, `patients.*`). An inaccessible resource returns 404 (D6, D57).
- `/health/live` has no dependency checks; `/health/ready` checks the database.

---

## Workflow expectations

- **CI must be green before starting the next step.**
- **Verify before claiming done:** `dotnet build`, `dotnet test`, and in
  `src/clinic-booking-web`: `npm run lint`, `npm test`, `npm run check:i18n`,
  `npm run check:api`, `npm run build`. State what actually ran.
- **Changed a DTO or controller?** Run `npm run gen:api` and commit `openapi.json`
  and `schema.d.ts`; CI fails on any diff.
- **Prefer the smaller change.** If an existing pattern conflicts with a rule
  here, raise it rather than copying it forward.
- **Do not refactor unrelated code** while implementing a feature.
- **Open questions** (O-numbers) are never answered silently.
