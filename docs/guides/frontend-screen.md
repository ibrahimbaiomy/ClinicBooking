# Guide — front-end screens

Build the API first (`backend-entity.md`). Copy the Specialties screens (D53);
Clinics (D56), Users (D59) and Doctors (D62) show the variations. Shared since
Doctors (D62): `shared/feature/feature-session.ts` (a feature's session is a
subclass), `shared/feature/scope-resolver.ts`, and `shared/list/list-params.ts`
(search, page, size, sort, direction); a feature adds its own filters. A page whose
access depends on the loaded record's clinics decides after loading (`canIn`); a
page that needs a clinic-scoped permission anywhere uses `clinicPermissionInAnyGuard`.

## Every new screen

1. **Route:** lazy, behind `authGuard`; add `permissionGuard(Permissions.X)` when
   it needs a permission. `*cbCan` hides controls (UX only). `authGuard` sends a
   user who must change the password to `/change-password` (D59): never write a
   route that bypasses it, and never add a second exemption like it.
2. **Data:** one method per endpoint in `src/api/`, types from `src/api/types.ts`
   (`RequestBody` / `ResponseBody` / `QueryParams`) over `schema.d.ts`; never a
   hand-written model. A new permission name goes into `core/auth/permissions.ts`
   (`Permissions` global, `ClinicPermissions` clinic-scoped) with its label in
   `users/ar.json` and `en.json` (`check:permissions`).
3. **Errors:** catch with `parseApiError`, show `ErrorMessageService.keyFor(error.key)`
   through the `transloco` pipe (comment `i18n-keys: error.unexpected` on that
   line); field errors from `error.fieldErrors`; never server text or a raw key.
4. **Forms:** Signal Forms (`@angular/forms/signals`), typed, messages as keys;
   real `<label for>`, `autocomplete` where it applies, `aria-invalid`, an
   `aria-live` error region, focus the first invalid field, disable and set
   `aria-busy` while submitting.
5. **Strings:** keys in a translation scope (`public/i18n/<scope>/{ar,en}.json`);
   the entity's `error.*` keys in the **root** files; Arabic wording reviewed by
   the owner.
6. **Layout:** logical utilities only; mirror directional icons (`rtl:-scale-x-100`).
7. **Tests:** logic with Vitest; HTTP with `HttpTestingController` and
   `provideAuthTesting()` (`src/testing/auth-testing.ts`).
8. **Template gotcha:** never write `>` inside an attribute expression
   (`length > 0`): the `check:i18n` heuristic ends the tag there. Use `!== 0` or a
   computed.

## An entity screen (list + form + delete)

1. **API client** `src/api/<entity>-api.ts`: list (with its query), get, create,
   update (with `rowVersion`), delete. Generated query names are PascalCase.
2. **Feature folder** `features/<entity>/`: `<entity>.routes.ts` (lazy, scope on
   the parent route, `permissionGuard` on create and edit), `<entity>.scope.ts`
   (scope + resolver that preloads the scope file), `list/` with `list-query.ts`,
   `form/`, and `<entity>-session.ts` (last list query + one-time status message).
3. **List:** the URL is the state (`q`, `page`, `size`, `sort`, `dir`; defaults in
   one constant and omitted from the URL; invalid values clamped). `rxResource`
   so a newer query cancels the older. Search: 300 ms debounce with `replaceUrl`,
   Enter at once, IME composition ignored, `maxlength` 100, any change returns to
   page 1. Loading, empty, no-results and error states; Retry calls `reload()`; a
   page past the end steps back. Table from md up (caption, `th scope`,
   `aria-sort`), cards below. Both names, each in a `<bdi>` with its own
   `lang`/`dir`, the UI language first; interpolate, never `innerHTML`.
4. **Form:** a separate page (not a modal). Signal Forms with the API's rules and
   keys; values sent trimmed. A 400's field errors and name-taken 409s go onto
   fields from `submit()`; anything else is a form-level message; 404 shows "not
   found". 409 `error.concurrency.conflict`: keep the input, disable Save until
   Reload, then show the user's earlier entries. After save, navigate to the
   remembered list query with a one-time `role="status"` message and focus the
   heading.
5. **Delete:** `confirm-dialog`, focus on Cancel, Esc ignored while pending; 404
   means already deleted; other errors stay in the dialog. Afterwards reload the
   list and focus the heading.
6. **Header:** a navigation entry. The header must still wrap with no horizontal
   overflow at 360 px in RTL and LTR (a by-hand check: jsdom has no layout).
7. **Optional fields (D56):** a blank value is sent as `null` (the `PUT` is a full
   replace); a missing value shows a "Not set" key, not a dash. A stored value
   shown in a friendlier form (a phone through `formatPhone` / the `phone` pipe,
   always in `<bdi dir="ltr">`) needs the **untouched guard**: keep the stored
   value and the text shown, and send the stored value back when the field still
   holds that text, otherwise exactly what was typed; test both directions. The
   client keeps only trivial guards (a length); the server's rules are the
   authority, and a key that names its field goes on that field.
8. **Tests:** API service, URL parsing, debounce, stale response, paging and
   sorting, delete (confirm, cancel, Esc, 404, last row of the last page), form
   errors and conflict, permission-aware rendering, a name containing HTML shown
   as text. Then `npm run lint`, `npm test`, `npm run check:i18n`,
   `npm run check:api`, `npm run build`.

## Special fields and lists (D59)

- **A password or other secret:** read once, empty the field **before** the
  answer comes back (success or failure), reset the show toggle; keep it out of
  the URL, router state, storage and any signal that outlives the form. Server
  messages for the field live in a small keys-only signal. The show/hide toggle
  is a button with a **fixed** label and `aria-pressed`. Tests search the DOM,
  URL, router state and storage for a recognisable value.
- **A dialog that asks for input:** pass `focusSelector` to `cb-confirm-dialog`
  so the field takes the focus; Cancel, Esc and any other close empty it.
- **A list of things to assign** (permissions) comes from the API, never a second
  hard-coded list; its wording is one translation object read with
  `translateObject`, and a script proves every name has wording. A picker over a
  paged list searches and shows the first page with a "refine" hint.

## Test notes (D53)

jsdom lacks `<dialog>.showModal()`/`close()`: use `src/testing/dialog-polyfill.ts`.
`whenStable()` hangs while an HTTP request is pending, so settle specs by hand.
rxjs `debounceTime` reads `Date.now()`, so fake timers must fake `Date` too.
