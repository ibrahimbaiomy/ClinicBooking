import { scopeResolver } from '../../shared/feature/scope-resolver';

export const CLINICS_SCOPE = 'clinics';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const clinicsScopeResolver = scopeResolver(CLINICS_SCOPE);
