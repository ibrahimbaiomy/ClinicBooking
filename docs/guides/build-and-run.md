# Guide — build, run and regenerate

Commands the owner runs and the ones an assistant may run. Assistants never run
`docker` or `docker compose` (AGENTS.md rule 11): list those as by-hand checks.

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
