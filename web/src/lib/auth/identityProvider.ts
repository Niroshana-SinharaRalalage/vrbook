/**
 * Slice OPS.M.12.7 — mirror of the backend
 * `HttpCurrentUser.SocialIdpValues` set. Kept literally in sync with
 * `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs`
 * so the SPA can detect the admin-vs-social-IdP mismatch BEFORE the API
 * middleware rejects.
 *
 * If the backend ever widens this set (e.g. adds LinkedIn as a supported guest
 * IdP), update this list too — the arch tests in
 * `tests/VrBook.Architecture.Tests/OpsM12_SocialIdpShapeTests.cs` (M.12.8) check
 * that both stay in sync.
 */

const SOCIAL_IDP_RAW_VALUES = new Set([
  'google.com',
  'live.com',
  'facebook.com',
  'apple.com',
  'linkedin.com',
  'twitter.com',
  'amazon.com',
]);

const SOCIAL_PROVIDER_KEYS = new Set([
  'google',
  'microsoft',
  'facebook',
  'apple',
]);

/**
 * Return true when the given idp claim (as it appears on the id token) signals
 * a social identity provider. A null / empty / entra-local value returns
 * false — the caller is responsible for treating that as `entra`.
 *
 * @param idpClaim raw value from `account.idTokenClaims.idp` (may also be a
 *   host like `vrbookcid.ciamlogin.com` for the Entra-local case, which
 *   returns false here — that's the tenant issuer host, not a social IdP).
 */
export const isSocialIdp = (idpClaim: string | null | undefined): boolean => {
  if (!idpClaim) return false;
  const lower = idpClaim.toLowerCase();
  if (SOCIAL_IDP_RAW_VALUES.has(lower)) return true;
  if (SOCIAL_PROVIDER_KEYS.has(lower)) return true;
  return false;
};
