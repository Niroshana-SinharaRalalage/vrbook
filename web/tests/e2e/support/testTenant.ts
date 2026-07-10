/**
 * Slice OPS.2.2 — test-data isolation primitives.
 *
 * The suite runs against DEPLOYED staging (plan §5-Q1-a) under the isolated
 * `e2e-tenant` (seeded by VrBook.Migrator.SeedE2EBackfill). Because staging is
 * shared and nightly runs are not torn down (cleanup is the OPS.2 POLISH.6
 * janitor, out of scope here), every row a spec creates MUST be namespaced by a
 * per-run id so concurrent runs and operator manual testing never collide
 * (plan §4 risks #4/#10).
 *
 * `RUN_ID` is stable for the lifetime of one `playwright test` invocation; use
 * `scopedName()` to stamp it onto anything a spec creates (property titles,
 * booking guest names, message bodies).
 */

/** Slug of the isolated E2E tenant seeded on staging. Mirrors SeedE2EBackfill. */
export const E2E_TENANT_SLUG = 'e2e-tenant';

/**
 * Deterministic public property + its GUID, seeded by
 * VrBook.Migrator.SeedE2EBackfill (OPS.2.3). The anonymous detail-by-slug smoke
 * navigates to `/properties/${E2E_SMOKE_PROPERTY_SLUG}`; the anonymous quote
 * smoke POSTs to `/api/v1/properties/${E2E_SMOKE_PROPERTY_ID}/quotes`.
 * MUST stay in sync with the SmokePropertySlug / SmokePropertyId constants in
 * src/VrBook.Migrator/SeedE2EBackfill.cs.
 */
export const E2E_SMOKE_PROPERTY_SLUG = 'e2e-smoke-property';
export const E2E_SMOKE_PROPERTY_ID = 'e2e00000-0000-0000-0000-000000000001';

/**
 * A short, filesystem/URL-safe id unique to this process. Uses the CI run id
 * when present (stable across the setup + authed projects of one CI job), else
 * a timestamp+random suffix for local runs.
 */
export const uniqueRunId = (): string => {
  const ci = process.env.GITHUB_RUN_ID;
  if (ci) return `ci${ci}`;
  const rand = Math.random().toString(36).slice(2, 8);
  return `loc${Date.now().toString(36)}${rand}`;
};

/** Stable for the whole invocation — import this rather than re-deriving. */
export const RUN_ID = uniqueRunId();

/**
 * Namespace a human-readable label with the run id so it's traceable back to
 * the run that created it and never collides across runs.
 * e.g. `scopedName('Cozy Cabin')` → `"[e2e ci123] Cozy Cabin"`.
 */
export const scopedName = (label: string): string => `[e2e ${RUN_ID}] ${label}`;
