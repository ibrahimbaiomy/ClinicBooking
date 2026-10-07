import { scopeResolver } from '../../shared/feature/scope-resolver';

export const ACCOUNT_SCOPE = 'account';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const accountScopeResolver = scopeResolver(ACCOUNT_SCOPE);
