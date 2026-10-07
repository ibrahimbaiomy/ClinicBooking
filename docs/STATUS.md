# STATUS — ClinicBooking

Read at the start of every session; update at the end. Keep it short: what is
done in one line per step (the detail is in the decisions), what is next, and
what the owner must check by hand. Last updated: 2026-10-07 (Doctors screens).

---

## Now

**Phase 1, next step: Patients** (name and phone only, `PhoneNumber` reused,
duplicate-phone warning, global `patients.*`, D38, D44). Patient search needs a
prefix or full-text approach, not a leading wildcard (D49). Reuse the shared
pieces: back end `CommonRules`, `NamedListing`-style paging, `ConcurrencyGuard`
(D61); front end `FeatureSession`, `scopeResolver`, `list-params` (D62).

Phase 1 is complete after Patients. Appointments (Phase 2) do not
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
  duration, `in_use` for specialties and clinics (D61); working-hours 422s name the
  period and the other clinic (D61 refinement).
- Shared front-end code: session memory, scope resolver, list URL helpers (D62).
- Doctors screens: list with filters, create, edit, detail (clinics, slot change,
  delete), working-hours editor; `clinicPermissionInAnyGuard` (D62).

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
- [ ] Users and change-password flows, including the forced change (D59).
- [ ] Create user: the browser does not offer to save the temporary password; if
      it does, set `autocomplete="off"` on the field and report it (D59).
- [ ] Doctors through the screens, against the real container (D61, D62):
      ```
      docker compose up --build
      ```
      The `AddDoctors` migration applies and the API starts cleanly. Signed in as the
      seeded admin, grant yourself `doctors.manage` in two clinics on the Users page,
      then under Doctors: create a doctor in both (the clinics appear without signing
      out), filter the list by clinic and status, edit names and specialties, save
      working hours in one clinic, try an overlapping period in the other (the message
      sits next to that period and names the first clinic), deactivate and reactivate
      (a clash shows in the row), schedule a slot change (the date picker starts
      tomorrow), and confirm that deleting that clinic or the doctor's specialty is
      refused with the in-use message.
- [ ] A user holding `doctors.manage` in only one of a doctor's two clinics: can edit,
      cannot delete (no button; the API answers `all_clinics_required`), has no
      working-hours link for the other clinic, and that page opened by its URL is
      read-only.
- [ ] Header at 360 px wide with four links (Specialties, Clinics, Doctors, Users),
      Arabic and English: wraps with no horizontal overflow (D56, D62).
- [ ] Doctors pages at 360 px in Arabic: cards below md, the working-hours editor
      (time inputs left to right, add and remove buttons wrap).
- [ ] Review the Arabic wording of `public/i18n/doctors/ar.json` and the new
      `error.doctor.*` texts.
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
- A searchable picker for specialties and clinics in the Doctors filters and forms,
  if either list grows past 100 (D62; today a note says the list is incomplete).
