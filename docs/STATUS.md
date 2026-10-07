# STATUS — ClinicBooking

Read at the start of every session; update at the end. Keep it short: what is
done in one line per step (the detail is in the decisions), what is next, and
what the owner must check by hand. Last updated: 2026-10-06.

---

## Now

**Phase 1, next step: Doctors** (back end first, then screens).

Things already in place or promised for Doctors:
- `doctors.manage` is the first clinic-scoped permission (D57). The seeded admin
  holds it in no clinic; it grants it to itself through the user API.
- Endpoints addressed by the doctor's own id need an `IClinicResolver` or
  `IClinicAccess.RequireAsync` (D57, `docs/guides/clinic-permissions.md`).
- Reading doctors stays open to any signed-in user; there is no `doctors.read` (D57).
- `canIn`, `canInAny` and `*cbCan` with a clinic are built and tested; no
  clinic-aware route guard exists yet (D59).
- Comes with Doctors: 409 `error.specialty.in_use` (D50) and
  `error.clinic.in_use` (D55).
- Doctors is the **third entity**: decide what to extract from the copied
  Specialties/Clinics code (list state, session service, scope resolver, form
  error mapping) by looking at what the three copies share (D56). The clinic
  name rules (`ClinicRules`) likewise get a shared helper at the third copy (D55).
- Model and rules: D32 (multi-clinic, working hours), D43 (slots), D35 (delete
  with upcoming appointments).

**Then: Patients** (name and phone only, `PhoneNumber` reused, duplicate-phone
warning, global `patients.*`, D38, D44). Patient search needs a prefix or
full-text approach, not a leading wildcard (D49).

Phase 1 is complete after Doctors and Patients. Appointments (Phase 2) do not
start before that (D54).

---

## Done

**Phase 0** (code done; deployment deferred, D54)
- Specialties back end (D50) and screens (D53).
- Login, JWT, refresh tokens, seeded admin (D48); front-end session (D52).
- `/health/live`, `/health/ready` (D20, D45).
- Bilingual UI with RTL (D26, D27, D51).
- Docker, compose, CI `test` / `web` / `image` jobs (D15, D47).

**Phase 1** (in progress)
- Clinics back end (D55) and screens (D56).
- User management and clinic-scoped permissions, back end (D57).
- Account state, change-password, admin reset, back end (D58).
- User-management and change-password screens (D59).
- Auditing fields on every entity (D36, D46).

---

## By-hand checks for the owner

None of the signed-in flows have been driven in a browser (they need the seed
password). Covered by unit tests; still to check by hand:

- [ ] Login: success shows the shell, reload keeps the session, logout returns to
      login, returnUrl after login (D52).
- [ ] Specialties: list, search, paging, sort, create, edit, conflict, delete,
      focus restored after the dialog closes (D53).
- [ ] Clinics: the same, plus clearing phone and address, phone digits staying
      left to right in Arabic, a long Arabic address at phone width (D56).
- [ ] Header at 360 px wide, signed in, Arabic and English: wraps with no
      horizontal overflow (D56).
- [ ] Users and change-password flows, including the forced change (D59).
- [ ] Create user: the browser does not offer to save the temporary password; if
      it does, set `autocomplete="off"` on the field and report it (D59).

---

## Scope (Phases 1 to 5, worked in order)

- **Phase 0, walking skeleton:** Specialties, login and refresh, health,
  bilingual UI, Docker and CI. Done; Azure deferred (D54).
- **Phase 1, core CRUD and access control:** Clinics, Doctors (clinic
  assignments, working hours, slot duration), Patients (name and phone; create,
  list, search, edit, soft delete), user management, clinic-scoped permissions,
  auditing fields.
- **Phase 2:** Appointments (book, reschedule, cancel, list by doctor/day),
  concurrency protection (D31), full audit trail.
- **Phase 3:** SMS or email notifications, reminders, patient self-service portal.
- **Phase 4:** payments, medical records or prescriptions, reporting.
- **Phase 5:** admin dashboard, caching, background jobs.

The owner declares when the build is complete; then the deployment checklist in
`docs/release.md` is worked. The deadline is real and scope is the only variable
under control: ideas that arrive mid-build go under "Later", not into code.

## Later

- Short hotline phone numbers (16xxx, 19xxx) are not accepted by `PhoneNumber` (D55).
- Search clinics by address or phone (D55).
- Filter the Clinics list by the caller's clinics, if D6's "inaccessible means
  404" is wanted for lists (D55, D57).
