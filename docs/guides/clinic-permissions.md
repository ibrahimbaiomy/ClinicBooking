# Guide — clinic-scoped permissions (D34, D57)

A permission is either **global** (`Permissions.Global`: held once for the whole
system) or **clinic-scoped** (`Permissions.ClinicScoped`: granted per clinic in
`UserClinicPermissions`). A global permission never satisfies a clinic-scoped
one, and the reverse. The seeder never grants clinic-scoped permissions.

## Adding one

1. A constant in a nested class of `Permissions` (for example
   `Doctors.Manage = "doctors.manage"`) and an entry in
   `Permissions.ClinicScoped`. `All` and the policies follow.
2. When a screen uses it, add it to `ClinicPermissions` in
   `src/clinic-booking-web/src/app/core/auth/permissions.ts`; `check:permissions`
   compares it with the C#.
3. **Add its label and description** to `public/i18n/users/ar.json` and
   `en.json` (`permissions.<area>.<action>`); `check:permissions` fails without
   them (D59).
4. Nothing else is needed for assignment: `GET /api/permissions` and
   `PUT /api/users/{id}/clinics/{clinicId}/permissions` read the same list.

## Protecting an endpoint

- **The clinic is in the route** (`/api/clinics/{clinicId}/doctors`):
  `[Authorize(Policy = Permissions.Doctors.Manage)]`. The route value must be
  named `clinicId` (`RouteClinicResolver`); otherwise register an
  `IClinicResolver`. A missing clinic, a deleted clinic, or a grant held only in
  another clinic is 403.
- **The resource is addressed by its own id** (`/api/doctors/{id}`): put
  `[Authorize]` on the action, load the resource in the service, then call
  `IClinicAccess.RequireAsync(clinicIds, permission, notFoundKey)`. No
  clinic-scoped permission in any of its clinics: **404** with the entity's own
  not-found key (never 403, so existence is not leaked). Some other clinic-scoped
  permission there: 403.
- **Lists and creation:** `IPermissionChecker.GetClinicIdsWithPermissionAsync`
  gives the clinics to filter by or to validate the target clinic against.

| Case | Status |
|---|---|
| No or invalid token | 401 `error.auth.unauthorized` |
| Global permission missing | 403 `error.auth.forbidden` |
| Clinic in the route; permission missing there | 403 |
| Resource by own id; no clinic-scoped permission in any of its clinics | 404, entity's not-found key |
| Same, but another clinic-scoped permission held there | 403 |
| The resource's clinic is soft-deleted | 404 (nothing is held there) |

A global administrator is not a clinic member. A disabled user fails every check.

**Tests per endpoint:** the right clinic, another clinic, a deleted clinic, no
grant, and the 404/403 rule. See `ClinicScopedAuthorizationTests` and the
test-only `ClinicScopedController`.

## Front end

`SessionService.can()` is global-only; `canIn(permission, clinicId)` and
`canInAny(permission)` answer clinic-scoped ones; `*cbCan="'doctors.manage';
clinic: id"`. All of this is UX only: the API decides. Ids may arrive as
`number | string` (int64), so compare through `Number()` (D59).

## Account state (D58)

Every authenticated request passes `AccountStateMiddleware`: a disabled user gets
401; a user with a temporary password gets 403
`error.auth.password_change_required` everywhere except `GET /api/auth/me` and
`POST /api/auth/change-password`. Never add `[AllowWhilePasswordChangeRequired]`
anywhere else. A password (temporary, new or current) is never returned, logged
or put in an error or exception message.
