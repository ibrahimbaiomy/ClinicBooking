import { scopeResolver } from '../../shared/feature/scope-resolver';

export const USERS_SCOPE = 'users';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const usersScopeResolver = scopeResolver(USERS_SCOPE);
