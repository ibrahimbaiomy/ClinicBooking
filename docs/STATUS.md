# STATUS — ClinicBooking

Read at the start of every session; update at the end. Keep it short: what is
done in one line per step (the detail is in the decisions), what is next, and
what the owner must check by hand. Last updated: 2026-10-07.

---

## Now

**Phase 1, next step: Doctors screens** (the back end is done, D61).

Things in place or promised for the screens:
- API: `/api/doctors` list (search, paging, sort, `ClinicId`, `SpecialtyId`,
  `IsActive` with `ClinicId` only), detail, create, edit, delete; assignments
  (add, activate, deactivate); working hours per clinic (`GET` open, `PUT` with the
  assignment's `rowVersion`); slot-duration change (D61). Types are in `schema.d.ts`.
- Permissions: create needs `doctors.manage` in every chosen clinic; edit and slot
  change in any of the doctor's clinics; delete in all of them (403
  `error.doctor.all_clinics_required` otherwise); assignment and hours changes in
  that clinic. `canIn`, `canInAny` and `*cbCan` with a clinic exist; no
  clinic-aware route guard yet (D59).
- `dayOfWeek` is 0 = Sunday in the API; the screens order the week from Saturday.
- Every back-end key is translated in the root `ar.json`/`en.json`.
- Doctors is the **third entity** for the front end too: decide what to extract
  from the copied Specialties/Clinics screen code (list state, session service,
  scope resolver, form error mapping) by looking at what the three copies share
  (D56). The back-end extraction is done (D61, extent to be confirmed by the owner).

**Then: Patients** (name and phone only, `PhoneNumber` reused, duplicate-phone
warning, global `patients.*`, D38, D44). Patient search needs a prefix or
full-text approach, not a leading wildcard (D49).

Phase 1 is complete after Doctors and Patients. Appointments (Phase 2) do not
start before that (D54).

**Phase 2 seams waiting for Appointments** (`IDoctorScheduleGuard`, D61; Phase 1
registers `NoAppointmentsScheduleGuard`, which allows everything):
- Leave days: not modelled yet; they come with Appointments.
- Slot-duration change only after the doctor's last active appointment (D43).
- Deleting a doctor with upcoming appointments: confirmation, cancel them in the
  same transaction (D35); deactivating an assignment with future appointments there.
- A working-hours change that leaves a future appointment outside its period or
  off the grid is refused (D43).

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
- Shared back-end code for named entities: name rules, list query, search and
  paging, concurrency guard (D61).
- Doctors back end: doctors, specialties, clinic assignments, working hours, slot
  duration, `in_use` for specialties and clinics (D61).

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
- [ ] Doctors back end (D61) against the real container: the `AddDoctors`
      migration applies at start-up and the API starts cleanly.
      ```
      docker compose up --build
      ```
      Then, signed in as the seeded admin (see `docs/guides/build-and-run.md`):
      grant yourself `doctors.manage` in a clinic
      (`PUT /api/users/{id}/clinics/{clinicId}/permissions`), create a doctor
      (`POST /api/doctors`), save working hours, deactivate and reactivate the
      assignment, and confirm that deleting that clinic or the doctor's specialty
      answers 409 `in_use`.
- [ ] `docker compose build` still succeeds (the Docker `web` stage runs
      `check:i18n` without the C# sources and only warns).

---

## Scope (Phases 1 to 5, worked in order)

- **Phase 0, walking skeleton:** Specialties, login and refresh, health,
  bilingual UI, Docker and CI. Done; Azure deferred (D54).
- **Phase 1, core CRUD and access control:** Clinics, Doctors (clinic
  assignments, working hours, slot duration), Patients (name and phone; create,
  list, search, edit, soft delete), user management, clinic-scoped permissions,
  auditing fields.
- **Phase 2:** Appointments (book, reschedule, cancel, list by doctor/day),
  concurrency protection (D31), full audit trail, doctor leave days.
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
- Cancel a pending slot-duration change without replacing it (D61).
