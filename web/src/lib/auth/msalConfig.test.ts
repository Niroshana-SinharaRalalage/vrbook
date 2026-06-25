import { describe, expect, it } from 'vitest';
import { apiScopes } from './msalConfig';

/**
 * Regression test for the load-bearing OPS.M.0 fix (see `docs/OPS_M_0_PLAN.md`
 * §2.4 + §1's "three real bugs" list).
 *
 * Pre-fix this was `[`${clientId}/.default`]` where clientId was the SPA's
 * app id, which minted tokens whose `aud` was the SPA itself — every
 * authenticated /api/* call returned 401 with audience mismatch.
 *
 * The correct value targets the API app registration's exposed scope
 * (`api://vrbook/access_as_user` per `docs/identity/setup.md` §3). If anyone
 * reverts this back to a `${clientId}/.default` pattern (or any pattern
 * containing `.default`), this test fails and CI blocks the merge.
 */
describe('msalConfig.apiScopes', () => {
  it('requests the API exposed scope, not the SPA client id', () => {
    expect(apiScopes).toEqual(['api://vrbook/access_as_user']);
  });

  it('does not use the ${clientId}/.default pattern', () => {
    for (const scope of apiScopes) {
      expect(scope.endsWith('/.default')).toBe(false);
    }
  });
});
