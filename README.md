# ClinicBooking

[![CI](https://github.com/ibrahimbaiomy/ClinicBooking/actions/workflows/ci.yml/badge.svg)](https://github.com/ibrahimbaiomy/ClinicBooking/actions/workflows/ci.yml)

ClinicBooking is an outpatient appointment booking system for multiple clinics.
It has two goals: a real, usable product, and a complete deployment pipeline
(container, registry, cloud, CI/CD) that doubles as a portfolio piece. The
application is small on purpose, and the pipeline is built first.

## Status

Early skeleton. What exists today: the four back-end layers, a persistence
layer (EF Core on SQL Server) with a first `Specialty` entity and migrations,
authentication (ASP.NET Core Identity, JWT access tokens, rotating refresh
tokens in an HttpOnly cookie) with permission-based authorization policies,
structured JSON logging, error responses carrying error keys, health endpoints,
a Docker image, and this CI workflow. The first feature API, Specialties (list,
Arabic-aware search, create, edit, soft delete), is in place with a committed
`openapi.json`; the other entities and the front end are not.

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

The API listens on <http://localhost:8080>. Check
<http://localhost:8080/health/ready>: it returns 200 once the database has been
created by the migration. Compose applies migrations at startup because it runs
the API in the Development environment; that is for local use only.

## Tests

```bash
dotnet test
```

Docker must be running: the tests start a throwaway SQL Server container.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every push to
`main` and on pull requests. It builds the solution in Release (warnings are
errors), runs all tests including the Testcontainers ones, and checks that the
Docker image builds. Nothing is pushed to a registry or deployed yet.
