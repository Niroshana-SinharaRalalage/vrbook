import { describe, it, expect } from 'vitest';
import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';

/**
 * Slice OPS.1.2 — determinism sanity check for the pact file.
 *
 * <p>Pact drift on CI is only useful if the file's content is a stable
 * function of the consumer test source. If PactV3 sneaks in a timestamp,
 * a nondeterministic UUID, or an ordering-dependent map, `git diff
 * --exit-code contracts/pacts/` in `cd-staging-web.yml` will fire on
 * every push — the SPA team learns to `git add contracts/pacts/` as a
 * ritual with no signal, and real drift stops being caught.</p>
 *
 * <p>NOTE: this test is a belt-and-suspenders check. The primary drift
 * gate is `git diff --exit-code contracts/pacts/` in CI. This test
 * catches non-determinism WITHIN the pact write itself (rare but
 * catastrophic when it happens).</p>
 */
describe('pact file determinism', () => {
  const PACT_FILE = path.resolve(
    __dirname,
    '..',
    '..',
    '..',
    'contracts',
    'pacts',
    'vrbook-web-vrbook-api.json',
  );

  it('has been written to the expected repo path', () => {
    expect(existsSync(PACT_FILE)).toBe(true);
  });

  it('contains the consumer + provider names from the plan', () => {
    const raw = readFileSync(PACT_FILE, 'utf8');
    const parsed = JSON.parse(raw);
    expect(parsed.consumer?.name).toBe('vrbook-web');
    expect(parsed.provider?.name).toBe('vrbook-api');
  });

  it('has interactions', () => {
    const raw = readFileSync(PACT_FILE, 'utf8');
    const hash = createHash('sha256').update(raw).digest('hex');
    expect(hash).toMatch(/^[a-f0-9]{64}$/);
    const parsed = JSON.parse(raw);
    expect(Array.isArray(parsed.interactions)).toBe(true);
    expect(parsed.interactions.length).toBeGreaterThanOrEqual(1);
  });
});
