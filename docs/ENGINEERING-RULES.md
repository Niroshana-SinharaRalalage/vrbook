# VrBook — Engineering Rules (what good work looks like)

Non-negotiable engineering standards for every story, every lane, every agent. [`AGENT-PLAYBOOK.md`](AGENT-PLAYBOOK.md) governs *how* you pick up and hand off work; this file governs *what the code and tests must be*. The **Definition of Ready** and **Definition of Done** at the bottom apply to **every** story — a story's own DoD is *in addition to* these, never a replacement.

---

## 1. Codebase adherence — build with what exists, don't reinvent

VrBook is a mature codebase. Most of what a story needs already has a home. **Before you write a new class, endpoint, component, or config key:**

- **Search first.** Grep for the concept (interface, handler, component, token) before creating one. Reuse the existing abstraction; don't add a parallel one. Examples that get re-invented and shouldn't be: `ITaxCalculator`, `IBlobStorage`/`PropertyImageUrlBuilder`, `TwoTenantApiFixture`, `ConfirmActionModal`, the brand tokens in `tailwind.config.ts`, `ICurrentUser.HasTenantRole`.
- **Match the layering.** Backend is modular-monolith + MediatR CQRS: a feature is `Command`/`Query` → `Handler` → domain aggregate → EF config, inside its module. Don't put logic in controllers; don't cross module boundaries except via `VrBook.Contracts`. RLS is enforced by `TenantGucCommandInterceptor` — respect it, don't bypass it.
- **Match the idiom.** New code reads like the code around it: same naming, same folder shape, same test style, same comment density. A reviewer should not be able to tell which agent wrote it.
- **Shared UI lives in `web/src/components/ui/*`** (Lane DESIGN). Don't copy-paste inline Tailwind into a page when a primitive exists or should (gap G20). Rule of three: the third time you need a thing, extract it to the shared location — don't fork a fourth copy.
- **One source of truth per fact.** Config keys live in `ops/CONFIG-MATRIX.md`; gaps in `ops/CURRENT-GAPS.md`; story state on `stories/BOARD.md`; decisions in `docs/adr/`. If you learn a fact, update the doc that owns it — don't duplicate it into a second place that will drift.

## 2. TDD (the loop, in order)

1. Write the failing test the story's TDD plan names. **Run it. Watch it fail** for the right reason.
2. Write the minimal code to make it pass.
3. Refactor with the test green.

Unit tests need no Docker. Integration tests use Testcontainers Postgres (`Category=Integration`). E2E is Playwright against staging. Use `superpowers:test-driven-development`. **No production code without a test that drove it.**

## 3. Endpoint API tests (ships with the endpoint, every time)

Any story that adds or changes an HTTP endpoint MUST, as part of that story:
1. **Add a `RouteMatrix` row** (`tests/VrBook.Api.IntegrationTests/Multitenancy/RouteMatrix.cs`) for its auth + cross-tenant-isolation shape — or mark the action `[ExemptFromCrossTenantMatrix("reason")]`. The strengthened `EndpointCoverageArchTest` (VRB-300) **fails the build** if you skip this.
2. **Add per-module contract tests** (`Contract/<Module>/*`) for the dimensions the matrix doesn't assert: **happy path · input validation (→400) · error contract (status + problem `type`) · idempotency** where it mutates.

Build on the existing `TwoTenantApiFixture` + `TwoTenantTestAuthHandler` — **do not stand up a new harness** (it already exists; see [`TEST-STRATEGY.md`](TEST-STRATEGY.md)). The full suite must be green before merge (playbook §5).

## 4. Security & owner-locked policies (invariant — never re-derive, never re-ask)

- **Admin vs guest IdP split:** Platform Admin + Tenant Admin → Entra-local email+password **only**, never any social IdP. Guest → email or any social IdP. Enforced by ADR-0016 + two-layer defence. Never merge the flows; never add a social button to an admin surface.
- **Admins are operator-pre-seeded** before first sign-in (ADR-0017 / VRB-… seed path). Guests self-serve.
- **No `IsOwner` / `IsAdmin` / `[Authorize(Roles="Owner,Admin")]` literals anywhere.** Global authority = `IsPlatformAdmin`; tenant-scoped writes = `HasTenantRole(tid, "tenant_admin")`. Arch tests fail loud on regression.
- **Never commit secrets** — even `pending-identity-setup` placeholders live only in Key Vault. Any new Bicep-referenced secret must be seeded to KV *before* the deploy or `main.bicep` fails atomically.
- Full policy text + rationale: [`CLAUDE.md`](../CLAUDE.md) "Owner-locked policies".

## 5. CI parity (the local checks are not optional)

CI's Docker analyzer is stricter than a local `dotnet build`. Run the playbook §8 pre-push checks every time. After push, `gh run watch <id> --exit-status` to green — a story is not `DONE` on a red or un-watched run.

---

## Definition of Ready (before you write the first test)

- [ ] Story is `CLAIMED` by me on [`stories/BOARD.md`](stories/BOARD.md) with my branch, pushed.
- [ ] I've read the story in full + every `blocked-by`/`related-to` story; all `blocked-by` are `DONE`.
- [ ] I've read [`architecture/CURRENT-STATE.md`](architecture/CURRENT-STATE.md) for the area I'm touching, and **grepped for an existing implementation** of what I'm about to build.
- [ ] The files I'll touch are all owned by my lane; no out-of-lane edits are required (or the dependency/contract is resolved).
- [ ] I know the TDD plan's named tests and the config keys/contracts this story consumes.

## Definition of Done (before you mark the story `DONE`)

- [ ] **Did I search for an existing implementation before creating a new one?** (reuse over reinvention — §1)
- [ ] Every test the story's TDD plan named is written and green (unit + integration + any E2E).
- [ ] **Every endpoint I added/changed has API contract tests, and the full API suite (VRB-300) is green.** (§3)
- [ ] All acceptance criteria met; UI stories used `frontend-design` and meet WCAG 2.2 AA.
- [ ] Owner-locked policies honoured (§4); no forbidden role literals; no committed secrets.
- [ ] Pre-push CI-parity checks pass locally (playbook §8); pushed; `gh run watch` is green.
- [ ] `superpowers:requesting-code-review` run and its findings addressed.
- [ ] **Self-healed the docs (playbook §6):** board row `DONE` + rollup bumped; CONFIG-MATRIX/CURRENT-GAPS/epic updated for any fact I changed; ADR written for any decision.
- [ ] I left nothing that blocks the next cold agent — no dangling `CLAIMED`, no undocumented contract, no TODO-in-code standing in for a missing story.
