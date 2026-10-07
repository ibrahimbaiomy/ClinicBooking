# Release — ClinicBooking

Read for CI, Docker image, Azure or release work. Azure deployment is postponed
until the owner declares the build complete (D54). The project is not called
released until every item below is true.

---

## Definition of Done (project as a whole)

None of the eleven items is dropped; D54 splits them in two.

**Done, kept green by CI**
1. Runs in Docker locally with one command.
7. Integration tests run in CI against a real database.
8. Both languages complete, RTL correct, no untranslated string; the
   translation-key check passes in CI.
9. CI fails if `openapi.json` / `schema.d.ts` are out of date.
10. Lint and front-end tests pass in CI.
11. `README.md` explains the architecture and shows the pipeline badge.

**Required before release (deferred, D54)**
2. Image builds and pushes to Azure Container Registry from CI (OIDC, no stored
   Azure secret).
3. Deploys to Azure Container Apps, reachable over HTTPS.
4. Migrations applied by the pipeline (migrations bundle), not by hand.
5. Secrets resolved from Key Vault through managed identity.
6. `/health/live` and `/health/ready` green as the Azure probes (the endpoints
   exist and are tested locally).

## What CI covers today (`test`, `web`, `image` jobs, D47)

- **Item 7:** the `test` job.
- **Item 8, the parts CI can check:** `check:i18n` (ar/en parity, every key used
  exists, every back-end `error.*` key is translated, no literal text in
  templates) and `check:logical` (no physical direction classes or CSS
  properties). **Not covered:** how RTL looks in a browser, literal strings in
  `.ts` code, the quality of the Arabic wording.
- **Item 9, fully:** `OpenApiDocumentTests` (`test` job) and `npm run check:api`
  (`web` job).
- **Item 10, fully for what exists:** `npm run lint`, `npm test`, the production
  build. No end-to-end or browser tests.
- Items 2–5, and 6 as Azure probes: deferred.

---

## Deferred until deployment

Tick an item only when it is done and working on Azure.

**Definition of Done items that need Azure**
- [ ] Image pushed to Azure Container Registry from CI (item 2, D17).
- [ ] GitHub Actions authenticates to Azure with OIDC federated credentials, no stored service-principal secret (item 2, D40).
- [ ] Deployed to Azure Container Apps and reachable over HTTPS (item 3, D17).
- [ ] Migrations applied by the pipeline: an EF migrations bundle run as a Container Apps Job before a new revision gets traffic, never by hand (item 4, D39).
- [ ] Secrets resolved from Key Vault through a managed identity (item 5, D19).
- [ ] The app connects to Azure SQL with Entra / managed identity where possible, so no connection string holds a password (D40).
- [ ] `/health/live` and `/health/ready` are green as the Container Apps probes (item 6, D20).

**Running behind the Azure ingress**
- [ ] Forwarded-headers handling, so rate limiting sees the client address and `Request.Host` is the public host for the Origin check. The startup refresh counts against the refresh rate limit (30 per minute per client address): without this every visitor shares the ingress address (D48, D52).
- [ ] `Auth:AllowedOrigins` contains the deployed host if it differs from the request host (D48, D52).
- [ ] The Azure SQL connection string does not use `TrustServerCertificate=True` (local container only, D45).
- [ ] Probe timeouts for `/health/live` and `/health/ready` set explicitly: readiness takes about 4 s to answer when the database is down (D20, D45).
- [ ] Scale to zero (D17): a cold start must not exceed the 10 s limit of the startup silent refresh, or a signed-in user sees the login page (D52).
- [ ] The structured JSON logs reach Log Analytics and can be queried by field (D21).
- [ ] If a CSP is added, it needs a hash for the inline pre-paint script in `index.html` (D26).

**Secrets and data**
- [ ] Remove the `Seed__*` variables after the first successful deploy, and move the JWT signing key and the seed values to Key Vault (D48). This stops the seeder top-up (D55, D57): from then on new global permissions are granted through user management, whose API (D57) and screens (D59) exist.
- [ ] A change-password flow exists before any real data is stored (D48). Built: API (D58) and screens (D59). Tick after the owner's by-hand browser check of those screens (`docs/STATUS.md`).
- [ ] An EF retry strategy for Azure SQL transient faults, together with the transaction wrapper D31 requires (D46, D31).

**CI and repository**
- [ ] A deploy job that `needs` `test`, `web` and `image`; revisit `cancel-in-progress` so an in-flight deploy is never cancelled (D47).
- [ ] Dependabot for the pinned GitHub Actions and the NuGet and npm dependencies (D47).

**Decisions and account**
- [ ] Close O1: the region (West Europe or UAE North), after confirming Container Apps, ACR, Key Vault and Azure SQL are all available there.
- [ ] Close O2: a custom domain with TLS, or the default Container Apps hostname.
- [ ] Revisit D13 (Azure SQL Basic tier), including whether a free Azure SQL offer is worth using.
- [ ] Create the Azure account only when ready to deploy, and set a budget alert immediately.
