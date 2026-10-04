# ClinicBooking

[![CI](https://github.com/ibrahimbaiomy/ClinicBooking/actions/workflows/ci.yml/badge.svg)](https://github.com/ibrahimbaiomy/ClinicBooking/actions/workflows/ci.yml)

ClinicBooking is an outpatient appointment booking system for multiple clinics.
It has two goals: a real, usable product, and a complete deployment pipeline
(container, registry, cloud, CI/CD) that doubles as a portfolio piece. The
application is small on purpose, and deployment to Azure is postponed until the
build is complete ([D54](docs/decisions.md)).

## Status

Deployment to Azure is postponed until the build is complete (see D54 in
[docs/decisions.md](docs/decisions.md), which lists what is deferred). CI builds
and tests on every push to `main` and on pull requests: back-end build and
tests, front-end checks, and a Docker image build.

What exists today: the four back-end layers, a persistence layer (EF Core on SQL
Server) with migrations, authentication (ASP.NET Core Identity, JWT access
tokens, rotating refresh tokens in an HttpOnly cookie) with permission-based
authorization policies, structured JSON logging, error responses carrying error
keys, health endpoints, and a Docker image. The first feature, Specialties
(list, Arabic-aware search, create, edit, soft delete), exists end to end: the API
with a committed `openapi.json`, and Angular screens. The Angular front end is
a strict, zoneless app with Tailwind, an Arabic-default language switcher with
RTL, generated API types, a login screen with silent session restore, and the
Specialties screens.

## Architecture

A .NET 10 solution in Clean Architecture, four projects plus tests:

- **Domain**: entities and exceptions. References nothing.
- **Application**: interfaces such as `IAppDbContext`. References Domain.
- **Infrastructure**: EF Core `AppDbContext`, entity configurations, the
  audit/soft-delete interceptor, migrations, health checks. References
  Application.
- **Api**: ASP.NET Core Web API, middleware, `Program.cs`. References
  Application and Infrastructure.
- **Tests**: integration tests with `WebApplicationFactory` and a real SQL
  Server in a Testcontainers container.

Errors are RFC 7807 `ProblemDetails` whose `title` is an error key, never an
English sentence. `/health/live` has no dependency checks; `/health/ready`
checks the database. The reasoning behind each choice is in
[docs/decisions.md](docs/decisions.md), and the working rules are in
[docs/Instructions.md](docs/Instructions.md).

## Run locally

Requires Docker.

```bash
cp .env.example .env     # then replace the placeholder values (JWT key, SQL and first-user passwords)
docker compose up --build
```

The API serves the Angular app and listens on <http://localhost:8080> (the shell page).
Check
<http://localhost:8080/health/ready>: it returns 200 once the database has been
created by the migration. Compose applies migrations at startup because it runs
the API in the Development environment; that is for local use only.

## Tests

```bash
dotnet test
```

Docker must be running: the tests start a throwaway SQL Server container. Front-end checks
(Node 24, in `src/clinic-booking-web`): `npm ci`, `npm run lint`, `npm test`, `npm run build`,
`npm run check:i18n`.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every push to
`main` and on pull requests. Three jobs run in parallel:

- **test**: builds the solution in Release (warnings are errors) and runs all tests,
  including the Testcontainers ones and the check that `openapi.json` is up to date.
- **web**: on Node 24, `npm ci`, lint (with the logical-properties check), unit tests,
  the translation-key check, the API-types check (`schema.d.ts` matches `openapi.json`)
  and the production build.
- **image**: checks that the Docker image builds.

Nothing is pushed to a registry or deployed: deployment to Azure is postponed until
the build is complete (D54).
