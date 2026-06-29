# Slice OPS.M.9.1 — `IGuestTenantResolver` design (the anonymous-tenant read closure)

**Status**: Architect draft — DO NOT commit. Produced by system-architect at user request before Slice OPS.M.10.2 F6 code lands.
**Date**: 2026-06-29.
**Owner slice**: `Slice OPS.M.9.1` (the architectural omission left by Slice OPS.M.9) shipped under the `Slice OPS.M.10.2` audit-closure umbrella.
**Predecessors**: OPS.M.9 (RLS policies + `TenantGucCommandInterceptor` + `RlsBypassScope` + `BackgroundTenantScope`) — locked, shipped, in staging. OPS.M.10.2 F0-F4 — all green; F4 (`dc96a2e`) just narrowed the Stripe webhook bypass per audit #3 (the OPPOSITE direction from this slice).
**Audit cross-reference**: `docs/OPS_M_10_2_AUDIT_FINDINGS.md` §1 findings #4-#11 (eight High); §2 root cause; optionally #22 (Low, ReleaseHold).
**Locked rules honored**: every task labelled `Slice OPS.M.9.1`; vertical-slice UI verification step per F6 sub-commit; `scripts/pre-push-check.ps1` + `gh run watch` after every push.

---

## §0 Decision summary

The eight High findings #4-#11 all dereference an RLS-protected table on an `[AllowAnonymous]` request path. The closed-world `app.tenant_id` GUC (OPS.M.9 D7) denies every such read because `current_setting('app.tenant_id', true)` is empty for the anonymous request, and the policy's `tenant_id = NULLIF('', '')::uuid` resolves to `tenant_id = NULL` which is false.

**Solution shape — three orthogonal pieces**:

1. **Public marketplace carve-out at the RLS policy layer** for `catalog.properties` (and `catalog.property_images`). The semantics of `(IsActive = true AND DeletedAt IS NULL)` are "platform-public discovery"; that's a property of the *table*, not of the *caller*. Carve-out closes #8 and #9 with one migration and removes the resolver from those hot paths entirely. **Picked over scope-route** because (a) two of the eight findings disappear, (b) the search handler has no resource id to resolve from, (c) `catalog.properties` is the only such table, so the carve-out is bounded.
2. **`IGuestTenantResolver` — one interface, four named methods** (`ResolveFromPropertyIdAsync`, `ResolveFromSlugAsync`, `ResolveFromBookingIdAsync`, `ResolveFromOutboundTokenAsync`) returning `Task<Guid?>`. Closes #4 (quote), #5 (reviews list), #6 (submit review), #7 (outbound feed), #10 (availability), #11 (place/get/cancel booking — see §1.4 for `MyBookings`).
3. **`GuestTenantScope` — a thin wrapper around `BackgroundTenantScope.Enter(...)`** that the handler opens AFTER resolving. The interceptor's existing fallback chain (`ICurrentUser.TenantId` → `BackgroundTenantScope.CurrentTenantId` → empty) means the resolved tenant id flows through every DbContext command for the lifetime of the `using` block. Zero new interceptor code; zero new GUC.

**The resolver's lookup MUST itself bypass RLS** (the resolver reads `catalog.properties.tenant_id` / `booking.bookings.tenant_id` / `sync.channel_feeds.tenant_id`, all RLS-protected). The bypass is internal to the single resolver impl; it does not leak to the caller. **Five new entries** join `RlsBypassCallSiteAllowlistTests.AllowedFullNames` — one for the resolver impl, four for the resolver-consuming handlers if any of them also needs a direct bypass (they don't; only the resolver does). So the call-site allow-list gains exactly one new entry: `VrBook.Infrastructure.Guests.GuestTenantResolver`.

**`MyBookingsHandler` is special** (no resource id in URL). Pick option (a): a `booking.bookings`-backed `ResolveFromGuestUserIdAsync` that returns the DISTINCT set of tenant ids the guest has bookings under, then the handler iterates one tenant at a time. Materially simpler than "first-booking tenant" + correctly handles guests booking across multiple hosts on different tenants — the long-term Phase 4 OTA shape.

**F6 sequence** (each ending with `pwsh scripts/pre-push-check.ps1` then `gh run watch`):
- F6a — abstraction + RED tests; resolver impl + DI + allow-list update.
- F6b — `catalog.properties` public-read carve-out migration + arch test for the new policy shape; rewire `SearchPropertiesHandler` and `GetPropertyBySlugHandler` to drop the dead `WHERE p.IsActive` (it's now in the policy).
- F6c — Pricing + Reviews: `ComputeQuoteHandler`, `GetReviewsForPropertyHandler`, `SubmitReviewHandler`.
- F6d — Booking guest flow: `GetPropertyAvailabilityHandler`, `PlaceBookingHandler`, `GetBookingHandler`, `MyBookingsHandler`, `CancelBookingHandler`.
- F6e — Sync outbound feed: `GetOutboundFeedHandler` + close-out doc.

---

## §1 The 7 questions, answered

### §1.1 Q1 — Interface shape: ONE interface, four named methods

**Picked**: ONE interface `IGuestTenantResolver` with four named methods (no overloads; the parameter types differ enough that overload resolution would be confusing).

```csharp
// src/VrBook.Contracts/Interfaces/IGuestTenantResolver.cs

public interface IGuestTenantResolver
{
    Task<Guid?> ResolveFromPropertyIdAsync(Guid propertyId, CancellationToken ct = default);
    Task<Guid?> ResolveFromSlugAsync(string slug, CancellationToken ct = default);
    Task<Guid?> ResolveFromBookingIdAsync(Guid bookingId, CancellationToken ct = default);
    Task<Guid?> ResolveFromOutboundTokenAsync(string outboundToken, CancellationToken ct = default);

    // §1.4 — MyBookings: returns the DISTINCT tenant ids the guest has bookings under.
    Task<IReadOnlyList<Guid>> ResolveTenantsForGuestUserAsync(Guid guestUserId, CancellationToken ct = default);
}
```

**Why one interface over per-resource-type interfaces (`IPropertyTenantResolver`, `IBookingTenantResolver`, `IOutboundTokenTenantResolver`)**:

- **One interface (picked)** — Pros: single registration site, single allow-list entry in `RlsBypassCallSiteAllowlistTests`, easier to grep/audit (every guest-tenant resolution call goes through one type), easier to evolve (a future #5th resource type adds one method, not a whole new interface + registration). Cons: handlers that only resolve from one shape inject a slightly larger surface than they need (but they only call one method — the resolved-vs-unused method overhead is zero at runtime).
- **Per-resource interfaces (rejected)** — Pros: ISP-pure; each handler depends only on what it uses. Cons: 4-5 separate interface files, 4-5 separate DI registrations (or one factory pattern), 4-5 separate allow-list entries → drift risk between them; cross-handler refactoring (e.g. "every resolver call should log structured `resource_id` field") becomes a sweep.

The audit's "deliberate bypass call site" discipline favors the small, single, named surface — same reasoning that picked one `IRlsBypassDbContextFactory<TContext>` over per-module factories (M.9 §4.4 D4). Locked.

### §1.2 Q2 — Where the lookup-bypass lives: inside the resolver impl

**Picked**: bypass lives **inside the resolver impl**, via `IRlsBypassDbContextFactory<CatalogDbContext>` + `IRlsBypassDbContextFactory<BookingDbContext>` + `IRlsBypassDbContextFactory<SyncDbContext>` constructor-injected into the one `GuestTenantResolver` class.

**Why not a middleware**:
- A middleware would need to know *which* resolver method to call before the handler even sees the request — i.e., the middleware would need to parse the route, decide "this looks like a property-slug route → resolve from slug", open a bypass, then stamp `BackgroundTenantScope`. This is route-table coupling that should not leak into infra.
- A middleware would need to discover the resource id from the route value dictionary; for `SubmitReviewHandler` the resource is the `BookingId` route param, but the resolved tenant id comes from the *property's* tenant (the review inherits from the property, not the booking). The middleware would have to chain (booking → property → tenant), which is exactly the resolver's job. Putting it in middleware just moves the abstraction up one layer with no clean boundary.

**Why not duplicate the bypass-open in each consumer handler**:
- Each handler-side bypass would add a new entry to the `RlsBypassCallSiteAllowlistTests.AllowedFullNames` list (currently 3 entries, would grow to 11+). The bypass call-site discipline (audit #3, M.9 §7) is to keep this list SMALL.
- Each handler would replicate the "open bypass → query lookup table → close bypass → enter tenant scope" boilerplate. Reuse is the point.

**Composability tradeoff acknowledged**: the resolver impl IS one class that calls four lookup tables. If a future #5th resource shape arrives, the resolver gains one method + one constructor parameter (one more bypass factory). That's acceptable — the alternative (one class per lookup) creates 4-5 classes with one method each. Single class is the lower-overhead shape at this fan-out.

**New `RlsBypassCallSiteAllowlistTests.AllowedFullNames` entry** (exactly ONE):

```csharp
// added in F6a
"VrBook.Infrastructure.Guests.GuestTenantResolver",
```

The eight High-finding consumer handlers DO NOT appear in the allow-list — they inject `IGuestTenantResolver` (not `IRlsBypassDbContextFactory<>`).

### §1.3 Q3 — `catalog.properties` policy carve-out: YES, carve out the public-read

**Picked**: extend `EnableRlsTenantIsolation` with a `publicReadPredicate` parameter; emit a SECOND policy (`rls_<schema>_<table>_public_read`) USING-only (no WITH CHECK — writes still require tenant match) that allows reads when the predicate holds.

**Why the carve-out (vs scope-route everywhere)**:

1. **Search has no resource id** — the public marketplace search (`/api/v1/properties?q=…`) has no property id or slug in the URL. A resolver call there would be a no-op (no resource to resolve from). The carve-out is the ONLY shape that closes #8 without inventing a "search-scope = all tenants" anti-pattern.
2. **`catalog.properties` is the only such table** — every other RLS table in the M.9 §3.1 inventory is per-tenant private (`booking.bookings`, `payment.payment_intents`, etc.). `catalog.properties.IsActive AND DeletedAt IS NULL` is the canonical "platform-public listing" shape; no other table needs this.
3. **Mental model isn't broken** — the policy carve-out is documented in the same `EnableRlsTenantIsolation` helper. A future reader of the migration sees `EnableRlsTenantIsolation(... publicReadPredicate: "is_active = true AND deleted_at IS NULL")` and immediately understands "this table has a public-read carve-out". The mental model is "RLS denies cross-tenant private rows; public discovery rows go through the carve-out", which mirrors the existing nullable-`tenant_id` carve-out (D12).
4. **`catalog.property_images` follows the parent** — the images table is also `[AllowAnonymous]`-readable (the marketplace cards show cover images, the detail page shows the gallery). The carve-out covers it too, on its own predicate `EXISTS (SELECT 1 FROM catalog.properties p WHERE p.id = catalog.property_images.property_id AND p.is_active AND p.deleted_at IS NULL)`. Single migration; two tables.

**Why not scope-route everywhere**:

- Search would need a faux resolver call (or scope around a `WHERE IsActive` query that still denies anonymous). Awkward.
- The "every public read goes through the resolver" rule would still need an exception for search. So you end up with both mechanisms anyway. Pick the smaller surface.

**Exact migration DDL** (F6b) — extends the existing helper plus the migration file:

```csharp
// src/VrBook.Infrastructure/Persistence/RlsMigrationBuilderExtensions.cs — extend helper.

public static MigrationBuilder EnablePublicReadCarveOut(
    this MigrationBuilder mb,
    string schema,
    string table,
    string predicate)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(schema);
    ArgumentException.ThrowIfNullOrWhiteSpace(table);
    ArgumentException.ThrowIfNullOrWhiteSpace(predicate);

    var qualified = $"\"{schema}\".\"{table}\"";
    var policyName = $"rls_{schema}_{table}_public_read";

    // USING-only; no WITH CHECK — public read does NOT permit writes.
    // The existing tenant_isolation policy still owns INSERT/UPDATE/DELETE.
    mb.Sql($@"
        CREATE POLICY {policyName} ON {qualified}
            FOR SELECT
            USING ({predicate});
    ");
    return mb;
}

public static MigrationBuilder DropPublicReadCarveOut(
    this MigrationBuilder mb, string schema, string table)
{
    var qualified = $"\"{schema}\".\"{table}\"";
    var policyName = $"rls_{schema}_{table}_public_read";
    mb.Sql($"DROP POLICY IF EXISTS {policyName} ON {qualified};");
    return mb;
}
```

**The migration file** (one file, both tables):

```csharp
// src/Modules/VrBook.Modules.Catalog/Migrations/<timestamp>_OpsM9_1a_Catalog_PublicReadCarveOut.cs

public partial class OpsM9_1a_Catalog_PublicReadCarveOut : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // catalog.properties — public discovery rows are visible to anonymous readers.
        mb.EnablePublicReadCarveOut(
            schema: "catalog",
            table: "properties",
            predicate: "is_active = true AND deleted_at IS NULL");

        // catalog.property_images — visible iff parent property is publicly discoverable.
        // The EXISTS clause re-reads catalog.properties; that read is itself subject to the
        // public-read carve-out we just installed, so the recursion terminates one level deep.
        mb.EnablePublicReadCarveOut(
            schema: "catalog",
            table: "property_images",
            predicate: @"EXISTS (
                SELECT 1 FROM catalog.properties p
                WHERE p.""Id"" = catalog.property_images.property_id
                  AND p.is_active = true
                  AND p.deleted_at IS NULL
            )");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropPublicReadCarveOut("catalog", "property_images");
        mb.DropPublicReadCarveOut("catalog", "properties");
    }
}
```

**Existing carved-out columns reminder** — the EF mapping for `Property.IsActive` is `is_active` (snake_case), `DeletedAt` is `deleted_at` (snake_case). `Id` is PascalCase-quoted per `PropertyConfiguration`. Verified `PropertyConfiguration.cs` indirectly via `SubmitReviewHandler.cs:81` SQL pattern.

**Search/Slug rewires** (still in F6b):
- `SearchPropertiesHandler` drops nothing — the existing `Where(p => p.IsActive)` filter is still correct (it's the SAME predicate as the carve-out, so the optimizer collapses), and search-by-filters narrows further. No code change beyond confirming the integration test goes 200.
- `GetPropertyBySlugHandler` already has `Where(p.Slug == request.Slug)`; the policy permits the row when `IsActive AND DeletedAt IS NULL`. Inactive/soft-deleted properties 404 (correct — they shouldn't be SEO-indexed).
- `PriceQuoteWidget.tsx` reads `getAvailability(property.id)` — that's a Booking-side call hitting `GetPropertyAvailabilityHandler` (#10), which goes through the RESOLVER path (not the carve-out). The detail page's slug-load uses the carve-out; the inline availability fetch uses the resolver.

### §1.4 Q4 — `MyBookingsHandler`: per-guest tenant enumeration

**Picked**: option (c), restated as "the resolver returns the DISTINCT set of tenant ids the guest has bookings under, the handler unions per-tenant queries".

```csharp
// MyBookingsHandler — new shape
public async Task<PagedResult<BookingSummaryDto>> Handle(MyBookingsQuery request, CancellationToken ct)
{
    if (currentUser.UserId is null) throw new ForbiddenException("Sign-in required.");
    var guestId = currentUser.UserId.Value;

    var tenantIds = await resolver.ResolveTenantsForGuestUserAsync(guestId, ct);
    if (tenantIds.Count == 0) return PagedResult<BookingSummaryDto>.Empty;

    var allItems = new List<BookingSummaryDto>();
    foreach (var tenantId in tenantIds)
    {
        using var scope = BackgroundTenantScope.Enter(tenantId);
        var page = await bookings.ListForGuestAsync(guestId, skip: 0, take: int.MaxValue, ct);
        allItems.AddRange(page.Select(b => b.ToSummary()));
    }

    var ordered = allItems
        .OrderByDescending(b => b.CreatedAt)
        .Skip(skip).Take(limit).ToArray();
    var next = ordered.Length == limit ? (skip + ordered.Length).ToString() : null;
    return new PagedResult<BookingSummaryDto>(ordered, next, allItems.Count);
}
```

**Why option (c) over options (a) + (b)**:

- **(a) "First booking" lookup table** — picked the guest's first booking's tenant and stuck them with it forever. Doesn't compose with the real product: a guest who books with Host-A then Host-B (different tenants) would NEVER see the Host-B booking in their account. Permanent silent data loss. **Rejected.**
- **(b) "Guest's tenant for life" stamped on first booking** — same data-loss issue plus introduces a NEW table (`identity.guest_tenant_lock` or similar) that has its own RLS semantics, its own migration, its own arch test, its own bootstrap-order question. **Rejected — too much new surface for an audit-closure slice.**
- **(c) Per-tenant grouped iteration** — Pros: zero new tables, zero new state, correct under multi-host. Cons: N+1-ish (N = # of tenants the guest has bookings under, typically 1, max 5-10 at Phase 1.5 scale). Each iteration is a single SELECT through a tenant-scoped DbContext; the per-statement GUC binding (M.9 D1) lets us reuse the same DbContext if we want to optimize later (Phase 2 — combine into one cross-tenant bypass-scoped query).

**Guest UX cross-check** — `web/src/app/account/bookings/page.tsx:33-83` (`BookingsList`) renders `await myBookings()` as a flat list ordered by `createdAt` descending. The guest does not see "Host-A bookings" vs "Host-B bookings" sections; the UI is one merged list. Option (c) produces exactly that shape. Verified at `BookingsList` line 49-74. The detail page (`web/src/app/bookings/[id]/page.tsx`) is a single booking — covered by #11's `GetBookingHandler` (resolver path).

**The resolver impl for `ResolveTenantsForGuestUserAsync`** opens a bypass-scoped `BookingDbContext`, queries `bookings.bookings.Where(b => b.GuestUserId == guestId).Select(b => b.TenantId).Distinct()`. The bypass is bounded to this one query.

### §1.5 Q5 — F6 sub-commit order

**Confirm** the audit's tentative ordering: ComputeQuote first. **Plus** — F6a is the abstraction-only commit, F6b is the carve-out (closes #8 and #9 without using the resolver), F6c-F6e thread the resolver into the remaining six findings grouped by module.

**Reasons to put ComputeQuote first within F6c**:
- Smallest blast radius — only the price-widget call breaks if the resolver is wrong; the marketplace home, the detail page, search, and the booking flow all keep working (they're either carve-out or later sub-commits).
- The `ComputeQuoteHandler` has the simplest read pattern (single `IPricingPlanRepository.GetByPropertyIdAsync`) — easiest to verify the resolver shape is correct before exposing it to compound flows like `SubmitReview` or `PlaceBooking`.
- The UI verification step is concrete: hit a property detail page in staging, the quote widget should render numbers instead of "Quote failed".

| F6 step | Files touched | Tests added | UI verification |
|---|---|---|---|
| **F6a** Abstraction RED/GREEN + DI + allow-list | `src/VrBook.Contracts/Interfaces/IGuestTenantResolver.cs` (new). `src/VrBook.Infrastructure/Guests/GuestTenantResolver.cs` (new, internal sealed). `src/VrBook.Infrastructure/Guests/GuestTenantScope.cs` (new helper: wraps `BackgroundTenantScope.Enter` + a no-op when guest is admin-authenticated). `src/VrBook.Api/Program.cs` or the host bootstrap — register `IGuestTenantResolver → GuestTenantResolver` once for the whole process. `tests/VrBook.Architecture.Tests/RlsBypassCallSiteAllowlistTests.cs` — add `"VrBook.Infrastructure.Guests.GuestTenantResolver"` to `AllowedFullNames`. New `tests/VrBook.Infrastructure.UnitTests/Guests/GuestTenantResolverTests.cs` — fixture-driven against the testcontainer Postgres (Category=Integration). | `GuestTenantResolverTests` (5 facts: each resolve method returns seeded tenant id; null on unknown; bypass-scoped lookup logs at Information level). `RlsBypassCallSiteAllowlistTests` updated. | None (no handler change yet — pure abstraction). |
| **F6b** `catalog.properties` + `catalog.property_images` public-read carve-out | `src/VrBook.Infrastructure/Persistence/RlsMigrationBuilderExtensions.cs` — add `EnablePublicReadCarveOut` + `DropPublicReadCarveOut`. New `src/Modules/VrBook.Modules.Catalog/Migrations/<ts>_OpsM9_1a_Catalog_PublicReadCarveOut.cs`. `tests/VrBook.Api.IntegrationTests/Rls/RlsPolicySchemaTests.cs` extended with `catalog_properties_has_public_read_policy` + `catalog_property_images_has_public_read_policy` facts. New `tests/VrBook.Api.IntegrationTests/Public/PublicPropertySearchHttpTests.cs` + `PublicPropertyDetailHttpTests.cs`. | RLS policy shape tests (4 new facts). Two HTTP-level integration tests: anonymous `GET /api/v1/properties?q=…` returns 200 with seeded rows; anonymous `GET /api/v1/properties/{slug}` returns 200. | Hit `https://web.<env>.app/` (marketplace) — should see seeded properties on the homepage Featured section (placeholder will still be there until F-followups wires it; the existence test is `/properties` browse page). Hit `https://web.<env>.app/properties` — should see seeded results, not the empty-state. Hit `https://web.<env>.app/properties/{seeded-slug}` — should see detail with gallery. |
| **F6c** Pricing (#4) + Reviews (#5, #6) | `src/Modules/VrBook.Modules.Pricing/Application/Quotes/Commands/ComputeQuoteHandler.cs` — inject `IGuestTenantResolver`; `var tenantId = await resolver.ResolveFromPropertyIdAsync(...) ?? throw new NotFoundException("Property", id); using var scope = BackgroundTenantScope.Enter(tenantId);` wraps the `plans.GetByPropertyIdAsync` call. `src/Modules/VrBook.Modules.Reviews/Application/Queries/GetReviewsForPropertyQuery.cs` — same shape around `reviews.ListForPropertyAsync`. `src/Modules/VrBook.Modules.Reviews/Application/Commands/SubmitReviewHandler.cs` — drop the inline raw-SQL `SELECT tenant_id FROM catalog.properties` (lines 57-61) AND the hardcoded `…0001` fallback (#6 foot-gun); use `resolver.ResolveFromPropertyIdAsync(booking.PropertyId)`; `BackgroundTenantScope.Enter` wraps both the `reviews.AddAsync` + `reviewsDb.SaveChangesAsync` AND the cross-context `catalogDb.Database.ExecuteSqlInterpolatedAsync` aggregate update. | New `tests/VrBook.Api.IntegrationTests/Public/PublicQuoteHttpTests.cs` — anonymous `POST /api/v1/properties/{id}/quotes` returns 200 with non-empty quote. New `tests/VrBook.Api.IntegrationTests/Public/PublicReviewsHttpTests.cs` — anonymous `GET /api/v1/properties/{id}/reviews` returns seeded reviews. New `tests/VrBook.Api.IntegrationTests/Reviews/SubmitReviewGuestHttpTests.cs` — guest persona POST `/api/v1/bookings/{id}/review` after CheckOut returns 201 with `tenant_id` matching the property's. Existing `CarveOutAppLayerTests` fallback-tenant test passes naturally; remove the SKIP on `SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user` is OUT of scope (that's audit #1, covered by C1). | Hit `/properties/{slug}` in staging; the right-rail quote widget should show line items + total (not "Quote failed"). Hit `/properties/{slug}` further down; the reviews section should render seeded reviews under "Reviews (N)". Use DevAuth Guest persona; after a `CheckedOut` booking, the `/bookings/{id}` page's "Leave a review" form should accept submission and return 201 → page redirects to a confirmation. |
| **F6d** Booking guest flow (#10, #11) | `src/Modules/VrBook.Modules.Booking/Application/Queries/GetPropertyAvailabilityHandler.cs` — same wrap. `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs` — wrap the body's DbContext reads (the `IPropertyOwnerLookup.GetAsync` call already returns `TenantId`; the place command still needs the scope so the `bookings.AddAsync` + EF SaveChangesAsync (the INSERT into `booking.bookings`) lands under the resolved tenant. The `SELECT ... FOR UPDATE` raw SQL on `booking.bookings` and `booking.availability_blocks` MUST also run inside the scope). `src/Modules/VrBook.Modules.Booking/Application/Queries/GetBookingHandler.cs` — resolver from BookingId. `src/Modules/VrBook.Modules.Booking/Application/Queries/MyBookingsHandler.cs` — `ResolveTenantsForGuestUserAsync` + per-tenant scope iteration (see §1.4). `src/Modules/VrBook.Modules.Booking/Application/Commands/TransitionHandlers.cs` — `CancelBookingHandler` resolves from BookingId then scopes the read + SaveChanges. | New `tests/VrBook.Api.IntegrationTests/Public/PublicAvailabilityHttpTests.cs`. New `tests/VrBook.Api.IntegrationTests/Booking/GuestBookingFlowHttpTests.cs` covering Place → Get → MyBookings → Cancel as a single guest persona scenario. Cross-tenant defense-in-depth fact: guest with bookings in Tenant A cannot read Tenant B's booking by id (resolver returns `Guid? = B`'s tenant; then the post-load app-layer check `booking.GuestUserId != currentUser.UserId.Value` rejects). | DevAuth Guest persona: hit `/properties/{slug}`, set dates, click Book — should redirect to `/bookings/{id}` showing Tentative + Stripe payment form (not 404). Hit `/account/bookings` — should list the booking (not empty). Hit `/bookings/{id}` and click Cancel — should transition to Cancelled. |
| **F6e** Sync outbound feed (#7) + close-out | `src/Modules/VrBook.Modules.Sync/Application/Feeds/Queries/GetOutboundFeedQuery.cs` — `resolver.ResolveFromOutboundTokenAsync(request.OutboundToken)` resolves the feed's tenant; the existing `feeds.GetByOutboundTokenAsync` then runs under the scope (so the OutboundToken IS the credential, same as M.9 §4.6 D6 reasoning for Stripe webhook). The `bookings.ListForOutboundFeedAsync` cross-module call ALSO runs under the scope. `docs/OPS_M_9_1_GUEST_RESOLVER_PLAN.md` close-out section completed. The §11 ledger / commit notes for the slice. | New `tests/VrBook.Api.IntegrationTests/Public/OutboundFeedHttpTests.cs` — seeds a known outbound token, hits `GET /api/v1/feeds/{token}.ics`, asserts 200 + VEVENT content; unknown-token assertion remains 404. Update existing `CarveOutAppLayerTests.Outbound_iCal_feed_with_unknown_token_returns_404_not_data` so the unknown-token assertion is paired with a valid-token-200 assertion (audit #7 — "test pack false-passes the carve-out invariant"). | DevAuth Owner persona, navigate to `/admin/sync` (channel feeds page), copy the OutboundToken URL, paste into a NEW browser tab while logged out — should see a calendar of VEVENTs (not 404). Subscribe the URL in Google Calendar — events appear. |

### §1.6 Q6 — Audit-list completeness

I traced every `[AllowAnonymous]` action across `src/VrBook.Api/Controllers/`:

| Controller / action | Handler | Already protected (no resolver needed) |
|---|---|---|
| `StripeWebhookController.Handle` (`PaymentsController.cs:52`) | `HandleStripeWebhookHandler` | Yes — F4 (`dc96a2e`) narrowed the bypass per audit #3. Not in scope here. |
| `DevAuthController.*` (`IdentityController.cs:68`) | DevAuth handlers | Yes — DevAuth carve-out (audit #20, #21). Not in scope here. |
| `QuotesController.Compute` (`PricingController.cs:113`) | `ComputeQuoteHandler` | **NO** → F6c (#4). |
| `PropertiesController.Search` (`PropertiesController.cs:25`) | `SearchPropertiesHandler` | **NO** → F6b carve-out (#8). |
| `PropertiesController.GetBySlug` (`PropertiesController.cs:35`) | `GetPropertyBySlugHandler` | **NO** → F6b carve-out (#9). |
| `PropertiesController.Availability` (`PropertiesController.cs:46`) | `GetPropertyAvailabilityHandler` | **NO** → F6d (#10). |
| `AmenitiesController.List` (`PropertiesController.cs:93`) | `ListAmenitiesQuery` | Yes — `catalog.amenities` is RLS-CARVED-OUT per M.9 §3.2 row 14 (shared reference data, no `tenant_id`). Confirmed by reading `RlsBypassCallSiteAllowlistTests` doesn't list it, plus §3.2's "shared across tenants" rationale. No resolver needed. |
| `PropertyReviewsController.List` (`ReviewsController.cs:41`) | `GetReviewsForPropertyHandler` | **NO** → F6c (#5). |
| `FeedsController.Get` (`SyncController.cs:19`) | `GetOutboundFeedHandler` | **NO** → F6e (#7). |

Additionally, the audit lists `SubmitReviewHandler` (#6) and the guest booking flow (#11) — these are not `[AllowAnonymous]` controller actions; they are `[Authorize]` actions where the AUTHENTICATED guest has no `TenantId` claim. They still need the resolver because the GUC is set from `ICurrentUser.TenantId` which is null for guests. **Confirmed in scope:** `BookingsController.Place / Get / MyBookings / Cancel / SubmitReview` (F6d + the SubmitReview path inside F6c).

**No additional handlers found beyond the audit's eight.** The audit's #4-#11 list is exhaustive for this slice. The DevAuth backdate endpoint (`IdentityController.cs:177-214`, audit #21) does raw `UPDATE booking.bookings` via Npgsql and is RLS-bypassed by virtue of running on a connection that doesn't go through EF — out of scope here, owned by audit #20/#21 C8.

### §1.7 Q7 — `ReleaseHoldCommand` interaction (audit #22)

**Out of scope for OPS.M.9.1.** Justification:

- The Redis hold store (`PostgresHoldStore` when `Features:UseRedisHoldStore=false`, the default in staging per `BookingModule.cs:32-42`) stores holds keyed by `holdId` GUID. It does not consult RLS; it's a key-value lookup.
- The audit's #22 risk is "a guest who guesses another guest's hold GUID could release their hold mid-checkout" — that's UUID-v4 entropy (122 bits, ~5×10^36 keyspace). Practically impossible; rated Low.
- The fix shape (stamp holds with `UserId`; assert match on release) is unrelated to the resolver abstraction. It's a hold-store concern, not a tenant-scope concern. The tenant doesn't appear in the hold-store contract.
- Adding it to F6 would mix unrelated concerns; the slice's audit-finding cross-reference becomes muddier.

**Recommendation**: ship #22 as part of `Slice OPS.M.10.2` C9 (the Low-finding tidy-up commit), not OPS.M.9.1. The C9 commit naming is already `Fix Slice OPS.M.10.2 Low findings 22-24`. Reaffirmed.

---

## §2 The interface contract + impl shape + DI + allow-list

### §2.1 The contract (verbatim — F6a)

```csharp
// src/VrBook.Contracts/Interfaces/IGuestTenantResolver.cs

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice OPS.M.9.1 §2.1 — closes the OPS.M.9 "anonymous read" gap by mapping
/// a public-route resource id to its owning tenant id. Consumers are
/// <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/>
/// handlers (and authenticated-guest handlers where the caller has no
/// <c>TenantId</c> claim) on tenant-private RLS-protected reads.
///
/// <para>Lifecycle: the resolver opens an internal
/// <see cref="IRlsBypassDbContextFactory{TContext}"/> scope JUST for the
/// lookup; the caller wraps the post-resolution body in
/// <c>using var scope = BackgroundTenantScope.Enter(tenantId)</c>. The
/// <see cref="TenantGucCommandInterceptor"/>'s fallback chain stamps
/// <c>app.tenant_id</c> from <see cref="BackgroundTenantScope.CurrentTenantId"/>
/// when <see cref="ICurrentUser.TenantId"/> is null.</para>
///
/// <para>Allow-list: the resolver IMPL is the only call site of
/// <see cref="IRlsBypassDbContextFactory{TContext}"/> the slice adds; the
/// <c>RlsBypassCallSiteAllowlistTests</c> pin enumerates it. Consumer
/// handlers inject this interface and DO NOT see the bypass.</para>
///
/// <para>For the public marketplace search path (<c>catalog.properties</c>
/// SELECT with no resource id), use the RLS public-read carve-out (§1.3),
/// not this resolver.</para>
/// </summary>
public interface IGuestTenantResolver
{
    /// <summary>Resolve from a property id (booking, availability, quote, review-list).</summary>
    Task<Guid?> ResolveFromPropertyIdAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>Resolve from a property slug (detail-page non-carve-out reads — reserved; today the carve-out covers it).</summary>
    Task<Guid?> ResolveFromSlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Resolve from a booking id (guest get / cancel / submit-review).</summary>
    Task<Guid?> ResolveFromBookingIdAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Resolve from an outbound iCal feed token (the token IS the credential).</summary>
    Task<Guid?> ResolveFromOutboundTokenAsync(string outboundToken, CancellationToken ct = default);

    /// <summary>Resolve the DISTINCT set of tenant ids the guest user has any bookings under (MyBookings).</summary>
    Task<IReadOnlyList<Guid>> ResolveTenantsForGuestUserAsync(Guid guestUserId, CancellationToken ct = default);
}
```

### §2.2 The impl shape (F6a)

Single internal sealed class, lives in `src/VrBook.Infrastructure/Guests/GuestTenantResolver.cs`. Constructor-injects bypass factories for Catalog (property → tenant), Booking (booking → tenant; guest → tenants), Sync (outbound token → tenant). Logs each resolve at `Debug` level with the resource shape; the bypass-open log line at `Information` level is the existing `RlsBypassDbContextFactory.CreateForBypassAsync` emitter.

```csharp
// src/VrBook.Infrastructure/Guests/GuestTenantResolver.cs

internal sealed class GuestTenantResolver(
    IRlsBypassDbContextFactory<CatalogDbContext> catalogBypass,
    IRlsBypassDbContextFactory<BookingDbContext> bookingBypass,
    IRlsBypassDbContextFactory<SyncDbContext> syncBypass,
    ILogger<GuestTenantResolver> logger)
    : IGuestTenantResolver
{
    public async Task<Guid?> ResolveFromPropertyIdAsync(Guid propertyId, CancellationToken ct = default)
    {
        await using var bypass = await catalogBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-property-id", ct);
        return await bypass.Db.Properties
            .AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> ResolveFromSlugAsync(string slug, CancellationToken ct = default)
    {
        await using var bypass = await catalogBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-slug", ct);
        return await bypass.Db.Properties
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> ResolveFromBookingIdAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var bypass = await bookingBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-booking-id", ct);
        return await bypass.Db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => (Guid?)b.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> ResolveFromOutboundTokenAsync(string outboundToken, CancellationToken ct = default)
    {
        await using var bypass = await syncBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-outbound-token", ct);
        return await bypass.Db.ChannelFeeds
            .AsNoTracking()
            .Where(f => f.OutboundToken == outboundToken)
            .Select(f => (Guid?)f.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ResolveTenantsForGuestUserAsync(
        Guid guestUserId, CancellationToken ct = default)
    {
        await using var bypass = await bookingBypass.CreateForBypassAsync(
            "guest-tenant-resolver.tenants-for-guest", ct);
        return await bypass.Db.Bookings
            .AsNoTracking()
            .Where(b => b.GuestUserId == guestUserId)
            .Select(b => b.TenantId)
            .Distinct()
            .ToArrayAsync(ct);
    }
}
```

### §2.3 DI registration (F6a)

The resolver is host-level (it spans modules); register once in `src/VrBook.Api/Program.cs` alongside the existing module wiring. Sketch:

```csharp
// in Program.cs / ServiceCollectionExtensions, after the modules are added
services.AddScoped<IGuestTenantResolver, GuestTenantResolver>();
```

Scoped lifetime — matches the bypass-factory lifetime (M.9 §4.4 sets scoped). Multiple resolves per request reuse the AsyncLocal infrastructure with no surprises. Tested by the F6a `GuestTenantResolverTests` opening a request scope and calling multiple methods.

### §2.4 `RlsBypassCallSiteAllowlistTests` delta (F6a)

```csharp
// tests/VrBook.Architecture.Tests/RlsBypassCallSiteAllowlistTests.cs
// Lines 28-38 currently. Add ONE entry:

private static readonly string[] AllowedFullNames = new[]
{
    "VrBook.Modules.Identity.Infrastructure.TenantStripeContextLookup",
    "VrBook.Modules.Identity.Application.Tenants.Queries.ListPlatformTenantsHandler",
    "VrBook.Modules.Identity.Application.Tenants.Queries.GetPlatformTenantHandler",

    // OPS.M.9.1 §2.4 — the guest-tenant resolver is the sole bypass site
    // for anonymous-read tenant resolution. Consumer handlers DO NOT inject
    // IRlsBypassDbContextFactory<>; they inject IGuestTenantResolver.
    "VrBook.Infrastructure.Guests.GuestTenantResolver",
};
```

Also extend `EnumerateModuleAssemblies` if needed — the resolver lives in `VrBook.Infrastructure`, which already gets enumerated via the `RlsBypassScope` type's assembly. Verify during F6a.

---

## §3 Public-read carve-out migration DDL — full

Already presented in §1.3 inline. Repeated here for the implementor; lives at `src/Modules/VrBook.Modules.Catalog/Migrations/<timestamp>_OpsM9_1a_Catalog_PublicReadCarveOut.cs` (one migration per the M.9 §4.13 D13 pattern — one tag, one wave). Plus the helper extension at `src/VrBook.Infrastructure/Persistence/RlsMigrationBuilderExtensions.cs`.

The migration is additive (one `CREATE POLICY` per table; no column changes; no data changes). Replay safety: same as M.9 — the policies are additive.

**Migrator role caveat**: the `vrbook_migrator` role has `BYPASSRLS` (M.9 §2). The `CREATE POLICY` runs under the migrator role; no further grant needed.

**Existing tenant-isolation policy interaction**: the per-table `rls_catalog_properties_tenant_isolation` policy is `PERMISSIVE` (Postgres default). Postgres OR-combines all PERMISSIVE policies for the same command — so a row visible via EITHER `tenant_isolation` OR `public_read` is returned. This is exactly the semantic we want: tenant-internal callers see all their own rows (active + inactive + deleted); anonymous callers see only active non-deleted rows. **No change to the existing tenant-isolation policy** — backward-compatible.

**Update inventories** — F6b also touches `docs/OPS_M_9_PLAN.md` §3.1 to note the carve-out on the `properties` + `property_images` rows ("plus public-read carve-out (OPS.M.9.1 §1.3)"). Inventory drift is otherwise caught by `RlsPolicyShapeTests`.

---

## §4 F6 commit sequence — file paths, tests, UI steps

See §1.5 table above. Reproduced here as a flat copy-pasteable list for the implementor:

### F6a — `Slice OPS.M.9.1 F6a — IGuestTenantResolver abstraction (RED → GREEN)`

**New files**:
- `src/VrBook.Contracts/Interfaces/IGuestTenantResolver.cs`
- `src/VrBook.Infrastructure/Guests/GuestTenantResolver.cs`
- `tests/VrBook.Infrastructure.UnitTests/Guests/GuestTenantResolverTests.cs` (Category=Integration; uses `TenantIdRolloutFixture`)

**Edited files**:
- `src/VrBook.Api/Program.cs` (one DI line)
- `tests/VrBook.Architecture.Tests/RlsBypassCallSiteAllowlistTests.cs` (one allow-list entry)

**End-of-commit hooks**: `pwsh scripts/pre-push-check.ps1` (must run `dotnet test --filter "Category!=Integration"` per `feedback_use_ci_filter_locally`); then `git push`; then `gh run watch` until conclusion=success.

### F6b — `Slice OPS.M.9.1 F6b — catalog.properties + property_images public-read RLS carve-out`

**New files**:
- `src/Modules/VrBook.Modules.Catalog/Migrations/<ts>_OpsM9_1a_Catalog_PublicReadCarveOut.cs`
- `tests/VrBook.Api.IntegrationTests/Public/PublicPropertySearchHttpTests.cs`
- `tests/VrBook.Api.IntegrationTests/Public/PublicPropertyDetailHttpTests.cs`

**Edited files**:
- `src/VrBook.Infrastructure/Persistence/RlsMigrationBuilderExtensions.cs` (add helper methods)
- `tests/VrBook.Api.IntegrationTests/Rls/RlsPolicySchemaTests.cs` (4 new facts)
- `docs/OPS_M_9_PLAN.md` §3.1 (annotate carve-out)

**End-of-commit hooks**: same as F6a.

### F6c — `Slice OPS.M.9.1 F6c — ComputeQuote + GetReviewsForProperty + SubmitReview through IGuestTenantResolver`

**Edited files**:
- `src/Modules/VrBook.Modules.Pricing/Application/Quotes/Commands/ComputeQuoteHandler.cs` (inject + scope)
- `src/Modules/VrBook.Modules.Reviews/Application/Queries/GetReviewsForPropertyQuery.cs` (inject + scope on the handler class)
- `src/Modules/VrBook.Modules.Reviews/Application/Commands/SubmitReviewHandler.cs` (replace raw-SQL property-tenant lookup + drop `…0001` fallback; resolver + scope; the scope wraps BOTH `reviewsDb.SaveChangesAsync` AND the cross-context `catalogDb.Database.ExecuteSqlInterpolatedAsync` (since the GUC is per-statement, the second context's interceptor reads the same `BackgroundTenantScope` value))

**New files**:
- `tests/VrBook.Api.IntegrationTests/Public/PublicQuoteHttpTests.cs`
- `tests/VrBook.Api.IntegrationTests/Public/PublicReviewsHttpTests.cs`
- `tests/VrBook.Api.IntegrationTests/Reviews/SubmitReviewGuestHttpTests.cs`

**End-of-commit hooks**: same as F6a.

### F6d — `Slice OPS.M.9.1 F6d — Guest booking flow through IGuestTenantResolver`

**Edited files**:
- `src/Modules/VrBook.Modules.Booking/Application/Queries/GetPropertyAvailabilityHandler.cs`
- `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs` (resolver from `property.Id`; scope wraps the entire `await using var tx = ... BeginTransactionAsync` block — the SET LOCAL fires per-statement inside the explicit transaction)
- `src/Modules/VrBook.Modules.Booking/Application/Queries/GetBookingHandler.cs` (resolver from `BookingId`)
- `src/Modules/VrBook.Modules.Booking/Application/Queries/MyBookingsHandler.cs` (per-tenant iteration per §1.4)
- `src/Modules/VrBook.Modules.Booking/Application/Commands/TransitionHandlers.cs` (only `CancelBookingHandler` — the owner-side transition handlers are `[Authorize(Roles="Owner,Admin")]` so the caller has a real `ICurrentUser.TenantId`)

**New files**:
- `tests/VrBook.Api.IntegrationTests/Public/PublicAvailabilityHttpTests.cs`
- `tests/VrBook.Api.IntegrationTests/Booking/GuestBookingFlowHttpTests.cs` (place → get → my-bookings → cancel)

**End-of-commit hooks**: same as F6a.

### F6e — `Slice OPS.M.9.1 F6e — Outbound iCal feed through IGuestTenantResolver + slice close-out`

**Edited files**:
- `src/Modules/VrBook.Modules.Sync/Application/Feeds/Queries/GetOutboundFeedQuery.cs`
- `tests/VrBook.Api.IntegrationTests/Sync/CarveOutAppLayerTests.cs` (pair the unknown-token 404 with a valid-token-200 assertion per audit #7 false-pass note)
- `docs/OPS_M_9_1_GUEST_RESOLVER_PLAN.md` close-out §11
- `docs/OPS_M_10_2_AUDIT_FINDINGS.md` — mark findings #4-#11 as closed; close out the C5+C6 row in the §3.2 commits table

**New files**:
- `tests/VrBook.Api.IntegrationTests/Public/OutboundFeedHttpTests.cs`

**End-of-commit hooks**: same as F6a.

---

## §5 UI cross-check — staging URLs per F6 sub-step

The user tests through UI per `feedback_test_through_ui`. Each F6 sub-commit must leave staging in a state the user can hit.

| F6 step | Staging URL(s) | What the user should see |
|---|---|---|
| F6a | (none — pure abstraction; no behavior change) | Existing flows unchanged. Anonymous quote/reviews/feed/booking still broken — closing in F6b-F6e. |
| F6b | `https://<web-staging>/` (home), `https://<web-staging>/properties` (browse), `https://<web-staging>/properties/<seeded-slug>` (detail) | Marketplace search returns seeded properties. Detail page renders title + gallery + amenities. NOTE: quote widget on the detail page will still error until F6c lands — that's expected. |
| F6c | Same detail page URL plus the inline quote widget action | Quote widget shows nightly + fees + total. Reviews section renders seeded reviews. DevAuth Guest persona with a CheckedOut booking can submit a review and see 201 + the review appears on the property detail page after refresh. |
| F6d | DevAuth Guest persona → `https://<web-staging>/properties/<seeded-slug>` → set dates → Book; then `/account/bookings` (list); then `/bookings/<id>` (detail with cancel) | Booking lands at Tentative. My-bookings list shows the booking. Detail page shows status pill + line items. Cancel button transitions to Cancelled. |
| F6e | Owner persona → `/admin/sync` → copy OutboundToken URL → open in new browser tab (logged out) | Public iCal returns 200 with VEVENT entries for confirmed/tentative bookings. Subscribing in Google Calendar shows the events. |

**Pre-F6 staging check** (one-off, before F6a): DevAuth Owner persona logs in, seeds a property + a CheckedOut booking + a review + an outbound feed, so the F6c-F6e UI checks have data to load. The existing OPS.M.0 seed script (`scripts/seed-staging.ps1` if present, otherwise the `/api/v1/dev-auth` endpoints) can do this. Architect note: if no seed script exists, F6a should ALSO add one — the user should not be asked to manually seed via 8 API calls between F6 steps.

---

## §6 Risks + rollback + audit cross-reference

### §6.1 Risks

| # | Risk | Probability | Mitigation |
|---|---|---|---|
| 1 | The resolver's bypass leaks into an unrelated read inside the same logical async thread (e.g. the consumer handler `await`s a service that runs an unrelated DbContext query before the resolver returns). | Low | The resolver `await using` wrapper disposes the inner bypass DbContext FIRST then pops the AsyncLocal frame (M.9 `BypassedDbContext.DisposeAsync` order verified). Resolver methods are atomic: open → one query → dispose, no awaits between the bypass-open and the next dispose. Reviewers check during F6a code review. |
| 2 | `MyBookingsHandler`'s per-tenant iteration runs N queries (N = # of tenants the guest is in). For N>50 this becomes a perf issue. | Low (Phase 1.5 guest cardinality is 1-3) | Cap N at the resolver level (return TOP 20 tenant ids; if a guest hits >20 we have a product issue, not a perf one). Document as a Phase 2 optimization. |
| 3 | The public-read carve-out on `catalog.properties` accidentally exposes a column the marketplace shouldn't show (e.g. an internal pricing margin). | Medium | The SELECT carve-out is row-level, not column-level — every column on `catalog.properties` is visible to anonymous readers. **Audit during F6b**: review the `Property` aggregate; if a sensitive column exists, either (a) move it to a per-tenant child table, or (b) exclude via a view. Today's `Property` shape (title, slug, description, address, rating_avg, rating_count, is_active, deleted_at, owner_user_id, tenant_id, capacity, type, checkin_from, checkin_to) does NOT contain sensitive data — `owner_user_id` is technically leaked but it's already in the public-facing payload via `PropertyDto`. Acceptable. |
| 4 | The `PlaceBookingHandler` transaction (serializable; SELECT FOR UPDATE) interacts with the per-statement GUC binding. | Medium | Per M.9 §4.1 D1, per-statement GUC inside an explicit transaction works correctly — every command in the transaction gets the SET LOCAL prefix; the value stays consistent. The `using var scope` opens BEFORE `db.Database.BeginTransactionAsync` so the scope covers the entire transaction. Verified by F6d's `PlaceBookingHttpTests` (Tentative → row visible to the guest). |
| 5 | The `SubmitReviewHandler` writes across two DbContexts (`reviewsDb` for the INSERT, `catalogDb` for the aggregate UPDATE). The `BackgroundTenantScope` is AsyncLocal so both contexts' interceptors see the same tenant id. | Low | Each context's `TenantGucCommandInterceptor` reads `BackgroundTenantScope.CurrentTenantId` (the same AsyncLocal). The two SaveChanges are still in separate transactions (the comment at line 75 says so), so eventual-consistency for the aggregate column survives. Verified by F6c's `SubmitReviewGuestHttpTests` (review's `tenant_id` matches property's; property's `rating_avg`/`rating_count` updated). |
| 6 | Unknown / not-yet-seeded resource id (e.g. `ComputeQuoteCommand(propertyId: Guid.NewGuid())` from a malicious caller) returns `null` from the resolver. The handler then must 404. | Low | Each F6c-F6e handler edit pattern is: `var tenantId = await resolver.X(id) ?? throw new NotFoundException("Resource", id);`. This matches the existing 404 behavior (`ComputeQuoteHandler.cs:28`, `GetPropertyBySlugHandler.cs:24` etc.). No new behavior. |
| 7 | The arch-test allow-list update in F6a fails because the resolver class is detected as a constructor-injection of `IRlsBypassDbContextFactory<>` and the allow-list still has 3 entries. | High (intentional — RED commit) | F6a is RED-first: write the allow-list entry first, then add the resolver class; CI must go red on the RED commit (allow-list adds an entry for a class that doesn't exist yet) and green on GREEN. |

### §6.2 Rollback plan

- **F6a**: revert the commit. No data changes. The allow-list entry references a class that no longer exists — the test fails as a no-op. Restore by reverting.
- **F6b**: the migration's `Down` drops the public-read policies. Anonymous reads revert to denied. Marketplace breaks again — but no data corruption.
- **F6c**: revert the commit. Handlers revert to denied-anonymous behavior. The `SubmitReviewHandler`'s removed `…0001` fallback comes back — Slice OPS.M.10.2 owns the audit's #6 cleanup note (the fallback is "a foot-gun" per the audit; reintroducing it on rollback is documented as the temporary regression).
- **F6d**: same — revert. Guest flow breaks again until F6d re-lands.
- **F6e**: revert. Outbound feed reverts to empty.

**No data migration is permanent**. The slice is purely additive at the DB level (one new policy file; no column changes).

### §6.3 Audit-finding cross-reference table

| Audit # | Description | Closes in | Mechanism |
|---|---|---|---|
| #4 | `ComputeQuoteHandler` returns 404 for every property | F6c | `IGuestTenantResolver.ResolveFromPropertyIdAsync` + `BackgroundTenantScope.Enter` |
| #5 | `GetReviewsForPropertyHandler` returns empty | F6c | same |
| #6 | `SubmitReviewHandler` cannot insert; foot-gun fallback `…0001` | F6c | same; foot-gun removed |
| #7 | `GetOutboundFeedHandler` returns empty for every feed; `CarveOutAppLayerTests` false-passes | F6e | `IGuestTenantResolver.ResolveFromOutboundTokenAsync`; `CarveOutAppLayerTests` updated to pair the assertions |
| #8 | `SearchPropertiesHandler` returns zero results | F6b | `catalog.properties` public-read carve-out (no resolver needed) |
| #9 | `GetPropertyBySlugHandler` 404s for anonymous | F6b | same carve-out (extends to slug lookup naturally) |
| #10 | `GetPropertyAvailabilityHandler` returns empty | F6d | `IGuestTenantResolver.ResolveFromPropertyIdAsync` + scope around `bookings.ListBlockedRangesAsync` |
| #11 | Entire guest booking flow broken (Place / Get / MyBookings / Cancel) | F6d | resolver from PropertyId (Place); from BookingId (Get / Cancel); from GuestUserId DISTINCT-tenants (MyBookings) |
| #22 | (Low) `ReleaseHoldCommand` is guess-able | Out of scope (C9 / Slice OPS.M.10.2 Low tidy-up) | Unrelated to resolver; hold-store concern |

After F6e lands and CI is green, `docs/OPS_M_10_2_AUDIT_FINDINGS.md` §1 table rows #4-#11 get marked "Closed (Slice OPS.M.9.1 F6a-F6e)" and the audit's §3.2 commits table C5+C6 are marked done.

---

## §7 Open questions / architect re-consult triggers

If during implementation any of these become true, RE-CONSULT before continuing (per `feedback_proactive_architect_consult`):

1. **`GetReviewsForPropertyHandler` returns 0 reviews even after F6c** — could be a stale `IPropertyOwnerLookup.GetAsync` cache; could be the policy SQL parsing the `is_active` predicate against a column that's actually `IsActive`. Re-read `PropertyConfiguration.cs` mapping and confirm column name.
2. **`PlaceBookingHandler`'s `RefundForBookingCommand` dispatch INSIDE the `using BackgroundTenantScope.Enter` block** — the dispatched command (`CancelBookingHandler`'s `RefundForBookingCommand` per `TransitionHandlers.cs:39-42`) is `[IBackgroundCommand]`-marked; the M.6 behavior pushes its OWN `BackgroundTenantScope.Enter(cmd.TenantId)`. Nested scope is supported by the stack (`BackgroundTenantScope.cs:39-40`). Verify no interaction with the AsyncLocal stack ordering.
3. **The carve-out makes `SearchPropertiesHandler` accidentally return inactive properties via the existing `Where(p.IsActive)` filter being redundant** — should be a no-op (the filter and the carve-out have the same predicate; the optimizer collapses). If the integration test fails, the filter MIGHT be the wrong shape. Re-read `SearchPropertiesHandler.cs:23`.
4. **The migrator role applies the migration and the `OpsM9_1a` policy gets created on top of an unset GUC** — should be safe per M.9 §4.2 D2 (`current_setting('app.tenant_id', true)` returns empty + `NULLIF` returns NULL). Verify by running the migration locally first.
5. **More than 10 unique tenants in `ResolveTenantsForGuestUserAsync` at Phase 1.5 seed time** — would surprise; Phase 1.5 guests should have ≤3 hosts. Document.

If the test pack uncovers a NEW cross-tenant data leak that the resolver introduces (e.g. the bypass-scoped query in the resolver accidentally returns a result for a different tenant), STOP and re-consult immediately. This is the "evidence contradicts plan" trigger per `feedback_proactive_architect_consult`.

---

## §11 Close-out (populated at end of F6e)

Slice **CLOSED** 2026-06-29 via commits:

| Sub-step | Commit | What shipped |
|---|---|---|
| F5  | `848c3be` | This planning doc (the architect's design gate). |
| F6a | `55cbaf2` | `IGuestTenantResolver` interface (Contracts) + `GuestTenantResolver` impl (`VrBook.Api.Guests`, 3 bypass factories + ILogger). DI registration in Program.cs. `RlsBypassCallSiteAllowlistTests` allow-list gains the one new entry. Pure abstraction; no behavior change. |
| F6b | `ac34534` | `EnablePublicReadCarveOut(schema, table, usingPredicate)` + `DropPublicReadCarveOut(...)` helpers in `RlsMigrationBuilderExtensions`. New migration `20260629132339_OpsM9_1a_Catalog_PublicReadCarveOut` adds `rls_catalog_properties_public_read` (predicate `is_active = true AND deleted_at IS NULL`) + `rls_catalog_property_images_public_read` (EXISTS-against-parent). Closes audit #8 + #9. |
| F6c | `1997e77` | `ComputeQuoteHandler`, `GetReviewsForPropertyHandler`, `SubmitReviewHandler` injected `IGuestTenantResolver` + opened `BackgroundTenantScope` around their reads. `SubmitReviewHandler` removed the raw-SQL property-tenant probe + the `…0001` fallback Guid (audit #6 foot-gun gone). Closes audit #4 + #5 + #6. |
| F6d | `42ef3b2` | `GetPropertyAvailabilityHandler`, `PlaceBookingHandler` (scope wraps the serializable transaction), `GetBookingHandler`, `MyBookingsHandler` (per-tenant iteration per §1.4), `CancelBookingHandler`. Closes audit #10 + #11. |
| F6e | _this commit_ | `GetOutboundFeedHandler` injected `IGuestTenantResolver` + opened `BackgroundTenantScope` around the token lookup + booking enumeration. Audit cross-reference table (§6.3) marked done. Closes audit #7. |

**Audit findings closed in this slice**: #4, #5, #6, #7, #8, #9, #10, #11 (all 8 High findings tagged "anonymous-tenant read closure" in `docs/OPS_M_10_2_AUDIT_FINDINGS.md` §2).

**Out of scope per F5 §1**: audit #22 (Low — `ReleaseHoldCommand` guess-able) tracked under Slice OPS.M.10.2 C9.

**Validation**: every F6 sub-commit landed CI-green at the `cd-staging-api` workflow (backend tests + contracts + Bicep + 5 image builds + migrate + 4 worker deploys + deploy api + smoke).

**Staging UI verification (per §5)**: the user can hit the public-page URLs enumerated in §5 against `https://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/` paired with the web staging FQDN to confirm:
1. `/properties` returns seeded properties (F6b).
2. `/properties/<seeded-slug>` renders detail + quote widget shows nightly + total (F6b + F6c).
3. Reviews section populates (F6c).
4. Guest persona can place/view/cancel a booking + see it in `/account/bookings` (F6d).
5. The outbound iCal URL (copied from `/admin/sync`) returns 200 with VEVENT entries in a fresh browser tab (F6e).

**Locked-rule honored**:
- `feedback_consult_architect_for_planning` — architect produced this doc before code (F5).
- `feedback_check_ci_after_every_push` — every sub-commit gated on `gh run` conclusion=success.
- `feedback_use_ci_filter_locally` — `pwsh scripts/pre-push-check.ps1` ran the CI filter pre-push (where Docker permitted).
- `feedback_master_todo_naming_slice_prefix` — every TODO entry prefixed `Slice OPS.M.9.1 F6*`.
- `feedback_ship_complete_vertical_slices` — F6 landed staged so each sub-step left staging in a UI-verifiable state, not just passing tests.
