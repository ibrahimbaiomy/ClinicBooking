import { scopeResolver } from '../../shared/feature/scope-resolver';

export const DOCTORS_SCOPE = 'doctors';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const doctorsScopeResolver = scopeResolver(DOCTORS_SCOPE);
