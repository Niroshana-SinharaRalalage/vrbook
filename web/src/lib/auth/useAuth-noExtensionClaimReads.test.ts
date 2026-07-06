import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

/**
 * Slice OPS.M.15.6 — arch guardrail: the SPA MUST NOT read the pre-ADR-0014
 * `extension_isOwner` / `extension_isAdmin` id-token claims. The backend
 * retired the emitters in M.14 (Entra CIAM tokens don't emit them anyway)
 * and dropped the readers in M.15.2/M.15.5. Nav derivation now reads
 * `/api/v1/me`'s `isOwner`/`isAdmin` DTO fields (kept for one cycle per
 * M.15 §7-Q1) via useMe / useMyTenants, NOT the id-token claims.
 *
 * Complements the C#-side arch test
 * `OpsM15_NoLegacyExtensionClaimSymbolsTests` — both sides must stay
 * clean or the legacy pattern silently regrows across the stack.
 */
const stripComments = (src: string): string =>
  src
    .replace(/\/\/[^\n]*/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '');

describe('SPA has no extension_* claim reads', () => {
  it('useAuth.ts code (comments stripped) does not reference extension_isOwner', () => {
    const src = stripComments(readFileSync('src/lib/auth/useAuth.ts', 'utf8'));
    expect(src).not.toContain('extension_isOwner');
  });

  it('useAuth.ts code (comments stripped) does not reference extension_isAdmin', () => {
    const src = stripComments(readFileSync('src/lib/auth/useAuth.ts', 'utf8'));
    expect(src).not.toContain('extension_isAdmin');
  });
});
