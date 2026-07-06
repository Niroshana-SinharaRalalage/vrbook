import { describe, expect, it } from 'vitest';
import { isSocialIdp } from './identityProvider';

/**
 * Slice OPS.M.12.7 — the SPA classifier MUST agree with the backend
 * `HttpCurrentUser.SocialIdpValues` set (see
 * `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs`).
 * If these drift, an admin using a social IdP would slip past the SPA guard
 * and hit the API middleware 403 with no useful error page.
 */
describe('isSocialIdp', () => {
  const socialCases: Array<[string, string]> = [
    ['google.com', 'Google via CIAM'],
    ['live.com', 'Microsoft consumer via CIAM'],
    ['facebook.com', 'Meta via CIAM'],
    ['apple.com', 'Apple Sign-In via CIAM'],
    ['linkedin.com', 'LinkedIn (deferred but classified)'],
    ['twitter.com', 'X/Twitter (deferred but classified)'],
    ['amazon.com', 'Amazon (deferred but classified)'],
    ['GOOGLE.COM', 'case-insensitive'],
  ];

  for (const [claim, why] of socialCases) {
    it(`treats "${claim}" as social (${why})`, () => {
      expect(isSocialIdp(claim)).toBe(true);
    });
  }

  const nonSocialCases: Array<[string | null | undefined, string]> = [
    [null, 'null claim → entra-local'],
    [undefined, 'undefined claim → entra-local'],
    ['', 'empty string → entra-local'],
    ['vrbookcid.ciamlogin.com', 'tenant issuer host → entra-local (classifier handles this)'],
    ['contoso.example', 'unknown host → not classified as social (fail-safe: not admin-block)'],
  ];

  for (const [claim, why] of nonSocialCases) {
    it(`treats ${JSON.stringify(claim)} as not social (${why})`, () => {
      expect(isSocialIdp(claim)).toBe(false);
    });
  }

  it('accepts canonical provider keys (mirrors HttpCurrentUser.SocialProviderKeys)', () => {
    expect(isSocialIdp('google')).toBe(true);
    expect(isSocialIdp('microsoft')).toBe(true);
    expect(isSocialIdp('facebook')).toBe(true);
    expect(isSocialIdp('apple')).toBe(true);
  });
});
