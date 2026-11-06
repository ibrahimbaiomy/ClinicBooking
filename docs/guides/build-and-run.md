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
# By hand: docker compose build alone does not restart the running container; use
# docker compose up --build (otherwise the old image keeps serving the old front end).

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

## Recovering the protected administrator's password (D64)

The protected administrator (the first user: `IsProtected = 1` in `AspNetUsers`) cannot be
reset through the screens or the API (`error.user.protected_reset_refused`); only its owner
changes it, on the change-password page. If that password is lost, recovery is a database
procedure, run by the owner against the database (locally the compose SQL Server; in Azure,
Azure SQL), with a backup taken first. Never put the new password in a file, a commit or a
ticket.

**If another active user holds `users.manage`** (simplest; no hashing):

```sql
-- 1. Find the protected user and take the flag off for the duration of the reset.
SELECT Id, UserName, IsActive FROM AspNetUsers WHERE IsProtected = 1;
UPDATE AspNetUsers SET IsProtected = 0 WHERE Id = <id>;
```

2. Signed in as the other administrator, open Users → that user → Reset password, and give
   the owner the temporary password in person. The reset forces a change at the next sign-in
   and ends every session.
3. Put the flag back at once:

```sql
UPDATE AspNetUsers SET IsProtected = 1 WHERE Id = <id>;
```

**If no other administrator exists:** set a temporary password hash directly.

1. Generate a hash with the same Identity version as the API (10.0.x, default hasher
   settings). `dotnet fsi` downloads the package into the local NuGet cache only; nothing is
   added to the solution:

   ```bash
   cat > hash.fsx <<'EOF'
   #r "nuget: Microsoft.Extensions.Identity.Core, 10.0.12"
   open Microsoft.AspNetCore.Identity
   let password = System.Environment.GetEnvironmentVariable "NEW_PASSWORD"
   printfn "%s" (PasswordHasher<obj>().HashPassword(null, password))
   EOF
   NEW_PASSWORD='<a temporary password meeting the policy>' dotnet fsi hash.fsx
   rm hash.fsx
   ```

2. Store it, force a change at the next sign-in, clear the lockout, renew the security stamp
   and end every session (this is what the admin reset does, D58):

```sql
BEGIN TRANSACTION;
UPDATE AspNetUsers
   SET PasswordHash = N'<hash from step 1>',
       SecurityStamp = CONVERT(nvarchar(36), NEWID()),
       MustChangePassword = 1,
       AccessFailedCount = 0,
       LockoutEnd = NULL,
       IsActive = 1
 WHERE Id = <id> AND IsProtected = 1;
DELETE FROM RefreshTokens WHERE UserId = <id>;
COMMIT;
```

3. The owner signs in with the temporary password and must choose a new one at once.

Keep this procedure until a self-service "forgot password" exists (`STATUS.md`, Later).
