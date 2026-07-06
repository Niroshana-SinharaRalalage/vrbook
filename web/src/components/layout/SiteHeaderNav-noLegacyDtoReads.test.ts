import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

/**
 * Slice OPS.M.21 (M.15 App Roles follow-up A step 1) — pins the SPA
 * nav-operator derivation off the retired `UserDto.IsOwner`/`IsAdmin`
 * wire-contract fields. Pre-M.21 `SiteHeaderNav` read
 * `data?.isOwner || data?.isAdmin || data?.isPlatformAdmin`. Post-M.21
 * derivation is `useMe().isPlatformAdmin ||
 * useMyTenants().memberships.some(m => m.role === "tenant_admin")` —
 * the ADR-0014 authoritative shape.
 *
 * <p>A regressor that resurrects `data?.isOwner` or `data?.isAdmin`
 * fails this test before merge — critical because M.21.A.3 drops the
 * underlying DB columns; any consumer still keying on those DTO
 * fields would silently see them as undefined and mis-render the
 * Admin nav entry.</p>
 */
const stripComments = (src: string): string =>
  src
    .replace(/\/\/[^\n]*/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '');

describe('SiteHeaderNav has no legacy DTO field reads', () => {
  it('SiteHeaderNav.tsx code (comments stripped) does not read data?.isOwner', () => {
    const src = stripComments(readFileSync('src/components/layout/SiteHeaderNav.tsx', 'utf8'));
    // Match any of `data.isOwner`, `data?.isOwner`, `me?.isOwner`, `me.isOwner`.
    expect(src).not.toMatch(/\b(data|me)\??\.isOwner\b/);
  });

  it('SiteHeaderNav.tsx code (comments stripped) does not read data?.isAdmin', () => {
    const src = stripComments(readFileSync('src/components/layout/SiteHeaderNav.tsx', 'utf8'));
    expect(src).not.toMatch(/\b(data|me)\??\.isAdmin\b/);
  });

  it('SiteHeaderNav.tsx derivation calls useMyTenants (positive presence)', () => {
    const src = stripComments(readFileSync('src/components/layout/SiteHeaderNav.tsx', 'utf8'));
    expect(src).toContain('useMyTenants');
    expect(src).toMatch(/memberships\.some/);
    expect(src).toMatch(/['"]tenant_admin['"]/);
  });
});
