# EPIC — Launch Features (VRB-1xx)

> **Every story here inherits the global [Definition of Ready + Definition of Done](../ENGINEERING-RULES.md#definition-of-ready-before-you-write-the-first-test).** Before code: **claim the story on [`BOARD.md`](BOARD.md)** (first-push-wins), read it + its `blocked-by`, and **grep for an existing implementation before building one**. TDD; **write API contract tests for every endpoint you touch and keep the VRB-300 suite green**; stay in your lane ([`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md)); on finish **self-heal the board + docs**. Operating model: [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md). Each story's own DoD is *in addition to* the global one.

**Epic owner:** VrBook launch program · **Gate:** GATE 3 (user stories) · **Prioritization:** MoSCoW · **Grounding:** every "current behavior" claim below is cited against real code paths. Product/design ambiguities are all resolved in [`OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md) (Rounds 1–3, all 🟢); the launch scope is fixed in [`docs/product/PRD.md`](../product/PRD.md) §6 and the gap register [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md).

This epic is the **launch-blocking feature set** for VrBook — the commission-free, multi-tenant direct-booking platform (see [`docs/architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md)). Phase 1 (core booking) + Phase 1.5 (multi-tenant SaaS) are functionally complete on staging; these thirteen stories close the remaining launch-critical gaps: the 501 media endpoints, the two money-correctness defects (fee reversal + merchant-of-record), the cancellation + tax engines, the mobile funnel, the SEO surface, and the two placeholder web pages — plus three revenue/conversion "Should" features (Instant-Book, promo codes, damage deposits). The locked design principle throughout is **standardize the framework, localize the values per property** (OPEN-QUESTIONS Q39R) so every engine we build here (cancellation, tax, discounts) is cross-business-cart-ready without forcing tenants onto identical rules.

## Summary table

| ID | Title | Priority | Estimate | Depends-on |
|---|---|---|---|---|
| VRB-101 | Listing photo upload / manage | Must | M | — |
| VRB-102 | Cancellation-policy engine (2 models) | Must | L | VRB-104 |
| VRB-103 | Stripe Tax + marketplace-facilitator + receipts | Must | L | VRB-105 |
| VRB-104 | Real application-fee reversal on refund | Must | S | — |
| VRB-105 | Reconcile merchant-of-record / tax posture | Must | S | — |
| VRB-106 | Mobile navigation + mobile checkout | Must | M | — |
| VRB-107 | Home page real featured properties | Must | S | — |
| VRB-108 | Guest profile page | Must | S | — |
| VRB-109 | SEO engine (sitemap / robots / canonical / meta) | Must | M | — |
| VRB-110 | Accessibility WCAG 2.2 AA pass | Must (best-effort) | M | VRB-106 |
| VRB-111 | Instant-Book (auto-capture) owner opt-in | Should | M | — |
| VRB-112 | Promo codes / discounts | Should | M | VRB-102, VRB-103 |
| VRB-113 | Damage deposit / security auth-hold | Should | M | — |

**Sequencing note:** VRB-104 and VRB-105 both edit `StripeGateway.cs` — one lane, land VRB-105 first (it's a one-line posture fix), then VRB-104. VRB-103 (tax-as-facilitator) is only *legally coherent* once VRB-105 makes the platform the merchant-of-record, hence the hard dependency. VRB-102's refund resolution reuses the corrected reversal path from VRB-104.

---

### VRB-101 — Listing photo upload / manage
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** M
- **Narrative:** As a property owner, I want to upload, caption, reorder, and delete photos of my listing, so that guests see a real gallery and the listing actually sells (a photoless listing does not convert).
- **Acceptance criteria:**
  - Given I am a `tenant_admin` on the property's tenant, When I POST a JPEG/PNG/WebP ≤ 10 MB to `POST /api/v1/properties/{id}/images`, Then it is stored in the `property-images` blob container under a tenant-scoped path and a `PropertyImage` row is created, and the endpoint returns 201 with a `PropertyImageDto` (id, SAS url, sortOrder, isPrimary, caption).
  - Given a property with ≥ 2 images, When I `PUT /api/v1/properties/{id}/images/order` with an ordered id array, Then `SortOrder` is persisted in that order and the first becomes `IsPrimary`.
  - Given an existing image, When I `DELETE /api/v1/properties/{id}/images/{imageId}`, Then the blob and row are removed and a remaining image is promoted to primary if the deleted one was primary.
  - Given a non-image or > 10 MB file or a disallowed content-type, When I upload, Then I get 422 with a `problem+json` explaining the limit (no partial blob left behind).
  - Given I am not a member of the owning tenant, When I call any image endpoint, Then 403 (cross-tenant refused by the pipeline `TenantAuthorizationBehavior`).
  - Given a property with fewer than 1 active image, When the owner attempts to activate/publish it, Then activation is refused with a clear "add at least one photo" message.
- **TDD plan:**
  - Unit: `UploadPropertyImageHandlerTests.RejectsOversizeFile`, `.RejectsDisallowedContentType`, `.FirstImageBecomesPrimary`; `ReorderPropertyImagesHandlerTests.PersistsSortOrderAndReassignsPrimary`; `DeletePropertyImageHandlerTests.PromotesNextWhenPrimaryDeleted`; `PropertyImageTests.CtorTrimsCaptionAndRequiresTenant` (extends existing `PropertyImage` domain — `src/Modules/VrBook.Modules.Catalog/Domain/PropertyImage.cs`).
  - Integration (`Category=Integration`, Testcontainers Postgres + Azurite): `PropertyImageEndpointsTests.UploadThenListReturnsSasUrl`, `.CrossTenantUploadForbidden`, `.DeleteRemovesBlobAndRow`, `.ReorderRoundTrips`.
  - E2E (Playwright): `owner/listing-photos.spec.ts` — owner uploads 3 photos, reorders, sets primary, deletes one; guest detail page then shows the gallery in the new order.
- **Technical notes:** Replace the three 501 stubs in `src/VrBook.Api/Controllers/PropertiesController.cs:72-88` (`UploadImage`/`ReorderImages`/`DeleteImage`). The plumbing already exists and is unused: `IBlobStorage` (`src/VrBook.Contracts/Interfaces/IBlobStorage.cs` — `UploadAsync`/`DeleteAsync`/`GetReadSasUri`), the `PropertyImage` aggregate + `Promote`/`UpdateCaption`, `IPropertyImageUrlBuilder`/`PropertyImageUrlBuilder` (`src/Modules/VrBook.Modules.Catalog/Infrastructure/Storage/`), and the `stvrbook{env}` storage account with a `property-images` container (`infra/modules/storage.bicep`). Add Catalog commands `UploadPropertyImageCommand`/`ReorderPropertyImagesCommand`/`DeletePropertyImageCommand` (all `ITenantScoped`) + `PropertyImageDto`. No schema change — `catalog.property_images` already exists. Blob path convention: `{tenantId}/{propertyId}/{imageId}.{ext}`.
- **UI/UX spec:** New `PropertyGalleryManager` on the owner edit page (`web/src/app/admin/properties/[id]/`, and the create flow `web/src/app/admin/properties/new/page.tsx`). States: **empty** (dashed dropzone, "Drag photos or browse"), **uploading** (per-tile progress + optimistic thumbnail), **error** (per-tile retry chip, size/type message inline), **success** (grid of draggable tiles using `@dnd-kit`, already a dependency). Primary photo marked with a badge; drag to reorder; hover/focus reveals delete + caption. Responsive: 2-col mobile / 4-col desktop grid; drag works with keyboard (dnd-kit keyboard sensor). a11y: dropzone is a labelled button with `aria-describedby` for limits; each tile has `role="listitem"` with an accessible name ("Photo 1, primary"); reorder announces via `aria-live`; delete confirmation via the existing `ui/ConfirmActionModal`. Design-system: build a reusable `Dropzone` primitive + `ImageTile` (shared UI lib is thin today — only `ui/ConfirmActionModal.tsx` exists; add these under `web/src/components/ui/`). Distinctive touch: primary badge uses the `brand-orange` ramp, drag ghost uses a soft maroon shadow to match the brand system.
- **Configuration:** `Catalog:Images:MaxSizeMb` (default 10 all envs), `Catalog:Images:AllowedContentTypes` (default `image/jpeg,image/png,image/webp`), `Catalog:Images:SasTtlMinutes` (default 10). Storage account + container already provisioned; managed-identity blob access already granted. No new secret.
- **Rollout:** No feature flag (replacing 501s is purely additive). No migration. Backward-compatible: existing empty galleries keep working; guest detail page already renders `PropertyGallery`. Rollback: revert the controller to the 501 stubs; no data migration to unwind.
- **Observability:** Structured logs on upload/delete with `tenant_id`/`property_id`/`image_id`/`size_bytes`; metric `catalog.image.upload.count` + `catalog.image.upload.bytes`; alert on upload 5xx rate > 2% over 5 min. Log SAS-generation failures distinctly (blob-auth regressions).
- **Definition of Done:** unit+integration green → code-review (tenant-scoping, content-type validation, no blob orphaning on failure) → staging deploy + owner uploads real photos to a test listing → prod deploy + verify a live listing gallery → monitored 48h.
- **Dependencies:** blocks nothing; independent. (Guest gallery render already shipped in `web/src/app/properties/[slug]/page.tsx`.)
- **Parallelisation:** Lane **catalog-media**. Owns: `PropertiesController.cs` image actions, new Catalog `Application/Properties/Commands/*Image*.cs`, `web/src/app/admin/properties/**` gallery manager, new `web/src/components/ui/Dropzone.tsx` + `ImageTile.tsx`. No overlap with money/tax lanes.

---

### VRB-102 — Cancellation-policy engine (two owner-selected models)
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** L
- **Narrative:** As a property owner, I want to choose one of two cancellation models per property (a platform-tiered refund schedule, or a paid refundable-rate upgrade), so that I control my own cancellation terms while the platform standardizes the machinery; and as a guest, I want the exact policy shown before I pay and honored on cancel.
- **Acceptance criteria (grounded in OPEN-QUESTIONS Q24 / Q3R-Q24R):**
  - Given the platform admin has set global tier numbers, When an owner configures a property with **Model 1 (Tiered)**, Then the property stores a reference to the tiered model (not copies of the numbers) and the guest sees "full refund ≥ N₁ days / X% between N₂–N₁ days / none < N₃h" resolved from the platform-set values.
  - Given **Model 2 (Refundable-rate upgrade)**, When a guest books, Then they may pay an extra configurable amount for a full-refund option; without it the booking is **non-refundable**; with it, a cancel request received **before check-in** yields a full refund.
  - Given a booking is placed, When it is created, Then the effective policy (model + resolved numbers/percentages) is **snapshotted onto the booking** so later admin changes to global tiers or property config do not retroactively alter an in-flight booking.
  - Given a Confirmed (captured) booking is cancelled, When the refund is computed, Then the amount is resolved from the **snapshotted** policy and passed to the refund path; a Tentative/unconfirmed booking is always fully released (nothing captured).
  - Given the cross-business-cart design, When policy is attached, Then it is **per line item** (framework standardized, values per property) — the launch single-line booking is the degenerate one-item case.
  - Given the current root default, Then the `Booking.CancellationPolicy` enum root (`Moderate` default) is **replaced** by the snapshotted policy value object; no code path reads the old `Flexible/Moderate/Strict` enum.
- **TDD plan:**
  - Unit: `TieredPolicyResolverTests.FullRefundAtOrBeyondFirstTier`, `.PartialRefundInMiddleTier`, `.NoRefundInsideCutoff`; `RefundableUpgradePolicyTests.NonRefundableWithoutUpgrade`, `.FullRefundWhenUpgradedAndBeforeCheckIn`, `.NoRefundAfterCheckIn`; `PolicySnapshotTests.SnapshotIsImmuneToLaterGlobalTierChange`; `BookingPlaceTests.SnapshotsEffectivePolicy` (replaces the `CancellationPolicyCode.Moderate` default at `src/Modules/VrBook.Modules.Booking/Domain/Booking.cs:40`).
  - Integration: `CancellationRefundResolutionTests.ConfirmedTieredMiddleTierRefunds50Pct`, `.RefundableUpgradeFullRefund`, `.TentativeAlwaysReleasesFull` (Testcontainers, exercises `RefundForBookingCommand`).
  - E2E: `guest/cancel-tiered-refund.spec.ts` (guest sees policy at checkout, cancels inside partial tier, sees expected refund), `owner/select-cancellation-model.spec.ts`, `platform-admin/set-global-tiers.spec.ts`.
- **Technical notes:** Deprecate/replace `src/VrBook.Contracts/Enums/CancellationPolicyCode.cs` and the `CancellationPolicy` property on `src/Modules/VrBook.Modules.Booking/Domain/Booking.cs:40`. Introduce a `CancellationPolicySnapshot` value object (model discriminator + resolved tier numbers/percentages OR upgrade fee/flag) persisted on the booking (new columns in `booking` schema: add→backfill→NOT-NULL migration per repo convention). Property-level selection lives in Catalog or Pricing (owner picks model + the property-level refundable-upgrade price); platform-global tier numbers are a platform-admin-configured setting surfaced via the Admin/Reports read model. Refund resolution wires into `src/Modules/VrBook.Modules.Payment/Application/Commands/RefundForBookingCommand.cs` — replace the flat `RefundOptions.ServiceFeePercent` computation (`RefundForBookingCommand.cs:80,158-163`) with the snapshotted policy result. Emitted `BookingCancelled` currently publishes `0m` refund (`Booking.cs:173,195`) — carry the resolved amount. **Cross-module rule:** attach policy per line item (`BookingLineItem`) to stay cross-business-cart-ready per the locked design principle.
- **UI/UX spec:** (1) **Owner** — a "Cancellation" section on the property editor: radio between the two models with plain-English preview of the resolved terms; Model 2 exposes a "refundable upgrade price" money input. States: loading (skeleton), success (live preview text), error (validation on upgrade price). (2) **Guest** — policy panel on the property detail (`web/src/app/properties/[slug]/page.tsx`) and a bold, unambiguous restatement in the checkout summary + a per-tier timeline; on the "My trips" cancel modal show the exact refund the guest will receive **before** they confirm (reuse `ui/ConfirmActionModal` with a computed-refund line). (3) **Platform admin** — a global-tiers config form. a11y: radios grouped in a labelled `fieldset/legend`; refund preview in an `aria-live` region; timeline is not color-only (icons + text). Distinctive: a horizontal refund-timeline component (build `web/src/components/booking/RefundTimeline.tsx`) rather than a bland table.
- **Configuration:** `Cancellation:Tiers:FirstTierDays`, `:SecondTierDays`, `:MiddleTierRefundPct`, `:FinalCutoffHours` (platform-set global values; defaults dev/staging = 7/2/50/48, prod owner-confirmed). `Cancellation:RefundableUpgrade:Enabled` (default true). Owner-selected model + upgrade price are per-property data, not config.
- **Rollout:** Flag `Features:CancellationEngineV2` (default off in prod until data backfill lands, on in dev/staging). Migration order: add snapshot columns (nullable) → backfill existing bookings from the old enum mapping (Flexible→tiered defaults) → set NOT-NULL → flip flag. Backward-compatible: old bookings get a snapshot at backfill; the enum stays as an `[Obsolete]` bridge for one release. Rollback: flip flag off (falls back to `ServiceFeePercent` flat refund); snapshot columns stay (harmless).
- **Observability:** Log resolved refund per cancel (`booking_id`, `policy_model`, `resolved_refund_cents`, `days_before_checkin`); metric `booking.cancellation.refund_pct` histogram; alert if refunds resolve to > captured total (should be impossible — indicates snapshot bug). Dashboard: cancellations by model.
- **Definition of Done:** tests green → review (snapshot immutability, no old-enum reads, per-line attachment) → staging: owner selects each model, guest cancels in each tier, refund matches → prod verify with a real cancel → monitored.
- **Dependencies:** blocked-by **VRB-104** (refund path must actually reverse fees for the resolved amount to be financially correct). Blocks **VRB-112** (promo discounts interact with the refundable base).
- **Parallelisation:** Lane **cancellation**. Owns: `Booking.cs`, `CancellationPolicyCode.cs`, new `CancellationPolicySnapshot` VO + Booking migration, policy config in Catalog/Pricing selection command, `RefundForBookingCommand.cs` refund-amount resolution (coordinate with VRB-104 which owns the *fee-reversal* half of the same file — split by concern, land VRB-104 first).

---

### VRB-103 — Stripe Tax + marketplace-facilitator + emailed receipts
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** L
- **Narrative:** As a guest, I want accurate US lodging/sales tax shown at quote time and an emailed receipt with a tax breakdown, so that pricing is legally correct and I trust the total; as the platform, I want to collect + remit tax as the marketplace facilitator so tenants don't each shoulder tax compliance.
- **Acceptance criteria (OPEN-QUESTIONS Q25):**
  - Given a quote request for a property with a jurisdiction, When the quote is computed, Then real tax is returned via the `ITaxCalculator` contract (Stripe Tax), itemized by jurisdiction, replacing the zero-tax stub.
  - Given a booking is placed and later confirmed/captured, When the charge is created, Then tax is collected **on the platform account** (platform as facilitator) — not passed to the connected account.
  - Given a captured booking, When capture succeeds, Then the guest receives an email receipt showing subtotal, fees, **per-jurisdiction tax lines**, and total.
  - Given Stripe Tax is unreachable, When a quote is requested, Then the quote fails closed with a clear "tax temporarily unavailable" error (never silently shows zero tax at launch).
  - Given the per-state facilitator refinement (design C6), Then the engine records the collecting/remitting posture per state so remittance reporting can be produced (sub-scope tracked as a dependency, below).
- **TDD plan:**
  - Unit: `StripeTaxCalculatorTests.MapsStripeCalculationToTaxLines`, `.ThrowsWhenStripeTaxErrors`, `.AppliesTaxToFeesPerConfig`; `QuoteWithTaxTests.TotalIncludesTaxLines` (Pricing quote engine).
  - Integration: `TaxCalculationEndpointTests.QuoteReturnsNonZeroTaxForUsAddress`, `ReceiptEmailTests.CaptureEnqueuesReceiptWithTaxBreakdown` (Notifications template render + queue row).
  - E2E: `guest/quote-shows-tax.spec.ts`, `guest/receipt-email.spec.ts` (assert receipt notification row + rendered tax lines in the dispatch preview).
- **Technical notes:** Implement a real `StripeTaxCalculator : ITaxCalculator` in the Payment module and register it in place of `src/VrBook.Infrastructure/Stubs/StubTaxCalculator.cs` (which returns `Money.Zero` — `StubTaxCalculator.cs:12-17`). The contract already exists and is jurisdiction-aware: `src/VrBook.Contracts/Interfaces/ITaxCalculator.cs` (`TaxCalculationResult`, `TaxLine{Label, Amount, JurisdictionCode}`). Pricing already depends on the interface; the quote flow currently carries `Taxes` on `Booking` (`Booking.cs:35`). Charge creation is in `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeGateway.cs` — tax must land on the platform account, which is **only coherent after VRB-105** drops `OnBehalfOf=supplier` (`StripeGateway.cs:71`). Receipts reuse the Notifications template pattern (`src/Modules/VrBook.Modules.Notifications/Templates/*.mustache`, e.g. `booking.confirmed.mustache`) — add `booking.receipt.mustache` + sample JSON. Enable Stripe Tax on the platform Stripe account (operator task).
- **UI/UX spec:** Guest-facing only. The `PriceQuoteWidget` (`web/src/components/property/PriceQuoteWidget.tsx`) gains an itemized breakdown: subtotal, fee lines, **tax lines by jurisdiction**, total — expandable "how is this calculated?" disclosure. States: loading (skeleton rows), success (line items animate in), **tax-unavailable error** (inline, blocks "Book" CTA with retry). Checkout summary mirrors it. a11y: breakdown is a `<dl>` with `dt/dd` pairs, not a div grid; the total has `aria-live` so screen-reader users hear it update when dates change. Distinctive: a subtle "Taxes collected & remitted by VrBook" trust line (facilitator posture) styled as a small badge.
- **Configuration:** `Tax:Provider=StripeTax` (default), `Tax:ApplyToFees` (default true, per Q25), `Tax:FailClosed` (default true prod, may be false in dev for offline work). Stripe secret already in KV (`stripe-*`); enabling Stripe Tax is an account-side toggle, not a new secret. `Notifications` sender already configured. **Sub-scope / dependency:** per-state facilitator refinement (design C6) — a `TaxRemittancePosture` per state — is a distinct follow-up story; this story lands the collection engine + receipts and records posture data.
- **Rollout:** Flag `Features:StripeTaxEnabled` (default off prod until Stripe Tax account config is verified, on dev/staging with test keys). No destructive migration (tax already stored on booking). Backward-compatible: with flag off, `StubTaxCalculator` remains and quotes show zero (current behavior). Rollback: flip flag → revert to stub.
- **Observability:** Log every tax calc (`property_id`, `jurisdiction`, `tax_cents`, `stripe_calculation_id`); metric `tax.calc.latency_ms` + `tax.calc.error_rate`; **alert if tax error rate > 1%** (fail-closed means this blocks bookings — page on-call); receipt-dispatch success metric. Dashboard tile: collected tax by state (feeds remittance).
- **Definition of Done:** tests green → review (fail-closed behavior, tax on platform account, receipt correctness) → staging: guest gets non-zero tax + receipt email with breakdown → prod: verify a live booking receipt + Stripe Tax dashboard shows collection → monitored (accountant reconciles first week).
- **Dependencies:** **blocked-by VRB-105** (platform must be merchant-of-record before it can collect facilitator tax). Blocks **VRB-112** (promo affects taxable base). Sub-scope: per-state facilitator refinement (design C6) filed as a follow-up.
- **Parallelisation:** Lane **tax**. Owns: new `StripeTaxCalculator.cs` (Payment infra), DI swap from `StubTaxCalculator`, Pricing quote assembly, `booking.receipt.mustache` + Notifications sample, `PriceQuoteWidget.tsx` breakdown. Coordinates with VRB-105 on `StripeGateway.cs` charge posture (VRB-105 lands first).

---

### VRB-104 — Real application-fee reversal on refund
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** S
- **Narrative:** As the platform, I want the application (platform) fee actually clawed back proportionally when a booking is refunded, so that we don't keep fees on money we returned to the guest and the connected-account balance can't go negative.
- **Acceptance criteria (gap G37 / design C4):**
  - Given a Connect destination charge is partially refunded, When the refund executes, Then the proportional application-fee reversal is **actually performed** via Stripe (not written to metadata only) and the reversed cents are **persisted** on the `Refund` row as `fee_reversal_cents`.
  - Given a full refund, When it executes, Then the entire application fee is reversed (`RefundApplicationFee=true`).
  - Given prior refunds exist, When a new refund computes the negative-balance guard, Then it uses the **persisted** prior reversal cents (not the current approximation that assumes each prior refund used a proportional reversal).
  - Given the reversal call fails, When the refund is attempted, Then the operation fails atomically (no refund row persisted claiming a reversal that didn't happen).
- **TDD plan:**
  - Unit: `StripeGatewayRefundTests.PartialRefundInvokesApplicationFeeRefundService`, `.FullRefundSetsRefundApplicationFeeTrue`, `.PersistsFeeReversalCents`; `RefundForBookingHandlerTests.NegativeBalanceGuardUsesPersistedPriorReversals`.
  - Integration: `RefundFeeReversalTests.PartialRefundReversesProportionalFee` (Testcontainers + Stripe test-mode / recorded interaction), asserts `payment.refunds.fee_reversal_cents` populated.
  - E2E: covered indirectly by `guest/cancel-tiered-refund.spec.ts` (VRB-102) asserting the refund succeeds; add `owner/refund-reverses-fee.spec.ts` if a platform-admin refund surface exists.
- **Technical notes:** The bug: `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeGateway.cs:135-144` writes `application_fee_refund_cents` to **refund metadata only** with a comment that the typed property "doesn't exist on 47.x" — so no fee is actually reversed. Fix: after (or instead of) the `RefundService.CreateAsync`, call Stripe's `ApplicationFeeRefundService.CreateAsync(applicationFeeId, ...)` (or set `RefundApplicationFee` for full + an explicit `ApplicationFeeRefund` amount for partial) to execute the reversal. Persist the reversed cents: add `FeeReversalCents` to the `Refund` entity (`src/Modules/VrBook.Modules.Payment/Domain/Refund.cs`) + `PaymentIntent.AddRefund(...)` (`PaymentIntent.cs:93`) + a `payment` schema migration `fee_reversal_cents`. Then in `RefundForBookingCommand.cs:106-110` replace the approximation comment ("We don't persist the prior reversal cents on Refund yet") with a read of the persisted values for the negative-balance guard.
- **UI/UX spec:** None (backend money-correctness). Any existing admin refund confirmation may optionally show "platform fee reversed: $X" — additive, not required.
- **Configuration:** No new config/secret/flag. Uses existing `stripe-*` KV secrets and `Payment:AllowPlatformFallback`.
- **Rollout:** No flag (correctness fix). Migration: add `fee_reversal_cents` (nullable, backfill prior rows to `NULL`/best-effort, leave nullable — historical rows genuinely unknown). Backward-compatible: reads treat null as "unknown, fall back to approximation for legacy rows only." Rollback: revert gateway + handler; the column is harmless if left.
- **Observability:** The existing log line (`StripeGateway.cs:150-152`) already emits `application_fee_refund_cents` — extend to log the **actual** Stripe reversal id. Metric `payment.fee_reversal.cents` (sum) + `payment.refund.negative_balance_rejections`; alert if reversal call error rate > 0.5%. Reconciliation check: sum(fee_reversal_cents) vs Stripe dashboard.
- **Definition of Done:** tests green → review (atomicity: no refund row without a real reversal; guard uses persisted values) → staging: partial + full refund on a test Connect account, verify reversal in Stripe dashboard + `fee_reversal_cents` row → prod verify → monitored (finance reconciles).
- **Dependencies:** none blocking. **Blocks VRB-102** (its resolved refund amount must actually reverse fees).
- **Parallelisation:** Lane **payments-core**. Owns: `StripeGateway.cs` refund methods (`RefundInternalAsync`), `Refund.cs`, `PaymentIntent.cs:AddRefund`, `payment` schema migration, `RefundForBookingCommand.cs:96-122` guard read. Shares `StripeGateway.cs` with VRB-105 — land VRB-105 first (charge side), then this (refund side); different methods, minimal conflict.

---

### VRB-105 — Reconcile merchant-of-record / tax posture
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** S
- **Narrative:** As the platform, I want to be the genuine merchant-of-record on single-tenant charges, so that the marketplace-facilitator tax posture (VRB-103) is legally correct and tax liability sits on the platform, not the supplier.
- **Acceptance criteria (gap G38 / design C5):**
  - Given a single-tenant Connect destination charge, When the PaymentIntent is created, Then `OnBehalfOf` is **not** set to the supplier account (platform is MoR), while `TransferData.Destination` + `ApplicationFeeAmount` still route funds + fee correctly.
  - Given the charge posture change, When capture/refund/statement-descriptor behavior is exercised, Then funds still settle to the connected account net of the platform fee (no regression in payout).
  - Given VRB-103's Stripe Tax collection, When tax is computed, Then it is collected on the platform account consistent with the platform being MoR.
- **TDD plan:**
  - Unit: `StripeGatewayChargeTests.DestinationChargeDoesNotSetOnBehalfOf`, `.StillSetsTransferDataAndApplicationFee`.
  - Integration: `DestinationChargeMoRTests.FundsSettleNetOfFeeWithoutOnBehalfOf` (Stripe test mode) — assert charge succeeds, transfer created, no `on_behalf_of`.
  - E2E: existing `guest` booking specs must stay green (regression gate); no new spec required.
- **Technical notes:** One-line posture change at `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeGateway.cs:71` — remove `opts.OnBehalfOf = destinationAccountId;` (and revisit the "OnBehalfOf for VAT" comment at `:67-71`). Keep `TransferData.Destination` (`:69`) and `ApplicationFeeAmount` (`:70`). Confirm statement-descriptor / dispute-liability implications are acceptable for the launch posture (Stripe docs: dropping `on_behalf_of` makes the platform the settlement merchant + card-statement entity). Verify no downstream code reads `on_behalf_of` back from Stripe.
- **UI/UX spec:** None (server posture change). Card statement descriptor may change to the platform name — note for support/hypercare.
- **Configuration:** No new config. (If a per-charge posture toggle is ever needed for a future multi-tenant split, that's Phase 3 — out of scope.)
- **Rollout:** No flag (must be consistent with VRB-103; ship them together or VRB-105 first). No migration. Backward-compatible for funds flow; the guest-visible change is only the statement descriptor. Rollback: re-add the one line.
- **Observability:** The existing create log (`StripeGateway.cs:77-79`) already logs `destination_account` + `fee_cents`; add an explicit `on_behalf_of=false` marker for audit. Watch dispute-rate + payout-success metrics for one week post-change (MoR shift can affect dispute routing).
- **Definition of Done:** tests green → review (confirm MoR implications with Stripe posture + accountant) → staging: place + capture a booking, confirm settlement net of fee and no `on_behalf_of` on the intent → prod verify a live charge + statement descriptor → monitored 1 week.
- **Dependencies:** none blocking. **Blocks VRB-103** (facilitator tax collection requires platform-as-MoR). Shares `StripeGateway.cs` with VRB-104 (charge vs refund methods).
- **Parallelisation:** Lane **payments-core** (same lane as VRB-104; land this first). Owns: `StripeGateway.cs:65-72` (`CreatePaymentIntentInternalAsync` destination block).

---

### VRB-106 — Mobile navigation + mobile checkout
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** M
- **Narrative:** As a mobile guest (>60% of travel traffic), I want a working navigation menu and a checkout I can complete on a phone, so that I can find and book a stay without a desktop.
- **Acceptance criteria (gap G19):**
  - Given a viewport < 768px, When I load any page with the site header, Then a hamburger button reveals the primary nav (Stays / My trips / Account, and Admin when I'm an operator) — today those links are `hidden md:flex` and vanish entirely on mobile.
  - Given the mobile menu is open, When I press Escape or tap outside or select a link, Then it closes and focus returns to the hamburger.
  - Given I open the mobile menu, When I tab, Then focus is trapped within it and the toggle is `aria-expanded`.
  - Given the booking/checkout flow on a 360px viewport, When I proceed through quote → guest details → payment (Stripe Elements) → confirmation, Then every control is reachable, tappable (≥ 44px targets), and nothing overflows horizontally.
- **TDD plan:**
  - Unit (Vitest): `MobileNav.test.tsx` — renders hamburger < md, toggles on click, `aria-expanded` flips, Escape closes, operator sees Admin link (mirror `SiteHeaderNav` operator logic); `MobileNav.focusTrap.test.tsx`.
  - E2E (Playwright, mobile project / `devices['iPhone 13']`): `guest/mobile-nav.spec.ts` (open menu, navigate), `guest/mobile-checkout.spec.ts` (full funnel on mobile viewport to confirmation).
- **Technical notes:** `web/src/components/layout/SiteHeaderNav.tsx:47` is `hidden items-center gap-6 md:flex` — the entire primary nav disappears below `md`. Add a `MobileNav` sibling (hamburger + slide-over sheet) rendered `md:hidden`, reusing the same operator-derivation (`useMe().isPlatformAdmin` + `useMyTenants()` `tenant_admin` membership, per `SiteHeaderNav.tsx:41-44`). Audit the checkout components (`web/src/components/property/PriceQuoteWidget.tsx`, the booking flow pages, Stripe Elements wrapper) for fixed widths / non-wrapping rows at small breakpoints. Build a reusable `Sheet`/`Drawer` primitive under `web/src/components/ui/` (none exists today).
- **UI/UX spec:** Hamburger in the header on mobile; tapping opens a slide-over (from the right) with large tap targets, the role badge (`SiteHeaderRoleBadge`) and sign-in/out. States: closed, open (backdrop + trapped focus), operator vs guest link sets. Checkout: single-column stacked layout, sticky "Total + Book" bar at the bottom on mobile, Stripe Elements sized to full width. a11y (WCAG 2.2 AA — a launch focus): hamburger `aria-label="Menu"` + `aria-expanded` + `aria-controls`; focus trap while open; Escape closes; visible focus rings; target size ≥ 24×24 CSS px (2.2 AA §2.5.8) — we target 44px. Distinctive: the drawer uses the brand gradient header used on `/` hero, not a flat panel.
- **Configuration:** None. (Playwright mobile project may need a config entry in `web/playwright.config.ts` if not present.)
- **Rollout:** No flag (progressive enhancement — desktop unchanged). No migration. Backward-compatible. Rollback: remove `MobileNav`, header reverts to desktop-only nav.
- **Observability:** Client analytics (once VRB analytics lands): mobile-menu-open events, mobile funnel step-through vs desktop. No server change.
- **Definition of Done:** Vitest + mobile E2E green → review (focus trap, target sizes, no horizontal overflow) → staging: complete a booking on a real phone / device emulation → prod verify on a phone → monitored (mobile conversion in funnel metrics).
- **Dependencies:** none blocking. **Blocks VRB-110** partially (its focus-trap work is reused by the a11y pass) — coordinate the `Sheet` primitive.
- **Parallelisation:** Lane **web-shell**. Owns: new `web/src/components/layout/MobileNav.tsx`, new `web/src/components/ui/Sheet.tsx`, responsive tweaks in checkout components. No backend files.

---

### VRB-107 — Home page real featured properties
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** S
- **Narrative:** As a guest landing on the home page, I want to see real, bookable featured properties, so that the site looks live and I can click straight into a listing (today it shows a hardcoded placeholder card).
- **Acceptance criteria (gap G17):**
  - Given I load `/`, When the featured section renders (SSR), Then it shows up to 6 real active properties fetched from the search API, each linking to its detail page — replacing the single "Featured properties land here" placeholder.
  - Given the API returns fewer than 6 (or zero) properties, When rendering, Then an appropriate empty/partial state shows (no broken/placeholder card).
  - Given the API is slow, When the page streams, Then the existing skeleton shows first (already present) then hydrates with real cards.
  - Given a featured property, When I click it, Then I navigate to `/properties/{slug}`.
- **TDD plan:**
  - Unit (Vitest): `FeaturedProperties.test.tsx` — renders real cards from a mocked fetch, renders empty state on `[]`, each card links to the slug.
  - E2E: `anon/home-featured.spec.ts` — home shows ≥ 1 real property card (from the E2E seed property) and clicking navigates to detail. (Fits the anon blocking-smoke suite.)
- **Technical notes:** `web/src/app/page.tsx:23-45` hardcodes `placeholderProperties` and `FeaturedProperties` returns them; the comment already specifies the intended call. Replace with a server-side `await apiFetch<PagedResult<PropertyCardModel>>('/properties', { query: { sort: '-rating', limit: 6 } })` against the existing public `GET /api/v1/properties` search (`src/VrBook.Api/Controllers/PropertiesController.cs:24-32`, anonymous). Reuse `PropertyCard` (already imported). Keep the `Suspense` + `FeaturedSkeleton` (already there). Map the search DTO to `PropertyCardModel`.
- **UI/UX spec:** Featured grid (1/2/3 col responsive — already coded). States: **loading** (existing skeleton), **success** (real cards with cover image, title, location, nightly rate, rating), **empty** ("New stays coming soon" tasteful message, not a fake card), **error** (silent fallback to empty — home must never 500). a11y: each card is a single focusable link with an accessible name (title + location); rating uses text + icon, not color alone; images have alt text. Distinctive: keep the brand hero; ensure featured cards have consistent aspect-ratio cover images (ties to VRB-101 real photos).
- **Configuration:** `Home:FeaturedLimit` (default 6), `Home:FeaturedSort` (default `-rating`). No secret/flag.
- **Rollout:** No flag. No migration. Backward-compatible. Rollback: restore the placeholder array.
- **Observability:** Server log if the featured fetch fails (falls back to empty); client analytics: featured-card click-through rate (once analytics lands).
- **Definition of Done:** Vitest + anon E2E green → review (SSR fetch error handling, empty state) → staging: home shows real seeded properties → prod verify with live listings → monitored.
- **Dependencies:** none blocking (search API already live). Visually improved by **VRB-101** (real cover photos).
- **Parallelisation:** Lane **web-marketing**. Owns: `web/src/app/page.tsx` `FeaturedProperties`/`placeholderProperties`. Shares this lane with VRB-109 but different concerns (VRB-109 adds `sitemap.ts`/`robots.ts`, not `page.tsx` body).

---

### VRB-108 — Guest profile page
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** S
- **Narrative:** As a guest, I want to view and edit my profile (name, phone) and see my email + loyalty tier, so that my booking contact details are correct (today the page is a dashed placeholder).
- **Acceptance criteria (gap G18):**
  - Given I open `/account/profile`, When it loads, Then it shows my current `displayName`, `email` (read-only), `phone`, and loyalty tier from `GET /api/v1/me`.
  - Given I edit name/phone and save, When I submit, Then it calls `PUT /api/v1/me` and shows a success state; on validation error it shows field-level errors.
  - Given the save is in-flight, When I submit, Then the button is disabled/busy and inputs are locked.
  - Given I am not signed in, When I visit the page, Then the existing `SignInGate` prompts sign-in.
- **TDD plan:**
  - Unit (Vitest): `ProfileForm.test.tsx` — populates from `useMe`, submits `displayName`/`phone` to a mocked `PUT /me`, shows success, shows field error on 422, disables while busy; email field is read-only.
  - Integration: none new (endpoint exists); optionally `UpdateProfileHandlerTests.TrimsAndPersists` if not already covered.
  - E2E: `guest/edit-profile.spec.ts` — signed-in guest edits name, saves, reloads, sees persisted value.
- **Technical notes:** Replace the stub `web/src/app/account/profile/page.tsx` (currently a `'use client'` component rendering a dashed placeholder). The backend is ready: `PUT /api/v1/me` → `UpdateProfileCommand(request.DisplayName, request.Phone)` (`src/VrBook.Api/Controllers/IdentityController.cs:29-31`) and `GET /api/v1/me` (`:23`). Reuse `useMe` (`web/src/hooks/useMe.ts`) for read; add a `useUpdateProfile` mutation via the hand-written `apiFetch` client (`web/src/lib/api/client.ts`) + TanStack Query invalidation of the `me` query. Loyalty tier is available via `me/loyalty`.
- **UI/UX spec:** A real form: DisplayName (text), Email (read-only, with a note that it's managed by the identity provider), Phone (tel), Loyalty tier (read-only badge). States: **loading** (skeleton form), **success** (toast/inline "Saved"), **error** (field + form-level), **dirty/pristine** (Save disabled until changed). Responsive single column. a11y: proper `<label for>` on every input, `aria-invalid` + `aria-describedby` for errors, `aria-live` save confirmation, read-only email conveyed to AT. Design-system: build a shared `TextField` primitive (validation + label + error), reusable across VRB-101/112/113 forms — the shared UI lib is thin (only `ui/ConfirmActionModal`). Distinctive: loyalty tier badge reuses the `SiteHeaderRoleBadge` chip styling for consistency.
- **Configuration:** None. No secret/flag.
- **Rollout:** No flag. No migration. Backward-compatible. Rollback: restore the stub.
- **Observability:** Client analytics: profile-save success/failure (once analytics lands). Server already audits `UpdateProfileCommand` via the Identity `AuditLogBehavior`.
- **Definition of Done:** Vitest + E2E green → review (read-only email, optimistic invalidation, a11y) → staging: guest edits + persists → prod verify → monitored.
- **Dependencies:** none (endpoint live). Shares the new `TextField` primitive with form-heavy stories (coordinate ownership).
- **Parallelisation:** Lane **web-account**. Owns: `web/src/app/account/profile/page.tsx`, new `web/src/components/ui/TextField.tsx`, `web/src/hooks/useUpdateProfile.ts`.

---

### VRB-109 — SEO engine (sitemap / robots / canonical / per-property meta)
- **Epic:** Launch Features · **Priority:** Must · **Estimate:** M
- **Narrative:** As the business, I want a full SEO surface (sitemap.xml, robots.txt, canonical URLs, rich per-property meta + validated structured data), so that direct-booking organic traffic — the entire moat vs OTAs — can actually find and index our listings.
- **Acceptance criteria (gap G34 / PRD §7):**
  - Given a crawler requests `/sitemap.xml`, When served, Then it lists the home, `/properties`, and every active property detail URL with `lastmod`, generated from the live catalog — no static file exists today.
  - Given a crawler requests `/robots.txt`, When served, Then it allows public pages, disallows `/account`, `/admin`, `/auth`, and points at the sitemap.
  - Given any public page, When rendered, Then it emits a `<link rel="canonical">` to its clean absolute URL.
  - Given a property detail page, When rendered, Then `generateMetadata` emits title, description, canonical, and OpenGraph/Twitter tags (extend the current partial `openGraph`), and the existing JSON-LD `LodgingBusiness` validates against schema.org (extend with `priceRange`/`image` where available).
  - Given the search/listing pages, When rendered SSR, Then they remain server-rendered (already are) for crawlability.
- **TDD plan:**
  - Unit (Vitest): `sitemap.test.ts` — includes active property slugs from a mocked catalog, excludes inactive; `robots.test.ts` — disallows private paths, references sitemap; `propertyMetadata.test.ts` — canonical + OG tags present.
  - E2E: `anon/seo-surface.spec.ts` — `/sitemap.xml` returns valid XML with the seed property URL, `/robots.txt` disallows `/admin`, property page has a canonical link + parseable JSON-LD.
- **Technical notes:** Add Next.js App Router `web/src/app/sitemap.ts` and `web/src/app/robots.ts` (metadata routes) — neither exists (glob confirms no `sitemap`/`robots` under `web/src/app`). `sitemap.ts` fetches active properties via the public search API (`GET /api/v1/properties`). Extend `generateMetadata` in `web/src/app/properties/[slug]/page.tsx:31-45` (currently title + description + partial `openGraph`) with `alternates.canonical`, Twitter card, and image. The JSON-LD `LodgingBusiness` block (`page.tsx:68-95`) already includes address/geo/aggregateRating — extend with `image` (from VRB-101 photos) + `priceRange`. Add canonical to the root layout for other public pages. Requires a stable public base URL config (avoid the hard-coded staging FQDN gotcha, gap G8).
- **UI/UX spec:** Non-visual (crawler-facing) except the `<head>` tags. Ensure no `noindex` leaks onto public pages and that private routes (`/account`, `/admin`) carry `noindex`. Validate Core Web Vitals aren't regressed (PRD §7: LCP<2.5s). No component to build.
- **Configuration:** `Seo:PublicBaseUrl` per env (dev `http://localhost:3000`, staging staging FQDN, **prod the real domain**) — must come from env, not hard-coded (guard against gap G8). `Seo:SitemapMaxUrls` (default 50000).
- **Rollout:** No flag. No migration. Backward-compatible (purely additive routes + tags). Rollback: remove `sitemap.ts`/`robots.ts` + revert meta.
- **Observability:** Server log sitemap generation (count of URLs, fetch failures); post-launch: Google Search Console coverage + indexed-page count (owner monitors); alert if sitemap returns 5xx or 0 URLs.
- **Definition of Done:** Vitest + E2E green → review (canonical correctness, private-path exclusion, JSON-LD validity via Google Rich Results test) → staging: sitemap/robots served, meta present → prod verify + submit sitemap to Search Console → monitored (indexing).
- **Dependencies:** none blocking. Enriched by **VRB-101** (JSON-LD `image`). Depends on a resolved prod public domain (config, not a story).
- **Parallelisation:** Lane **web-marketing**. Owns: new `web/src/app/sitemap.ts` + `web/src/app/robots.ts`, `generateMetadata` in `web/src/app/properties/[slug]/page.tsx`, canonical in `web/src/app/layout.tsx`. Coordinates with VRB-107 (same lane, different files).

---

### VRB-110 — Accessibility WCAG 2.2 AA pass
- **Epic:** Launch Features · **Priority:** Must (best-effort target, per OPEN-QUESTIONS Q28 — not a hard launch gate) · **Estimate:** M
- **Narrative:** As a guest or owner using assistive technology or a keyboard, I want the app to meet WCAG 2.2 AA, so that everyone can find, book, and manage stays; and as the business, I want the legal/ethical baseline covered.
- **Acceptance criteria (gap G33):**
  - Given an automated axe scan on the key flows (home, search, property detail, checkout, my-trips, owner dashboard), When run, Then there are **zero critical/serious** violations.
  - Given any modal (starting with `ui/ConfirmActionModal`), When open, Then focus is **trapped**, Escape closes it, and focus returns to the trigger — today `ConfirmActionModal` sets `role="dialog"`/`aria-modal` but has **no focus trap or Escape handling** (its own comment defers that to "a shared Radix Dialog wrapper").
  - Given keyboard-only navigation, When I traverse any page, Then focus order is logical, focus is always visible, and there are no keyboard traps.
  - Given color/contrast, When measured, Then text and UI components meet AA contrast (4.5:1 / 3:1).
  - Given interactive targets, When measured, Then they meet WCAG 2.2 §2.5.8 target-size minimums.
- **TDD plan:**
  - Unit (Vitest + `jest-axe`): `ConfirmActionModal.a11y.test.tsx` (focus trap, Escape, restore focus, no axe violations); `PropertyDetail.a11y.test.tsx`, `Checkout.a11y.test.tsx` (no serious axe violations).
  - E2E (Playwright + `@axe-core/playwright`): `a11y/anon-flows.spec.ts`, `a11y/guest-flows.spec.ts`, `a11y/owner-flows.spec.ts` — scan each page, assert zero critical/serious; keyboard-only checkout traversal.
- **Technical notes:** Add a focus-trap to `web/src/components/ui/ConfirmActionModal.tsx` (Escape + trap + focus restore) — either inline or via the shared `Dialog` primitive its comment anticipates (`ConfirmActionModal.tsx:16-19`). Reuse the focus-trap built for VRB-106's mobile `Sheet`. Audit for missing labels, heading order, landmark roles, contrast (the two parallel color systems — semantic tokens vs raw `brand-*` — flagged in CURRENT-STATE §11 — may hide contrast issues). Add axe to CI (informational→blocking on critical). This is a cross-cutting audit lane; changes touch many components but should be small/surgical.
- **UI/UX spec:** Focus rings visible and consistent (brand-orange ring). Skip-to-content link on public pages. `aria-live` regions for async results (quote total, save confirmations — ties to VRB-103/108). Reduced-motion respected on the animated skeletons/hero gradient. Distinctive but accessible: keep the brand look while guaranteeing contrast (may require darkening `brand-orange` text-on-white usages).
- **Configuration:** None. CI gate config: axe severity threshold (fail on `critical`/`serious`).
- **Rollout:** No flag. No migration. Backward-compatible. Rollback: individual component reverts (low risk).
- **Observability:** CI axe report artifact per build; post-launch, track a11y regressions via the nightly E2E axe scan. No runtime metric.
- **Definition of Done:** axe + a11y unit/E2E green (zero critical/serious) → review (manual keyboard + screen-reader smoke on the booking funnel) → staging verified with a screen reader → prod verify → monitored (nightly axe).
- **Dependencies:** blocked-by **VRB-106** for the shared focus-trap/`Sheet` primitive (reuse, don't duplicate). Touches components owned by VRB-101/103/107/108 — schedule **after** those land to avoid re-auditing churned code.
- **Parallelisation:** Lane **a11y** (runs late, integrative). Owns: `ConfirmActionModal.tsx` focus-trap, axe CI wiring, focus/contrast fixes across components (coordinate — this lane edits many files, so land it after feature lanes freeze).

---

### VRB-111 — Instant-Book (auto-capture) owner opt-in
- **Epic:** Launch Features · **Priority:** Should (post-launch per PRD §6) · **Estimate:** M
- **Narrative:** As a property owner, I want to opt a property into Instant-Book so bookings auto-confirm and capture without my manual approval, so that I convert more guests (Airbnb's default), while manual-approval remains the platform default.
- **Acceptance criteria (OPEN-QUESTIONS Q6 — manual capture is the launch default; this is the opt-in alternative):**
  - Given a property with Instant-Book enabled, When a guest places a booking with a valid payment, Then it goes straight to Confirmed and the payment is captured immediately (no Tentative window), and confirmation email is sent.
  - Given Instant-Book is disabled (default), When a guest books, Then the current manual-capture flow (Tentative → owner confirm → capture) is unchanged.
  - Given an iCal/availability conflict is detected at instant-book time, When placing, Then the booking is refused atomically (no capture) rather than auto-confirmed.
  - Given the owner toggles Instant-Book, When they save, Then it applies only to future bookings.
- **TDD plan:**
  - Unit: `BookingPlaceInstantBookTests.AutoConfirmsAndSkipsTentativeWindow`, `.RefusesOnConflict`; `PropertyInstantBookTests.ToggleDefaultsOff`.
  - Integration: `InstantBookFlowTests.PlaceCapturesImmediately` (Testcontainers + Stripe test mode) — asserts status Confirmed + captured PI.
  - E2E: `guest/instant-book.spec.ts` (guest books an instant-book property, sees immediate confirmation), `owner/enable-instant-book.spec.ts`.
- **Technical notes:** Add an `InstantBook` flag to `Property` (Catalog schema migration). In the place-booking flow, branch: instant-book calls `Confirm()` + capture inline instead of leaving `Tentative` with the 24h/48h `TentativeUntil` (`src/Modules/VrBook.Modules.Booking/Domain/Booking.cs:112-119,141-149`). Capture uses the existing `CapturePaymentIntentAsync` (`StripeGateway.cs:83-89`) — note the intent is created with `CaptureMethod="manual"` (`StripeGateway.cs:60`); instant-book captures immediately after auth rather than switching to automatic capture (keeps one code path, SAQ-A intact). Reuse the existing conflict/availability guard from the place flow. Interacts with VRB-102 (a non-refundable/refundable policy still applies).
- **UI/UX spec:** (1) **Owner** — a toggle on the property editor ("Instant Book — guests book without your approval") with an explainer. (2) **Guest** — property detail + search show an "⚡ Instant Book" badge; checkout CTA reads "Book instantly" and the confirmation is immediate (no "awaiting host" state). States: toggle saving/success/error; guest immediate-confirmation screen. a11y: toggle is a labelled switch with `aria-checked`; badge has text, not icon-only. Distinctive: the lightning badge uses brand-orange; immediate-confirm screen celebrates (subtle) vs the pending state.
- **Configuration:** `Features:InstantBook:Enabled` (platform kill-switch, default on). Per-property `InstantBook` is data. No new secret.
- **Rollout:** Flag `Features:InstantBook:Enabled` (default off prod until validated, on dev/staging). Migration: add `Property.InstantBook` (default false → existing properties keep manual capture). Backward-compatible. Rollback: flip flag → all properties behave as manual capture.
- **Observability:** Log instant-book placements (`property_id`, `booking_id`, `captured`); metric `booking.instant_book.count` + `booking.instant_book.conflict_refusals`; alert on capture-failure spike (instant-book captures synchronously, so failures are guest-visible).
- **Definition of Done:** tests green → review (conflict atomicity, no double-capture, policy interaction) → staging: enable on a test property, guest books instantly, PI captured → prod verify → monitored.
- **Dependencies:** none hard; interacts with **VRB-102** (policy still applies) and **VRB-104** (refunds on instant-booked stays reverse fees).
- **Parallelisation:** Lane **instant-book**. Owns: `Property` `InstantBook` field + Catalog migration, place-booking branch in Booking application layer, instant-book badge + owner toggle in web. Touches `Booking.cs` place path — coordinate with VRB-102 (different methods).

---

### VRB-112 — Promo codes / discounts
- **Epic:** Launch Features · **Priority:** Should · **Estimate:** M
- **Narrative:** As a property owner, I want to issue promo codes (percentage or fixed amount, with limits and expiry), so that I can run direct-channel marketing; and as a guest, I want to apply a code at checkout and see the discount, so that I'm rewarded for booking direct.
- **Acceptance criteria:**
  - Given an owner creates a promo code (percent or fixed, optional min-nights, usage cap, valid-from/to, per-property or tenant-wide), When saved, Then it is stored and enforceable.
  - Given a guest applies a valid code at quote/checkout, When applied, Then the discount is computed and reflected in the total (subtotal → discount → fees → tax → total), and the applied amount is captured on the booking's `Discount` field.
  - Given an invalid/expired/over-cap code, When applied, Then a clear inline error shows and the total is unchanged.
  - Given tax (VRB-103), When a discount applies, Then tax is computed on the **post-discount** taxable base (or per Stripe Tax rules), consistently.
  - Given a discounted booking is refunded, When resolving the refund (VRB-102), Then the refund is based on the actually-charged (discounted) total.
- **TDD plan:**
  - Unit: `PromoCodeTests.PercentAndFixedComputation`, `.RejectsExpired`, `.RejectsOverUsageCap`, `.EnforcesMinNights`; `QuoteWithPromoTests.DiscountAppliedBeforeTax`.
  - Integration: `PromoRedemptionTests.ApplyThenPlaceCapturesDiscountedTotal`, `.ConcurrentRedemptionRespectsCap` (serializable-txn, mirrors the booking race-safety pattern).
  - E2E: `guest/apply-promo-code.spec.ts` (apply valid → discount shows, invalid → error), `owner/create-promo-code.spec.ts`.
- **Technical notes:** New promo aggregate — cleanest in Pricing (owns the quote engine) or a small new surface; add `promo_codes` + `promo_redemptions` tables (migration). The `Booking.Discount` field already exists and is explicitly reserved for this (`src/Modules/VrBook.Modules.Booking/Domain/Booking.cs:36-37` — "Loyalty / promo discount captured at booking time. Wired in A4.1 + A8"). Wire discount into the quote assembly (Pricing) **before** tax (coordinate ordering with VRB-103). Enforce usage caps with the existing serializable-transaction + `SELECT FOR UPDATE` race-safety idiom used for booking holds. Owner CRUD via a new admin controller; guest apply via the quote endpoint (`POST /api/v1/properties/{id}/quotes`) accepting an optional code.
- **UI/UX spec:** (1) **Owner** — a promo-codes admin page (create/list/disable): code, type (%/fixed), value, limits, dates, scope; states loading/empty/success/error. (2) **Guest** — an "Add promo code" field in the checkout summary; states: idle, validating (spinner), applied (green line "Code SAVE10 −$X" with remove), error (inline "code expired"). a11y: apply field labelled, result in `aria-live`, remove is a labelled button. Reuse the `TextField` primitive (VRB-108). Distinctive: applied-code chip styled like a coupon (dashed border, brand-orange accent).
- **Configuration:** `Features:PromoCodes:Enabled` (default off prod, on dev/staging). `Promo:MaxPercent` (default 100), `Promo:CodePattern`. No secret.
- **Rollout:** Flag-gated. Migration: additive tables (no change to existing bookings). Backward-compatible (quote works without a code). Rollback: flip flag → promo field hidden, quote ignores codes.
- **Observability:** Log redemptions (`code`, `booking_id`, `discount_cents`); metric `promo.redemption.count` + `promo.discount.cents`; alert on redemption-cap race anomalies (same code redeemed beyond cap → bug).
- **Definition of Done:** tests green → review (cap race-safety, discount-before-tax ordering, refund basis) → staging: owner creates code, guest redeems, discounted total captured → prod verify → monitored.
- **Dependencies:** **blocked-by VRB-102** (refund basis) and **VRB-103** (taxable-base ordering — discount must slot into the quote pipeline before tax). Shares the quote pipeline with VRB-103 — sequence after it.
- **Parallelisation:** Lane **promo**. Owns: new Pricing `PromoCode`/`PromoRedemption` aggregates + migration, promo admin controller, quote endpoint optional-code param, owner promo page + guest apply field. Coordinates with VRB-103 on the quote assembly (land VRB-103 first).

---

### VRB-113 — Damage deposit / security auth-hold
- **Epic:** Launch Features · **Priority:** Should · **Estimate:** M
- **Narrative:** As a property owner, I want an optional refundable security/damage deposit held on the guest's card, so that I'm protected against incidentals without charging the guest upfront; and as a guest, I want the hold clearly disclosed and released after checkout.
- **Acceptance criteria:**
  - Given an owner sets a deposit amount on a property, When a guest books, Then the deposit is disclosed at checkout and a separate Stripe **auth-hold** (uncaptured PaymentIntent) is placed for the deposit amount alongside the booking payment.
  - Given a clean checkout, When the stay completes, Then the deposit hold is **released** (cancelled) automatically within the configured window.
  - Given damage, When the owner claims within the window, Then the owner can capture up to the held amount (partial or full), with an audit record; the remainder is released.
  - Given the auth-hold would expire (Stripe ~7-day auth limit), When it nears expiry, Then it is released (and optionally re-held per policy) — never silently lost.
  - Given the deposit hold fails, When booking, Then the guest is told and the booking does not proceed as "protected."
- **TDD plan:**
  - Unit: `DepositHoldTests.PlacesUncapturedHoldForDepositAmount`, `.ReleasesOnCleanCompletion`, `.CapturesUpToHeldAmountOnClaim`, `.RejectsOverClaim`.
  - Integration: `DepositLifecycleTests.HoldThenReleaseOnCompletion`, `.HoldThenPartialCaptureOnClaim` (Stripe test mode) — asserts PI states.
  - E2E: `guest/deposit-disclosed.spec.ts` (checkout shows the hold), `owner/claim-deposit.spec.ts` (owner captures a claim, guest sees the charge).
- **Technical notes:** Reuse the existing manual-capture machinery: a deposit is a second uncaptured PaymentIntent (`CreatePaymentIntentAsync` + `CaptureMethod="manual"`, `StripeGateway.cs:37-81`), released via `CancelPaymentIntentAsync` (`StripeGateway.cs:91-97`) on clean completion (hook into the completion sweep — `Booking.Complete()`, `Booking.cs:266-271`), or captured via `CapturePaymentIntentAsync` (`:83-89`) on a claim (up to held amount). Add a deposit amount to `Property` (Catalog migration) and a deposit-hold record in Payment (new small aggregate or a typed PaymentIntent). Respect Stripe's ~7-day auth-hold limit — a worker (extend the Booking completion worker) releases/expires holds. SAQ-A preserved (Elements auth). Interacts with the completion sweep timing and turnover-aware completion (OPS.M.16).
- **UI/UX spec:** (1) **Owner** — deposit-amount field on the property editor + a "Deposits" admin view to see active holds and file a claim (capture amount + reason). (2) **Guest** — checkout clearly states "A $X refundable hold will be placed and released after checkout" (not a charge); the "My trips" detail shows hold status (held / released / claimed). a11y: claim capture uses `ui/ConfirmActionModal` with the amount; hold-status uses text + icon. Distinctive: a "shield" motif for the protected-stay badge; hold-status uses a clear timeline (held → released).
- **Configuration:** `Features:DamageDeposit:Enabled` (default off prod, on dev/staging). `Deposit:ReleaseAfterHours` (default 24 post-completion), `Deposit:MaxAuthDays` (default 7). Per-property deposit amount is data. No new secret.
- **Rollout:** Flag-gated. Migration: additive `Property.DepositAmount` + deposit-hold table. Backward-compatible (no deposit = current flow). Rollback: flip flag → deposits skipped; release any outstanding holds first (runbook step).
- **Observability:** Log hold lifecycle (`booking_id`, `hold_pi`, `held_cents`, `released|captured`, `claim_cents`); metric `deposit.hold.active` gauge + `deposit.claim.count`; **alert on holds nearing the 7-day auth limit un-released** (money-correctness + guest trust); alert on release failures.
- **Definition of Done:** tests green → review (auth-expiry handling, over-claim guard, SAQ-A intact) → staging: place a deposit hold, complete clean → released; file a claim → captured → prod verify → monitored (no stuck holds).
- **Dependencies:** none hard; interacts with the completion sweep + turnover (OPS.M.16) and refund/MoR posture (VRB-105). Independent of VRB-102 (separate PI).
- **Parallelisation:** Lane **deposit**. Owns: `Property.DepositAmount` + Catalog migration, new Payment deposit-hold aggregate + migration, deposit hooks in the completion worker, owner deposit/claim UI + guest disclosure. Shares `StripeGateway` *methods* (reuses existing capture/cancel — no edits needed there), so no conflict with VRB-104/105.

---

## Cross-story implementation notes

- **Money lane ordering (hard):** VRB-105 → VRB-104 → VRB-103 (all touch the Stripe charge/refund/tax posture); VRB-102 consumes VRB-104's corrected refund path.
- **Quote-pipeline ordering (hard):** VRB-103 (tax) → VRB-112 (promo) — both slot into the Pricing quote assembly; discount-before-tax.
- **Shared web primitives:** VRB-101 (`Dropzone`/`ImageTile`), VRB-106 (`Sheet` + focus-trap), VRB-108 (`TextField`), VRB-110 (`Dialog`/focus-trap) all add to the currently-thin `web/src/components/ui/` (only `ConfirmActionModal` exists today). Assign one owner per primitive to avoid duplicate implementations; VRB-110 reuses VRB-106's focus-trap.
- **Flags default posture:** engine/money stories ship flag-off in prod until verified (VRB-102/103/111/112/113); pure replacements ship unflagged (VRB-101/104/105/106/107/108/109/110).
- **Every story ends monitored** per the DoD and the program rule (check CI green + staging verified before close-out).
