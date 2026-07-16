#!/usr/bin/env node
// Slice OPS.2.7 — E2E suite invariant guard (web side). Runs in CI on every web
// push (blocking). Enforces two drift-detectors from OPS_2_PLAYWRIGHT_PLAN §6/§7:
//   1. Exactly EXPECTED_SCENARIOS Playwright scenarios exist (test() + test.fixme(),
//      excluding describe/beforeEach/skip). Catches a silently added/removed spec.
//   2. web/tests/e2e/.auth/ is gitignored (never commit persona session state).
// The .NET arch companion (no [AllowAnonymous] on admin controllers, no test
// middleware in prod Program.cs) lives in VrBook.Architecture.Tests.
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

// Honest authored total (architect: count Playwright's total incl. test.fixme;
// do not pad to a round number). anon 13 + guest 16 + owner 14 + platform-admin 6.
// VRB-110 batched the deferred WEB-GUEST specs (+8 anon: home-featured 2,
// seo-surface 3, a11y-flows 3; +6 guest: mobile-nav 2, mobile-checkout 1 fixme,
// edit-profile 1, a11y-flows 2), taking 31 → 45.
// VRB-101 added owner/listing-photos (+4 owner: renders, upload, delete, reorder
// fixme), taking 45 → 49.
const EXPECTED_SCENARIOS = 49;

const webRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const e2eDir = join(webRoot, 'tests', 'e2e');

const specFiles = readdirSync(e2eDir, { recursive: true })
  .map((p) => String(p))
  .filter((p) => p.endsWith('.spec.ts'))
  .map((p) => join(e2eDir, p));

// A scenario = `test(` or `test.fixme(` at a call boundary. The negative
// lookbehind on [.\w] excludes `test.describe(`, `test.beforeEach(`,
// `test.skip(`, `test.slow(` etc., while `test.fixme(` is matched explicitly.
const scenarioRe = /(?<![.\w])test\(|(?<![.\w])test\.fixme\(/g;

let total = 0;
const perFile = [];
for (const f of specFiles) {
  const src = readFileSync(f, 'utf-8');
  const n = (src.match(scenarioRe) ?? []).length;
  total += n;
  perFile.push(`  ${n}\t${f.slice(webRoot.length + 1)}`);
}

const errors = [];
if (total !== EXPECTED_SCENARIOS) {
  errors.push(
    `E2E scenario count is ${total}, expected ${EXPECTED_SCENARIOS}. ` +
      `If this change is intentional, update EXPECTED_SCENARIOS in web/scripts/check-e2e-suite.mjs ` +
      `AND the OPS.2 close-out. Per-file counts:\n${perFile.sort().join('\n')}`,
  );
}

// .auth/ must be gitignored (root .gitignore). Never commit persona sessions.
const gitignore = readFileSync(join(webRoot, '..', '.gitignore'), 'utf-8');
if (!/web\/tests\/e2e\/\.auth\//.test(gitignore)) {
  errors.push('web/tests/e2e/.auth/ is NOT in the root .gitignore — persona session state could be committed.');
}

if (errors.length > 0) {
  console.error('E2E suite invariant check FAILED:\n- ' + errors.join('\n- '));
  process.exit(1);
}
console.log(`E2E suite invariants OK: ${total} scenarios across ${specFiles.length} spec files; .auth/ gitignored.`);
