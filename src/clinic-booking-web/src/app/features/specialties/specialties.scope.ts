import { scopeResolver } from '../../shared/feature/scope-resolver';

export const SPECIALTIES_SCOPE = 'specialties';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const specialtiesScopeResolver = scopeResolver(SPECIALTIES_SCOPE);
