import { scopeResolver } from '../../shared/feature/scope-resolver';

export const PATIENTS_SCOPE = 'patients';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const patientsScopeResolver = scopeResolver(PATIENTS_SCOPE);
