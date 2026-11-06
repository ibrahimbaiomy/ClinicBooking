# STATUS — ClinicBooking

Read at the start of every session; update at the end. Keep it short: what is
done in one line per step (the detail is in the decisions), what is next, and
what the owner must check by hand. Last updated: 2026-11-06 (Appointments back end, D66).

---

## Now

**Phase 2, next step: the Appointment screens** (a separate task; the full audit trail is
another). The back end is done (D66). Until the screens exist:
- The existing Doctors and Patients screens do not offer the new confirm-and-cancel flow:
  deactivating an assignment, deleting a doctor or deleting a patient with upcoming
  appointments shows the translated 409 (`error.doctor.has_future_appointments`,
  `error.patient.has_future_appointments`) and changes nothing. The screens task adds the
  confirmation (resend with `confirmCancelAppointments`).
- A slot change or a working-hours save refused by the new 422s
  (`effective_from_before_last_appointment`, `hours_strand_appointments`) shows the
  translated message; `appointmentCount` is not shown yet.
- Leave days have no screen yet (API only).

---

## Done

**Phase 0** (code done; deployment deferred, D54)
- Specialties back end (D50) and screens (D53).
- Login, JWT, refresh tokens, seeded admin (D48); front-end session (D52).
- `/health/live`, `/health/ready` (D20, D45).
- Bilingual UI with RTL (D26, D27, D51).
- Docker, compose, CI `test` / `web` / `image` jobs (D15, D47).

**Phase 1** (complete)
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
- Patients back end: name and phone, four global permissions, phone or name search,
  duplicate-phone warning, no personal data in logs (D63).
- Patients screens: list, create, edit with the duplicate-phone panel, delete (D63).

**Refinements before Phase 2** (D64)
- API sort fields for header sorting (clinic phone and address, doctor slot, patient phone, user
  status and password flag); the protected administrator (`IsProtected`, seeder, migration, 422s).
- Toasts for success messages (duration in `src/app/app-settings.ts`); one-row pager, 10 per page.
- Header sorting in every table; Doctors and Users filters behind a Filter button.
- "Show password" checkboxes; permissions save on each change with a "Saved" indicator; the
  protected administrator's screen.
- Doctors: direct row actions (Edit, Specialties, Clinics, Delete); the detail page is gone.
- Bootstrap Icons, self-hosted (only the six icons used ship); `cbAction` outline buttons with icons
  (add, edit, save, delete, danger, cancel) on every screen; green success toasts with an icon (D65).
  Initial bundle 404.64 → 412.16 kB raw (104.98 → 106.83 kB transferred).

**Phase 2** (in progress)
- Appointments back end (D66): book, day list, free slots, get by reference, reschedule,
  cancel, complete, no-show; five clinic-scoped `appointments.*` permissions; the double-booking
  index; the Cairo grid with daylight saving; the patient-overlap warning; leave days; every
  Phase 1 seam filled (deactivate, delete doctor, delete patient, slot change, working hours);
  `AddAppointments` migration.

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
- [ ] Header at 360 px wide with five links (Specialties, Clinics, Doctors, Patients,
      Users), Arabic and English: wraps with no horizontal overflow (D56, D62, D63).
- [ ] Patients against the real container (D63): the `AddPatients` migration applies;
      after a restart the seeded admin holds the four `patients.*` permissions (the
      top-up). Then: create a patient, search by name (try an Arabic variant), by
      "010…", "+20…", "0020…" and by digits from the middle; create a second patient
      with the same phone (the panel lists the first; Save anyway saves; Cancel and
      changing the phone hide it); edit without changing the phone (no panel); delete.
- [ ] A user with `patients.create` only: the 409 shows the count without names, and
      after saving the page stays on an empty form with the message.
- [ ] Patient pages at 360 px in Arabic: a Latin name and an Arabic name in the same
      list both read correctly (`dir="auto"`), phone digits left to right.
- [ ] Review the Arabic wording of `public/i18n/patients/ar.json`, the
      `error.patient.*` texts and the four permission labels.
- [ ] Doctors pages at 360 px in Arabic: cards below md, the working-hours editor
      (time inputs left to right, add and remove buttons wrap).
- [ ] Review the Arabic wording of `public/i18n/doctors/ar.json` and the new
      `error.doctor.*` texts.
- [ ] `docker compose build` still succeeds (the Docker `web` stage runs
      `check:i18n` without the C# sources and only warns).
- [ ] D64 against the real container (`docker compose up --build`): the `AddProtectedUser`
      migration applies; in SQL, `SELECT Id, UserName, IsProtected FROM AspNetUsers` shows exactly
      the lowest id flagged. On that user's page (as another administrator): the protected note, no
      Disable, no Reset password, `users.manage` locked.
- [ ] Toasts: appear at the bottom on the left in Arabic and on the right in English, disappear
      after 5 s, stay while hovered or while Close has the focus; a screen reader announces them
      once and the focus never moves. Errors still show in place.
- [ ] Doctors and Users at 360 px, Arabic and English: the Filter panel stays on screen and closes
      with Esc and an outside click; the pager stays usable (it may wrap); Doctors row actions
      wrap; the header sort buttons and their arrows read correctly in RTL.
- [ ] User permissions: tick several boxes quickly (one request at a time, each box disabled
      until saved, "Saved" next to it); remove your own `users.manage` (confirmation, then home).
- [ ] Doctors: Edit (names save and slot save separately), Specialties (conflict in two tabs),
      Clinics (read-only for a clinic you do not manage), Delete from the row; an old
      `/doctors/7` bookmark opens `/doctors/7/clinics`.
- [ ] Review the Arabic wording of the new keys: `toast.close`, `filter.*`, `pager.page_size`,
      `users.saved`, `users.detail.protected_note`, `users.global.remove_own.*`,
      `users.global.locked`, `users.table.password`, `users.sort.*`, `doctors.actions.*`,
      `doctors.edit.*`, `doctors.specialties_page.title`, `doctors.flash.specialties_updated`,
      the new sort labels and the three `error.user.protected_*` texts.
- [ ] Try the protected-password recovery procedure once on a local copy
      (`docs/guides/build-and-run.md`).
- [ ] D65 in the browser, Arabic and English, at 360 px and on a desktop: every Add, Edit, Save,
      Delete, Remove period and Cancel shows its icon and text; colours as D65 (green add and
      create-save, blue edit and edit-save, red delete, grey cancel), filled on hover, a visible
      focus ring with the keyboard, faded when disabled; row actions and dialog buttons still wrap.
      Check the contrast of text and border with the browser's accessibility tools (≥ 4.5:1).
- [ ] Dialogs: the delete confirm shows the trash icon; deactivate (pause-circle), disable
      (person-slash), reset password (key) and removing your own users.manage (shield-slash) are a red
      outline with their own icon. Icons confirmed rendering after `docker compose up --build`
      (2026-11-06); the colour, contrast and 360 px checks above are still open.
- [ ] Toasts: light green with the check icon, readable in both languages; icons sit before the
      text in RTL too (none of the current icons is mirrored).
- [ ] `docker compose build` still succeeds (the image builds the icons in through `npm ci`).
- [ ] D66 against the real container (`docker compose up --build`): the `AddAppointments` migration
      applies and the API starts. In SQL: `Appointments` has `UX_Appointments_Doctor_Start` filtered
      `[Status] <> 2` and `UX_Appointments_BookingReference`; `DoctorLeaveDays` has
      `UX_DoctorLeaveDays_Doctor_Date`; the three appointment foreign keys are NO ACTION.
- [ ] D66 through the API (no screens yet): on the Users page grant yourself the five
      `appointments.*` permissions in a clinic (the labels show in both languages); with a doctor
      working there, `GET /api/clinics/{clinicId}/doctors/{doctorId}/available-slots?date=<tomorrow>`
      lists slots in UTC; `POST /api/clinics/{clinicId}/appointments` books one (201, a 6-character
      reference); booking it again is 409 `slot_taken`; `GET /api/appointments/{reference}` and the
      day list return it; cancel it with its `rowVersion`.
- [ ] D66 in the existing screens: deactivating an assignment, deleting a doctor or a patient with an
      upcoming appointment shows the translated "has upcoming appointments" message and changes nothing.
- [ ] Review the Arabic wording of the new `error.appointment.*` keys, `error.doctor.has_future_appointments`,
      `effective_from_before_last_appointment`, `hours_strand_appointments`, the `error.doctor.leave_*`
      keys, `error.patient.has_future_appointments` and the five `permissions.appointments.*` labels.

---

## Known issues

- **A stalled local test run (2026-10-08).** One full `dotnet test` run took about 31
  minutes instead of about 3: one test (`PatientsAuthorizationTests`, a patient create)
  hung for about 28 minutes and then got a 500. It passed alone and on a full rerun.
  Cause unproven (the machine or the SQL Server container pausing is the guess). If it
  happens again, before rerunning: note the failing test and the `correlationId` of the
  500, which the failure message now shows with the response body (`AssertStatusAsync`,
  used by every test helper), then find the logged exception with that id in the test
  host's console output. In CI, the job timeout ends such a run (D47).
- **Fixed: random phone digits in `PatientsSearchTests` (2026-11-06).**
  `Digits_without_a_leading_0_or_plus_match_anywhere_in_the_number` failed once in a full run. Cause:
  it searched six random digits, which start with `0` one time in ten; a search starting with `0` is a
  national-number prefix search (`+20…`, D63), so the target was missed. The prefix theory next to it had
  a second chance failure: its six cases share one database and varied the "other" phone by two random
  digits, so two cases could create the same number (a 409 duplicate warning), about one run in six by
  arithmetic. Both were forced once to prove them and fail as described. The tests now use fixed or
  per-case distinct numbers. Unrelated to the stalled run above (a hang, not wrong data). The other
  search suites (Specialties, Clinics, Doctors, Users) scope every search with a GUID-based tag (8 to 32
  characters), where a chance match is negligible; unchanged.

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
- Patient name search with full-text (or a prefix index) once patients pass about
  100,000 or a search takes more than 200 ms; today it is a scan (D63).
- Forgot password (self-service recovery); when built, revisit the admin reset (D58, D64).
- Undo a final appointment status (Completed, Cancelled, NoShow are final in Phase 2, D66).
- Partial-day leave (leave is whole Cairo days today, D66).
- A patient's appointment history (D66 lists by clinic and day only).
