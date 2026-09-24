# Instructions.md — ClinicBooking

Instructions for any AI assistant working in this repository.
Read this file and `docs/decisions.md` before writing code.

---

## What this project is

An outpatient appointment booking system for a multi clinics.

**The application is small on purpose.** Its reason for existing is to be
containerised, pushed to a registry, deployed to Azure Container Apps, and
kept deployable through a CI/CD pipeline. Deployment is the deliverable.

---

## Non-negotiable rules

1. **Never add a feature that is not in scope** (see `decisions.md`). If a
   change seems to need one, stop and say so instead of implementing it.
2. **Never write a user-facing string literal in a component.** Every string
   goes through `t('key')` and into both `ar.json` and `en.json`.
3. **Never use physical CSS direction utilities.** `ms-`/`me-`/`ps-`/`pe-`/
   `text-start`/`text-end` only.
4. **Never return an English sentence from the API.** Errors are keys:
   `error.appointment.slot_taken`.
5. **Never return or accept an EF entity in a controller.** DTOs only.
6. **Never put a secret in `appsettings.json`** or anywhere else in the repo.
7. **Never use `DateTime`.** `DateTimeOffset` for instants, `TimeOnly` for
   working hours.
8. **Never introduce a new NuGet or npm package** without saying why and
   getting agreement first.

---

## Structure

```
ClinicBooking.sln
├── src/
│   ├── ClinicBooking.Domain/          Entities, Enums, ValueObjects, Exceptions
│   ├── ClinicBooking.Application/     Features, DTOs, Interfaces, Validators
│   ├── ClinicBooking.Infrastructure/  Persistence, Identity, EntitiesConfigurations, Interceptors
│   ├── ClinicBooking.Api/             controllers, middleware, Program.cs, wwwroot
│   └── clinic-booking-web/       Angular + TypeScript
├── tests/
│   └── ClinicBooking.Tests/      integration tests (Testcontainers)
├── docs/
│   ├── Instructions.md
│   └── decisions.md
├── Dockerfile                    multi-stage: node → dotnet sdk → runtime
├── docker-compose.yml            api + sqlserver
└── .github/workflows/ci.yml
```
Application and Infrastructure must include static class DependencyInjection with static void function to add it's dependancies in Program.cs in Api
all projects must have GlobalUsings.cs for common used

## Stack

**Back end** — .NET 10, ASP.NET Core Web API, attribute-routed controllers,
EF Core + SQL Server, FluentValidation, Serilog (JSON), JWT bearer.

**Front end** — Angular, TypeScript, Tailwind.

**Infrastructure** — Docker, Azure Container Registry, Azure Container Apps,
Azure SQL, Key Vault, GitHub Actions.

---

## Conventions

### C#
- File-scoped namespaces, nullable enabled, `var` for obvious types.
- Async everywhere; suffix `Async`; always pass `CancellationToken`.
- Services: interface + implementation, scoped registration.
- Queries: project with `Select` into DTOs rather than loading entities.
- No `.Result`, no `.Wait()`, no `async void`.

### TypeScript / Angular
- Standalone components only (no NgModules).
- Use Angular Signals for local reactivity; Observables/RxJS for HTTP streams and events.
- API types come from `src/api/schema.d.ts`, generated — never hand-edited.
- One TanStack Query (or dedicated service) hook per endpoint in `src/api/`.
- Strict mode enabled: No `any`. Inject dependencies via `inject()` function over constructor injection where applicable.

### Git
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `test:`.
- English commit messages.
- Commit after each verified step, not in large batches.
- A commit that does not build is never pushed.

---

## Workflow expectations

- **Verify before claiming done.** `dotnet build`, `dotnet test`, and
  `npm run build` must all pass. State what actually ran.
- **Report what was decided that the instructions did not cover.** Every
  such decision either goes into `decisions.md` or gets raised.
- **Prefer the smaller change.** If an existing pattern conflicts with a rule
  here, raise it rather than copying the pattern forward.
- **Do not refactor unrelated code** while implementing a feature.

---

## Build and run

```bash
# full stack, local
docker compose up --build

# API alone (SQL Server must already be running)
dotnet run --project src/ClinicBooking.Api

# front end dev server (Angular CLI)
cd src/clinic-booking-web && ng serve

# tests
dotnet test

# regenerate API types after changing a DTO
npm run gen:api
```

---

## Domain rules that are easy to get wrong

- **An appointment may not overlap another for the same doctor.** Check on
  create and on reschedule, excluding the appointment being moved.
- **An appointment must fall inside the doctor's working hours** for that
  day of the week.
- **A cancelled appointment frees its slot**; a completed one does not.
- **Working hours are local Cairo times** stored as `TimeOnly` + day-of-week.
  Never store them as absolute timestamps — Egypt observes DST.
- **Booking references are unique and collision-checked on insert.**

---

## Definition of done, for the project as a whole

1. Runs in Docker locally with one command.
2. Image builds and pushes to Azure Container Registry from CI.
3. Deploys to Azure Container Apps, reachable over HTTPS.
4. Migrations applied by the pipeline, not by hand.
5. Secrets resolved from Key Vault via managed identity.
6. `/health` green.
7. Integration tests run in CI against a real database.
8. Both languages complete, RTL correct, no untranslated string.
9. `README.md` explains the architecture and shows the pipeline badge.

Anything beyond this list is out of scope until all nine are true.
