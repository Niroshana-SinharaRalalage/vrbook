# VrBook — Agent Kickoff Prompts (per lane)

Copy-paste one prompt to start a lane. Each is self-contained. Lanes + waves + file ownership are defined in [`EXECUTION-PLAN.md`](EXECUTION-PLAN.md); stories in [`../stories/INDEX.md`](../stories/INDEX.md).

**Every prompt assumes this shared preamble** (the agent should internalise it):
> You are implementing part of the VrBook backlog (repo root `c:\Work\BookingApp`, .NET 8 modular monolith + Next.js 14, work on the `develop` branch). Read first: `CLAUDE.md`, `docs/architecture/CURRENT-STATE.md`, `docs/stories/INDEX.md`, and each story you pick up in its epic file. **TDD is non-negotiable** (failing test → minimal impl → refactor). Use `superpowers:test-driven-development` per story and `superpowers:requesting-code-review` before merge; use `frontend-design` for any UI. Work on a git worktree/branch `lane/<name>` off `develop`; only edit files THIS LANE OWNS; a cross-lane need is a dependency (wait) or a contract (ship the interface first). Before merge: `dotnet format --verify-no-changes` + `dotnet publish -c Release` + `dotnet test --filter "Category!=Integration"` (backend) and `npm run lint && npm run typecheck && npm test && npm run check:e2e-suite` (web); then `git push` and `gh run watch <id> --exit-status` to green. Honour the owner-locked policies in `CLAUDE.md` (admin-vs-social IdP split; no `IsOwner`/`IsAdmin` literals; per-tenant `HasTenantRole`).

---

## WAVE 0

### Lane CONFIG
> Implement stories **VRB-200, VRB-201, VRB-203, VRB-202, VRB-205** (`docs/stories/EPIC-configuration-settings.md`). You OWN: config wiring in `src/VrBook.Api/Program.cs`, options classes across modules, `infra/scripts/10-store-secrets.ps1`, config-validation. Deliver fail-fast startup validation (`.ValidateOnStart()` — today a missing Entra config silently boots with no auth, gap G5), Key-Vault/managed-identity secrets with seed-parity (gap G6: `stripe-publishable-key` + `acs-sender-address` are referenced but unseeded), the feature-flag runtime + naming convention (gap G13: `StubFeatureToggle` is a no-op), and the CORS/callback/rate-limit config. This lane lands FIRST — everything downstream reads config. Cross-check every key against `docs/ops/CONFIG-MATRIX.md`.

### Lane DESIGN
> Build the shared UI component library + the accessible modal focus-trap (story **VRB-110** primitive) that the whole frontend needs. You OWN: `web/src/components/ui/*`, `web/tailwind.config.ts`, `web/src/app/globals.css`. Today only `ui/ConfirmActionModal` exists and pages copy-paste inline Tailwind (gap G20) — build Button, Card, Dialog (Radix, with focus-trap + Esc), Input, Field/Form, Badge, Skeleton using the existing brand tokens (brand-orange/brand-maroon, semantic tokens, dark mode). WCAG 2.2 AA. Use `frontend-design` for a distinctive, intentional system — not templated defaults. This lane lands FIRST for the UI lanes.

### Lane DEVOPS-prereq
> Implement stories **VRB-301, VRB-302, VRB-303, VRB-304, VRB-306** (`docs/stories/EPIC-go-live.md`). You OWN: `.github/workflows/*`, `infra/*`. Deliver the **production deploy pipeline** (`cd-prod.yml` — today only `cd-staging-*` exist, gap G23), **tested blue-green rollback** (the `revision_suffix`/`traffic_weight` inputs in `_deploy-container-app.yml` are unused, gap G24), the zero-downtime forward-only migration strategy, a **tested** backup/restore drill (RPO ≤1h PITR / RTO ≤4h, gap G25), and observability dashboards + alert rules with real thresholds + owners. These are early prerequisites, not launch-week work. Reuse `docs/OPS_LAUNCH_COMPLETION_PLAN.md` + existing runbooks.

## WAVE 1

### Lane PAY (sequenced internally)
> Implement, IN THIS ORDER, **VRB-105 → VRB-104 → VRB-103 → VRB-102 → VRB-113 → VRB-111 → VRB-112** (`docs/stories/EPIC-launch-features.md`). You OWN: `src/Modules/VrBook.Modules.Payment/*`, `src/Modules/VrBook.Modules.Pricing/*` (tax/fees), the cancellation-policy fields on `src/Modules/VrBook.Modules.Booking/Domain/Booking.cs`, and `StripeGateway.cs`. Load-bearing launch fixes first: **VRB-105** drop `OnBehalfOf=supplier` so the platform is MoR (gap G38); **VRB-104** actually call `ApplicationFeeRefundService` + persist `fee_reversal_cents` (today it's metadata-only, gap G37); **VRB-103** Stripe Tax via the ready `ITaxCalculator` contract (platform-facilitator, per-state — coordinate with SETTINGS VRB-216); **VRB-102** the two cancellation models (needs the policy config from SETTINGS VRB-215/216 — wait for that contract). This lane exclusively holds `StripeGateway.cs` for the wave.

### Lane CATALOG
> Implement **VRB-101** (`docs/stories/EPIC-launch-features.md`). You OWN: `src/Modules/VrBook.Modules.Catalog/*` image endpoints (the three 501 stubs at `PropertiesController.cs:72-88`), `web/src/app/admin/properties/*`, `web/src/components/property/*`, and blob wiring (`IBlobStorage`, `PropertyImageUrlBuilder`, the `stvrbook{env}` container already exist). Deliver photo upload/reorder/delete + a gallery-management UI (consume DESIGN primitives). A listing needs photos to sell — this is a launch Must.

### Lane WEB-GUEST
> Implement **VRB-106, VRB-107, VRB-108, VRB-109, VRB-110** (`docs/stories/EPIC-launch-features.md`). You OWN: `web/src/app/page.tsx`, `web/src/app/account/*`, `web/src/components/layout/*`, `web/src/app/sitemap.ts` + `robots.ts`. Deliver mobile navigation (today `SiteHeaderNav.tsx:47` is `hidden md:flex`, gap G19), real home featured properties (replace the placeholder at `page.tsx:23-45`, G17), the guest profile page (stub → `PUT /me`, G18), the SEO engine (sitemap/robots/canonical/per-property meta, G34 — coordinate with DEVOPS VRB-305 for the domain), and the WCAG 2.2 AA audit (G33, uses the DESIGN focus-trap). Consume DESIGN primitives.

### Lane SETTINGS
> Implement **VRB-210, VRB-211, VRB-212–220, VRB-206–209** (`docs/stories/EPIC-configuration-settings.md`). You OWN: `web/src/app/admin/settings/*`, `src/Modules/VrBook.Modules.Admin/*` (build out the no-op stub, gap G14), settings APIs. Deliver the admin settings UI shell (validation + safe defaults), the **who-changed-what audit trail**, and product settings: property, pricing/fees/tax/min-stay, availability/iCal, cancellation policy (2 models per-property, VRB-215) + the platform global tier/fee/tax table (VRB-216), payout config, notification templates, branding/SEO, roles/admin-users. Also the config-defect fixes: loyalty thresholds (G1/VRB-206), 48h SLA (G2/VRB-207), dead config (G3/G4/VRB-208), AdminFlowName (G7/VRB-209). **Ship the VRB-215/216 policy-config contract early** — PAY's VRB-102 depends on it. Consume CONFIG + DESIGN.

## WAVE 2

### Lane DEVOPS-launch
> Implement **VRB-305, VRB-307, VRB-308, VRB-309, VRB-312, VRB-313** (`docs/stories/EPIC-go-live.md`). You OWN: `.github/workflows/*`, `infra/*`, `docs/runbooks/*`. Custom domain/DNS/TLS, security hardening (WAF + the already-scaffolded Trivy/ZAP), prod-sized k6 (not against scale-to-zero staging), **payments go-live** (live Stripe keys + webhook + real-money E2E + replay — verifies PAY's VRB-104/105 in prod), the launch cutover runbook execution, and hypercare. Kick off the operator long-poles (DKIM DNS, Stripe live keys, Entra prod cutover) Day-0. Follow `docs/ops/GO-LIVE-RUNBOOK.md`.

### Lane COMPLIANCE
> Implement **VRB-311, VRB-310** (`docs/stories/EPIC-go-live.md`). You OWN: analytics/consent/legal surfaces + the UAT process. Deliver analytics + conversion tracking **live before the first real visitor** (gap G35), a cookie-consent banner (necessary/analytics categories, gap G32), GDPR/CCPA data-subject flows + retention, and the Terms/Privacy/Cancellation legal pages (VrBook drafts them; Ohio governing law). Then run owner UAT with sign-off criteria. Consume DESIGN for the consent/legal UI.

## WAVE 3 — Phase 3 (post-launch, sequenced)

### Lane P3-FOUNDATION
> Implement, STRICTLY IN ORDER, **VRB-400→401→402→403→404** (`docs/stories/EPIC-phase3-foundation-rooms.md`), to the **corrected** design (`docs/architecture/PHASE-3-4-DESIGN.md` §0.5 supersedes the prose). You EXCLUSIVELY HOLD the tenant/RLS core for this wave: `TenantGucCommandInterceptor.cs`, `TenantAuthorizationBehavior.cs`, `PlaceBookingHandler.cs`, `Booking.cs`. Deliver the `ordering` schema + guest-scoped `Order`, the `app.user_id` GUC with **fail-safe-deny for anonymous** (C11), the `Booking→Reservation` split (state machine verbatim) + `ReservableKind`/`ReservableId`, the **48h** SLA (C10), and the forward-only backfill with dual-event emission and a **zero-guest-facing-change** gate (run the 6-flow staging walk). No other lane runs this wave.

### Lane P3-ROOMS (after P3-FOUNDATION)
> Implement **VRB-405→406→{407,408,409}→{410,411}→412** (`docs/stories/EPIC-phase3-foundation-rooms.md`). VRB-406 is the `properties→facilities` **multi-schema rename wave** — ONE atomic PR across catalog/pricing/reviews/sync/messaging with view shims; hold those schemas exclusively while it lands. Then RoomType + the **RatePlan dimension** (C7), count-availability with the **`booking.room_inventory` `FOR UPDATE` counter-row lock** (C8), per-room-type iCal (reviews stay facility-level), and the room UIs. You OWN Catalog + the room web surfaces for this wave.

### Lane P3-CART (after P3-ROOMS)
> Implement **VRB-420…431** (`docs/stories/EPIC-phase3-cart.md`), to the corrected design. Sub-lane (sequenced, exclusive on `StripeGateway.cs`/`PlaceBookingHandler.cs`/`TenantGucCommandInterceptor.cs`): VRB-422 (per-tenant flush within ONE serializable txn — C3), 423 (PI re-scoped to platform — C2), 424 (separate charges & transfers), 425 (resolve→single partial capture→per-supplier transfer — C1), 426 (per-item cancel + reversal, reuses VRB-104). Fan-out: 420 assembly, 421 anonymous-cart, 427 FX decision, 428 mixed-policy display, 429 per-state tax, 430 UI, 431 observability.

## WAVE 4 — Phase 4 (after P3-CART)

### Lane P4-OTA
> Implement **VRB-500…512** (`docs/stories/EPIC-phase4-ota.md`). Reuse the Phase-3 engine — the Itinerary is an `Order` overlay, legs are `Reservation`s, settlement rides the corrected capture pipeline. Deliver the `Itinerary` aggregate, leg polymorphism (`Flight/Car/Activity` + `IReservableResolver`), the `identity.tenant_supplier_relationships` table (a relationship, NOT an enum — swap the `ITenantStripeContextLookup` impl without changing the contract; reconcile the `tenant_connect_accounts` doc-comment naming to `tenant_supplier_relationships`), the agency-margin N+1 transfer split, FX, per-leg cancellation, the agency package-builder UI, and the guest itinerary checkout. You OWN a new `travel`/OTA module + its web surfaces.
