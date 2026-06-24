# Slice 7 — Reports + realtime polish (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, revision 2, 2026-06-24.
**REPLAN section**: `docs/REPLAN.md` §3 Slice 7 (lines 309-332).
**Sequence**: `docs/MASTER_PLAN.md` §2 row 4 (after Slice 6, final Phase-1 slice).

This plan supersedes revision 1 of this document. It is the contract.

---

## 1. What is already shipped (do not re-architect)

**Infra — most of the SignalR work is already done**:
- `infra/modules/signalr.bicep` provisions `Microsoft.SignalRService/signalR@2024-03-01` in `Serverless` mode at `Standard_S1`. EnableConnectivityLogs on, MessagingLogs off, public network enabled, CORS `*`. Outputs: `id`, `name`, `hostName`, `connectionAuthName`.
- `infra/main.bicep` wires the module (line 183), pushes the connection string into the API container as `SignalR__ConnectionString` via `secretRef: 'signalr-cs'` (line 248), declares the secret descriptor `signalr-cs` (line 301), and outputs `signalrHostName` (line 568).
- `src/VrBook.Infrastructure/VrBook.Infrastructure.csproj` already references `Microsoft.Azure.SignalR.Management 1.27.0` (pinned in `src/Directory.Packages.props:52`).
- `src/VrBook.Api/appsettings.json` already has a `SignalR.ConnectionString` slot.

**What is *not* done in the Bicep — corrected from v1**:
- `infra/scripts/10-store-secrets.ps1:73` stores `'pending-bicep-deploy'` as a placeholder for `signalr-cs`. **Nothing currently overwrites it.** ACS solves the same shape by writing the secret inside its module (`infra/modules/acs.bicep:72-79`); SignalR does not. Slice 7 lifts the ACS pattern into `signalr.bicep` (see §2.7).

**What's stubbed and Slice 7 replaces**:
- `RealtimeController.Negotiate` at `src/VrBook.Api/Controllers/ThreadsController.cs:79-86` returns 501.
- `web/src/app/admin/reports/page.tsx` is a 19-line placeholder pointing at "Agent O1 / Screen 6".
- `web/src/app/admin/page.tsx` fetches bookings on mount and computes `tentative.length` — the SignalR target. No subscription today.

**Existing stubs in `src/VrBook.Api/Controllers/AdminController.cs` that Slice 7 must DELETE before re-creating**:
- `ReportsController` at `AdminController.cs:28-47` is a `StubController` returning 501 for `Occupancy` / `Revenue` / `Adr`. The same class name in the same namespace cannot live in both `AdminController.cs` and a new `ReportsController.cs` — CS0101. Slice 7 deletes lines 28-47 of `AdminController.cs` in the **same commit** that adds the new file (see §3 C1).
- The corresponding DTOs `OccupancyReportRow`, `RevenueReportRow`, `AdrReportRow` at `src/VrBook.Contracts/Dtos/Admin.cs:16-40` are tabular `(propertyId, propertyTitle, date, …)` shapes — closer to a CSV row than to the planned series-with-summary. They have no other consumers. Slice 7 **deletes** them in the same commit. `BookingQueueRowDto`, `FeatureToggleDto`, `AlertDto` etc in the same file are unrelated and untouched.

**Data sources for reports are in place**:
- `booking.bookings` (BookingDbContext, schema `booking`): `Status`, `Source` (`BookingSource` enum), `Total`, `Currency`, `Stay.CheckinDate`, `Stay.CheckoutDate`, `ConfirmedAt`, `CancelledAt`, `CheckedOutAt`, `CreatedAt`, `PropertyId`. Properly indexed.
- `booking.availability_blocks` (BookingDbContext, schema `booking`): `PropertyId`, `StartDate`, `EndDate` — half-open `[StartDate, EndDate)`. Slice 7 uses this in the Occupancy denominator (see §2.3).
- `sync.external_reservations` (SyncDbContext, schema `sync`): `PropertyId`, `Channel` (`ChannelKind` enum: `AirBnb`/`Vrbo`/`BookingCom`/`Other`), `Checkin`, `Checkout`, `CancelledAt`. **`IsActive` at `ExternalReservation.cs:27` is a computed property** (`=> CancelledAt is null`) — not a stored column — so EF can't translate it in a `Where()`. The handlers must filter on `CancelledAt == null` directly. The plan's v1 was wrong on this and is corrected throughout §2.3.
- `catalog.properties` (CatalogDbContext): used via `IPropertyOwnerLookup` for the auth filter + label lookup.

**Notes on `BookingSource` enum**:
- `BookingSource.Direct` is the **only** value ever written; every `Booking` is hard-coded `Source = BookingSource.Direct` at `Booking.cs:82`. The other two enum values (`AirBnb`, `Manual`) are latent — they exist in the contract but no code path writes them. The Source report therefore lists exactly **one** direct slice (`Direct`), plus the four channel slices from `sync.external_reservations`. We don't ship a `Manual` or `AirBnB-direct` slice that would always render as 0; if Phase 2 adds `Manual`-typed bookings, the slicer expands.

**Cross-module port that Slice 7 reuses**:
- `IPropertyOwnerLookup` (`src/VrBook.Contracts/Interfaces/IPropertyOwnerLookup.cs`) — exposes `ListPropertyIdsOwnedByAsync(ownerUserId)` and per-property `GetAsync(propertyId)`. Same port that `ListAdminBookingsHandler` uses.

**Admin module is essentially empty**:
- `src/Modules/VrBook.Modules.Admin/AdminModule.cs` is a no-op stub. There is no AdminBookingsController in the Admin module — admin-facing booking queries live in `VrBook.Modules.Booking.Application.Queries.ListAdminBookingsQuery`.

**Frontend baseline**:
- `web/package.json` does not include `recharts` or `@microsoft/signalr` (both new deps).
- `@dnd-kit/*` already shipped in Slice 6; per-slice dep additions are consistent with prior slices.
- `web/src/lib/api/client.ts` exports `apiFetch`. `web/src/lib/api/me.ts` exists from Slice 6. `web/src/lib/api/booking.ts` has `adminListBookings(...)`.

**Test fixture baseline**:
- `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs` migrates **only** `IdentityDbContext`. Reports handler integration tests need Booking + Sync + Catalog contexts migrated as well (see §2.13).

**Solution file**:
- New projects must be added to `src/VrBook.sln`. Slice 7 adds an 11th module project.

---

## 2. Decisions (architect-locked)

### 2.1 Module placement — **new `VrBook.Modules.Reports` module**

Three options were considered:
- (i) New `VrBook.Modules.Reports` with its own slim `IReportsQueries` shape that depends on `BookingDbContext` + `SyncDbContext` + `IPropertyOwnerLookup`. **Picked.**
- (ii) Pour the handlers into the Admin module. The Admin module is a stub — extending it forces the same wiring with the extra cognitive load that "admin" conflates "admin moderation" (Phase 2) and "owner reports" (Phase 1).
- (iii) Materialized views per report. Out — operational cost without a Phase-1 latency problem to solve.

`VrBook.Modules.Reports` has no DbContext of its own. Each query handler injects the read-only `DbContext` it needs (the module-per-context convention is preserved — Reports never *owns* a context, it only *reads* from the modules whose contracts have stabilised). Reports is the first module to read from two DbContexts (Booking + Sync) and zip results in C#; prior art for cross-module ProjectReferences exists (Reviews → Booking + Catalog) but cross-DbContext aggregation is genuinely new.

### 2.2 Reports period model — **`from`/`to` date range (UTC dates, inclusive), per-property filter optional**

The four report queries share the same envelope:

```csharp
public sealed record ReportRangeQuery(
    DateOnly From,
    DateOnly To,
    Guid? PropertyId);   // null = all properties owned by caller
```

- `To >= From`; max span 366 days (FluentValidation; matches a calendar-year export).
- Bucket granularity is **daily** for Occupancy / Revenue / ADR. Source report is **non-bucketed totals over the range** (one slice per channel).
- Property filter: `null` = "all properties owned by the caller". `Admin` role sees all properties unfiltered. Non-admin owners passing a `propertyId` they don't own → `403`.

### 2.3 Report shapes (4 reports × decisions per report)

**Occupancy** — `OccupancyReportDto { ReadOnlyList<OccupancyPoint> Series, OccupancySummary Summary }`, where `OccupancyPoint { DateOnly Date, int BookedNights, int AvailableNights, decimal OccupancyPct }`.

- **Numerator per day** = count of (property, day) tuples where there is an active booking covering that night — `Status ∈ { Confirmed, CheckedIn, CheckedOut, Completed }` (Tentative excluded; Cancelled / Rejected excluded). Half-open `[Stay.CheckinDate, Stay.CheckoutDate)` convention preserved.
- **Denominator per day** = `(properties in filter set) − (properties blocked on that day via `booking.availability_blocks`)`. Phase-1 simplification kept from v1: properties are otherwise treated as continuously rentable (no per-season-window listing model). This is the **denominator correction** the reviewer asked for: ~15 LoC of additional SQL (one `AvailabilityBlock` query, fan to in-range nights in C#, subtract from the daily denominator).
- **Algorithm**: pull bookings whose stay overlaps `[from, to]`, fan to in-range nights. Pull blocks whose `[StartDate, EndDate)` overlaps `[from, to]`, fan to in-range nights. Group both by date. 50 properties × 365 days × ≤200 bookings + ≤50 blocks ≈ 12k tuples — trivial in C#.

**Revenue** — `RevenueReportDto { ReadOnlyList<RevenuePoint> Series, RevenueSummary Summary }`, where `RevenuePoint { DateOnly Date, decimal Revenue, string Currency }`.

- Bucket by `ConfirmedAt::date` (UTC) — the moment money was committed. Tentative excluded; rows where `CancelledAt > ConfirmedAt` excluded (the booking was confirmed then cancelled in-range; we count net confirmations). Defensive `Status NOT IN { Cancelled, Rejected, Refunded, Disputed }` for future-proofing.
- Currency: Phase 1 owners are single-currency in practice. The DTO carries `Currency` per-point but the handler **asserts a single currency across the result** and returns `422 reports.mixed_currency` if mixed.
- External reservations (iCal feeds carry no money) are **excluded** from Revenue. This is unambiguous in the Revenue tab.

**ADR** (Average Daily Rate) — `AdrReportDto { ReadOnlyList<AdrPoint> Series, AdrSummary Summary }`, where `AdrPoint { DateOnly Date, decimal? Adr, int BookedNights, decimal Revenue, string Currency }`.

- Per day: `Adr = Revenue ÷ BookedNights` over the same projection as Occupancy + Revenue.
- **Zero-night days emit `Adr = null`** (not `0`). Recharts is configured `connectNulls={false}` so the line breaks at no-data days. Rendering `0` would lie — no rate was charged. A null gap is the honest UX. (Resolves §2.3 ADR contradiction in v1.)
- Same single-currency assertion as Revenue.

**Source** — `SourceReportDto { ReadOnlyList<SourceSlice> Slices, SourceSummary Summary }`, where `SourceSlice { string Source, int Bookings, int Nights }`. **Revenue is intentionally absent.**

- Slices: `Direct`, `AirBnB`, `Vrbo`, `BookingCom`, `Other`. The first comes from `booking.bookings.Source = Direct` (the only direct value ever written today). The last four come from `sync.external_reservations.Channel`.
- "Bookings" = distinct booking IDs whose stay overlaps `[from, to]`. "Nights" = sum of in-range nights.
- **No `Revenue` field on the slice.** Revenue lives on the Revenue tab where the cohort (confirmed-in-range) is unambiguous; including it on Source would force either a cohort mismatch with the in-range/Bookings count (two-cohort confusing) or count direct-but-confirmed-outside money (overlap-confusing). Rather than ship either confusion, we omit it. (Resolves §2.3 Source contradiction in v1.)
- **Cross-module read**: the handler queries `BookingDbContext` and `SyncDbContext` separately and zips in C#. No DB-level join.
- External reservations are filtered with `r.CancelledAt == null` — **not** `r.IsActive`, because `IsActive` is a computed expression EF can't translate (`ExternalReservation.cs:27`). The semantic is identical; the syntax matters.

### 2.4 Authorization — **per-method `[Authorize(Roles = "Owner,Admin")]`; ownership enforced in handler**

Slice 6 §2.12 established the pattern: handler-level ownership check via `IPropertyOwnerLookup`, controller-level role gate. Slice 7 follows.

- `ReportsController` is `[Authorize(Roles = "Owner,Admin")]` per-method.
- Each handler:
  1. If `IsAdmin` → skip ownership filter.
  2. Else if `PropertyId` is set → call `IPropertyOwnerLookup.GetAsync(propertyId)`; if the owner doesn't match `currentUser.UserId` → `ForbiddenException`.
  3. Else → call `ListPropertyIdsOwnedByAsync(currentUser.UserId)` and scope the query to that set.

### 2.5 Realtime notification path — **`IRealtimeNotifier` port + MediatR `INotificationHandler<BookingPlaced>` adapter; lean payload + client refetch; fire-and-forget**

`BookingPlaced` is already raised by `Booking.Place(...)`. Outbox + MediatR fan-out already in place. Slice 7 adds:

1. New port `VrBook.Contracts.Interfaces.IRealtimeNotifier`:
   ```csharp
   Task NotifyUserAsync(Guid userId, string method, object payload, CancellationToken ct);
   Task<NegotiateResult> NegotiateForUserAsync(Guid userId, CancellationToken ct);
   ```

2. Implementation `VrBook.Infrastructure.Realtime.SignalRRealtimeNotifier` using `Microsoft.Azure.SignalR.Management.ServiceManager` + `ServiceHubContext`. **Singleton**. The `ServiceHubContext` is held in a `Lazy<Task<ServiceHubContext>>` field so the first call pays the build cost once; every subsequent call reuses it. `Dispose()` releases the hub context. Configuration read from `SignalR:ConnectionString` via `IOptions<SignalROptions>` snapshot at construction.

3. **Per-user routing**: server uses `Clients.User(ownerUserId.ToString())`. In Serverless mode, routing is done by the SignalR Service using the JWT `sub` claim set during `Negotiate` — there is no API-side `IUserIdProvider` to register.

4. New `INotificationHandler<BookingPlaced>` in the Reports module (`OnBookingPlacedHandler`):
   - Resolve `ownerUserId` via `IPropertyOwnerLookup.GetAsync(@event.PropertyId, ct)` — one indexed lookup.
   - Build **lean payload**: `{ bookingId, reference, checkinDate, checkoutDate, tentativeUntil }`. Property title + guest name are deliberately not in the payload (see §2.5 reasoning).
   - **Fire-and-forget** the SignalR call: `_ = Task.Run(async () => { try { await notifier.NotifyUserAsync(...); } catch (Exception ex) { logger.LogWarning(ex, "tentativeBookingAdded push failed"); } });`. The booking-POST does not wait on the SignalR REST round-trip. Realtime is best-effort by contract.
   - The MediatR handler returns `Task.CompletedTask` immediately after dispatching the fire-and-forget.

5. **No DB write**. The pill increment is computed client-side from the existing on-mount fetch plus the push event. **On push, the client refetches** `adminListBookings({ status: 'Tentative' })` (single source of truth — no client-side counter that can drift from the server). The push event is the trigger; the DB is the data.

**Why lean payload + refetch**: the dashboard already has the list query and refetching it is one tab's existing code path. Putting title + guest name in the push payload would force `OnBookingPlacedHandler` to do a second DB read (catalog property title + identity user display name — two more network hops on the booking-POST critical path), and the dashboard would still need to integrate the push row with the existing list shape. Refetching after push is one request that returns the authoritative table.

### 2.6 `Negotiate` endpoint — **per-user token, 1h TTL, hub name `notifications`**

`RealtimeController.Negotiate` becomes the real implementation:
- One hub: `notifications`. No per-purpose hub split. Slice 7 ships only the `notifications` hub; a messaging hub would be future work.
- Token TTL: 1 hour. SDK call: `hub.NegotiateAsync(new NegotiationOptions { UserId = userId.ToString(), TokenLifetime = TimeSpan.FromHours(1) })`. `ExpiresAt` is computed in the controller as `clock.UtcNow + 1h`.
- Token claims: `sub` = caller's user ID (Guid as string).
- Response: `{ url, accessToken, expiresAt }` (existing `RealtimeNegotiateResponse` DTO at `src/VrBook.Contracts/Dtos/Messaging.cs:38-41`).
- Returns `401` if no authenticated user. Returns `503 realtime.unavailable` if `NegotiateAsync` throws (`AzureSignalRException` / `HttpRequestException`); a 1s CTS timeout prevents a hung endpoint from holding the request.

### 2.7 Hub auth + Service interaction — **Serverless mode; no API-hosted hub; KV write inside `signalr.bicep`**

The Bicep sets `ServiceMode: Serverless`. The API process does not host a SignalR hub class; `ServiceManager.CreateHubContextAsync("notifications")` returns a `ServiceHubContext` for server-to-client push.

**Bicep change — the missing piece**: `infra/modules/signalr.bicep` currently does not write the connection string anywhere. The `signalr-cs` Key Vault secret stays at the placeholder value `'pending-bicep-deploy'` from `infra/scripts/10-store-secrets.ps1:73`. Slice 7 follows the ACS pattern (`infra/modules/acs.bicep:72-79`):

- Add `param keyVaultName string` to `signalr.bicep`.
- Add a `Microsoft.KeyVault/vaults/secrets@2024-04-01-preview` child resource that writes `sigr.listKeys().primaryConnectionString` into KV under `name: 'signalr-cs'`. The secret never leaves the deployment as a module output (linter rule `outputs-should-not-contain-secrets`).
- Wire `keyVaultName: kv.outputs.name` in `infra/main.bicep` line 185.
- After this deploys, `10-store-secrets.ps1` line 73's placeholder is overwritten with the real value on every `azd up` — same idempotency story ACS has today.

No SKU change (`Standard_S1`: 1k concurrent connections, 1M msgs/day — massively over-provisioned for ≤50 users; leave it).

### 2.8 CSV export — **client-side, `Blob` URL with BOM + RFC 4180 escaping, InvariantCulture**

Picked client-side. Reports max ~366 rows; CSV generation is microseconds.

Escaping rules:
- Prefix with UTF-8 BOM so Excel opens non-ASCII correctly.
- Per RFC 4180: any cell containing `,`, `"`, `\r`, or `\n` is quoted; embedded `"` is doubled.
- ISO-8601 dates (`YYYY-MM-DD`) via `toISOString().slice(0,10)`.
- Numbers formatted with `Intl.NumberFormat('en-US', ...)` (locked to `en-US`, not the browser locale) → `.` as decimal separator, no thousands separator. Locking the locale is what prevents a German tester's CSV from emitting `1,5` and breaking Excel column types.

Filename: `vrbook-{report}-{from}-{to}.csv`.

Column headers are `snake_case` lowercase versions of the DTO field names (e.g. `date`, `booked_nights`, `available_nights`, `occupancy_pct`) so the column order survives sorting and the headers don't depend on locale-specific display strings.

European Excel installations may need an import-with-locale-override; CSVs use `.` decimal because the ISO data standard does.

### 2.9 Charting library — **`recharts` (new dep), dynamic-imported per tab**

REPLAN names Recharts explicitly. ~95KB gzipped. MIT.

Each chart component (`OccupancyChart`, `RevenueChart`, `AdrChart`, `SourceChart`) is dynamic-imported (`next/dynamic` with `ssr: false`) from the tab page. The `/admin/reports` initial bundle therefore loads filter-bar + tab shell + the active tab's chart only. Inactive tabs do not pay the Recharts cost until clicked. This is the bundle-isolation point the reviewer flagged.

- Occupancy: `<LineChart>` with one line.
- Revenue: `<BarChart>` daily.
- ADR: `<LineChart>` daily with `connectNulls={false}` so zero-night day gaps render as breaks (per §2.3).
- Source: `<PieChart>` for the bookings slice; table beneath for nights breakdown.

### 2.10 Where `OnBookingPlacedHandler` lives — **Reports module**

The unit of cohesion is "things the owner watches"; the dashboard pill is the same audience as the reports tabs. The Booking module stays clean of SignalR.

### 2.11 Connection lifecycle in the browser — **`@microsoft/signalr` singleton with reconnect, hidden-tab pause, visibility-refetch fallback**

- `web/src/lib/realtime/connection.ts` exports `getRealtimeConnection()` — a module-level cached `HubConnection`. The cache is gated on `typeof window !== 'undefined'` (no SSR mis-instantiation) and the `start()` call is idempotent — calling it while already `Connected` or `Connecting` is a no-op. This handles React Strict Mode's dev double-invoke of `useEffect`.
- `HubConnectionBuilder().withUrl(negotiateUrl, { accessTokenFactory: ... }).withAutomaticReconnect()`. The `accessTokenFactory` re-fetches `/api/v1/realtime/negotiate` so the 1h TTL is invisible.
- **Hidden-tab handling**: `document.visibilityState === 'hidden'` → `connection.stop()`; on visible, `connection.start()` (idempotent).
- **Graceful degrade**: on negotiate / start failure, the hook silently logs and sets `connected = false`. The dashboard then falls back to a `visibilitychange → visible` one-shot `refetch` of `adminListBookings({ status: 'Tentative' })` — no `setInterval`, just an opportunistic refresh whenever the user returns to the tab. The pill self-heals without realtime.
- **Always-on safety net**: even when `connected === true`, the dashboard runs the same visibility-refetch one-shot. Guards against staleness after long backgrounds even if SignalR is connected.

### 2.12 Owner identity in the push — **`OwnerUserId` resolved from `PropertyId` at handler time**

`BookingPlaced` carries `PropertyId` but not `OwnerUserId`. The handler resolves owner via `IPropertyOwnerLookup.GetAsync(propertyId)` — one indexed read.

### 2.13 Test fixture — **extend `IdentityApiFixture` to migrate Booking + Sync + Catalog contexts**

`IdentityApiFixture` today migrates only `IdentityDbContext`. Reports handlers fan out across `BookingDbContext` + `SyncDbContext` + `CatalogDbContext` with date-window EF queries (group-by-date, overlap windows). Substituting these `DbContext`s with mocks would force the tests to re-implement the aggregation logic — defeating the test.

Add three `MigrateAsync()` calls inside `InitializeAsync` (~15 LoC). The Postgres testcontainer cost is already paid. The connection string already routes all four contexts to the same DB (the modules carve schemas, not databases).

Update `ResetAsync()` to truncate the additional schemas: `booking.*`, `sync.*`, `catalog.*` — cascade-keyed so order doesn't matter.

### 2.14 Sync conflict realtime push — **out of scope; document the seam**

A "sync conflict appeared" toast would also benefit from realtime push. Slice 7 explicitly does **not** ship this. The `IRealtimeNotifier` port + Bicep + Negotiate work makes the second handler a ~30-line add in Phase 2.

---

## 3. Commit split (5 commits — C3 folded into C4 per Decision 7)

### C1 — Reports module + 4 query handlers + endpoints + tests

**Step 1 (do first, in this commit)**: delete dead code.
- Delete `ReportsController` class (and its three `[HttpGet]` methods) at `src/VrBook.Api/Controllers/AdminController.cs:28-47`.
- Delete `OccupancyReportRow`, `RevenueReportRow`, `AdrReportRow` records at `src/VrBook.Contracts/Dtos/Admin.cs:16-40`.
- Verify no other consumer: `rg "OccupancyReportRow|RevenueReportRow|AdrReportRow"` returns zero hits after the delete.

**Step 2 (new module)**:
- New project `src/Modules/VrBook.Modules.Reports/VrBook.Modules.Reports.csproj`. References: `VrBook.Application`, `VrBook.Contracts`, `VrBook.Modules.Booking`, `VrBook.Modules.Sync`, MediatR, FluentValidation.
- **Add to `src/VrBook.sln`**: `dotnet sln src/VrBook.sln add src/Modules/VrBook.Modules.Reports/VrBook.Modules.Reports.csproj`. Must precede the `Program.cs` reference.
- `ReportsModule.cs` — registers `AddModuleAssembly(typeof(ReportsModule).Assembly)` for MediatR + validators. No DbContext.
- `Application/Common/ReportRangeQuery.cs` — base record + FluentValidation rules (To >= From, max 366 days).
- `Application/Common/ReportsAuthorization.cs` — shared helper (mirrors `PricingAuthorization` from Slice 6).
- Four query handlers (Occupancy / Revenue / ADR / Source).
- Contracts DTOs added to `src/VrBook.Contracts/Dtos/Reports/` (one file per report).
- New `src/VrBook.Api/Controllers/ReportsController.cs` (its own file, NOT in `AdminController.cs`) — `[Route("api/v1/admin/reports")]`, `[Authorize(Roles = "Owner,Admin")]` per method:
  - `GET /occupancy?from=...&to=...&propertyId=...`
  - `GET /revenue?from=...&to=...&propertyId=...`
  - `GET /adr?from=...&to=...&propertyId=...`
  - `GET /source?from=...&to=...&propertyId=...`
- `src/VrBook.Api/Program.cs` adds `.AddReportsModule(builder.Configuration)`.

**Step 3 (test fixture)**:
- Extend `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs` per §2.13: migrate Booking + Sync + Catalog contexts in `InitializeAsync`; truncate their schemas in `ResetAsync`.

**Tests in `tests/VrBook.Api.IntegrationTests/Reports/`**:
- `OccupancyReportHandlerTests.cs` — 3 properties × 30 days × mix of bookings + blocks → expected daily numerator + denominator.
- `RevenueReportHandlerTests.cs` — confirmed-in-range bookings sum; mixed-currency 422 path.
- `AdrReportHandlerTests.cs` — per-day `Adr × BookedNights == Revenue`; zero-night day emits null (asserted explicitly).
- `SourceReportHandlerTests.cs` — direct booking counts + external reservation counts; `CancelledAt == null` filter exercised (not `IsActive`).
- `ReportsAuthorizationTests.cs` — owner-A 403 when probing owner-B's property; admin bypass.

Acceptance 1 (filter date range + charts render — server side) lands here.

### C2 — `/admin/reports` UI with tabs + Recharts (dynamic-imported) + filters + CSV export

- New `web/src/lib/api/reports.ts`: types matching the contracts DTOs + 4 fetchers.
- `web/package.json`: add `recharts`.
- Replace `web/src/app/admin/reports/page.tsx` stub:
  - Top filter bar: date-range picker (default last 30 days), property dropdown (`adminListMyProperties()`).
  - Tab bar: Occupancy / Revenue / ADR / Source. Active tab in URL query (`?tab=revenue`).
  - **Each chart component is dynamic-imported** (`next/dynamic` with `ssr: false`) so inactive tabs don't pull Recharts into the initial bundle (§2.9).
  - "Export CSV" button on each tab.
- New `web/src/components/reports/`: `OccupancyChart.tsx`, `RevenueChart.tsx`, `AdrChart.tsx` (with `connectNulls={false}`), `SourceChart.tsx`, `ReportFilters.tsx`.
- New `web/src/lib/csv.ts` — RFC 4180 escaper + BOM-prefix + `downloadCsv(filename, rows)`, using `InvariantCulture`-equivalent formatting (`Intl.NumberFormat('en-US', { useGrouping: false })`, ISO dates).
- Vitest unit test on `web/src/lib/csv.ts` exercising quote escape, embedded newline, locale-invariance.

Acceptance 1 (filter + export) lands here.

### C3 — API `Negotiate` + `IRealtimeNotifier` + Bicep KV-write + `OnBookingPlacedHandler` (folded scope per Decision 7)

**Bicep**:
- `infra/modules/signalr.bicep` — add `param keyVaultName string`; add a `Microsoft.KeyVault/vaults/secrets` child resource that writes `sigr.listKeys().primaryConnectionString` to `signalr-cs` (mirrors `acs.bicep:72-79`).
- `infra/main.bicep` line 185 — pass `keyVaultName: kv.outputs.name` to the `sigr` module.
- After deploy, the `'pending-bicep-deploy'` placeholder at `10-store-secrets.ps1:73` is overwritten on every `azd up`. No script change required; **no new `.ps1`** because the Bicep write is now the source of truth.
- New `docs/runbooks/signalr-secret-rotation.md` — runbook (rotate primary → secondary, `azd up` re-writes KV, restart Container App, verify `/api/v1/realtime/negotiate` 200).

**Contracts**:
- New `src/VrBook.Contracts/Interfaces/IRealtimeNotifier.cs`.
- `RealtimeNegotiateResponse` already exists at `src/VrBook.Contracts/Dtos/Messaging.cs:38-41` — verified, no change.

**Infrastructure**:
- New `src/VrBook.Infrastructure/Realtime/SignalRRealtimeNotifier.cs`:
  - Singleton.
  - `private readonly Lazy<Task<ServiceHubContext>> _hub;` — built once at first use; `ServiceManager` constructed from `IOptions<SignalROptions>` in the ctor, `CreateHubContextAsync("notifications")` called inside the lazy factory. Subsequent calls reuse the cached `ServiceHubContext`.
  - `NotifyUserAsync` → `(await _hub.Value).Clients.User(userId.ToString()).SendCoreAsync(method, new[] { payload }, ct)`.
  - `NegotiateForUserAsync` → `(await _hub.Value).NegotiateAsync(new NegotiationOptions { UserId = userId.ToString(), TokenLifetime = TimeSpan.FromHours(1) })`.
  - `IAsyncDisposable.DisposeAsync` releases the hub.
- New `src/VrBook.Infrastructure/Realtime/NullRealtimeNotifier.cs` — dev fallback when `SignalR:ConnectionString` is empty; logs + no-ops.
- `src/VrBook.Infrastructure/ServiceCollectionExtensions.cs` — edit: register `IRealtimeNotifier` based on config presence.

**API**:
- `src/VrBook.Api/Controllers/ThreadsController.cs` — replace the 501 in `RealtimeController.Negotiate` with the real impl (calls `IRealtimeNotifier.NegotiateForUserAsync(currentUser.UserId)`; returns 503 `realtime.unavailable` on failure with 1s CTS timeout).

**Reports module — push handler**:
- New `src/Modules/VrBook.Modules.Reports/Application/Realtime/OnBookingPlacedHandler.cs` — `INotificationHandler<BookingPlaced>`:
  - Resolve `ownerUserId` via `IPropertyOwnerLookup.GetAsync(@event.PropertyId, ct)`.
  - Build **lean payload** `{ bookingId, reference, checkinDate, checkoutDate, tentativeUntil }`.
  - Fire-and-forget: `_ = Task.Run(async () => { try { await _notifier.NotifyUserAsync(ownerUserId, "tentativeBookingAdded", payload, CancellationToken.None); } catch (Exception ex) { _logger.LogWarning(ex, "tentativeBookingAdded push failed"); } });`. Return `Task.CompletedTask`.

**Tests**:
- `tests/VrBook.Api.IntegrationTests/Realtime/NegotiateEndpointTests.cs` — happy path + 401 + 503.
- `tests/VrBook.Api.IntegrationTests/Realtime/OnBookingPlacedHandlerTests.cs` — uses a substitute `IRealtimeNotifier` to assert payload shape + that booking-POST does not wait on the push (asserted via a 500ms delay on the substitute notifier — the booking-POST must return well before the delay completes).

### C4 — `@microsoft/signalr` client + Dashboard pill subscription + reconnect handling

- `web/package.json`: add `@microsoft/signalr` (8.x).
- New `web/src/lib/realtime/connection.ts` — singleton `HubConnection` per §2.11 (module-level memo, window-gated, idempotent `start()`).
- New `web/src/hooks/useTentativeBookingPush.ts`:
  - Calls `getRealtimeConnection()`, calls `.on('tentativeBookingAdded', onPushed)`.
  - Pauses on `visibilitychange → hidden`; resumes on visible.
  - Returns `{ connected: boolean }`.
- `web/src/app/admin/page.tsx` — edit:
  - Subscribe via `useTentativeBookingPush(onPushed = () => refetch())`. On push, refetch `adminListBookings({ status: 'Tentative' })` (single source of truth — §2.5 step 5).
  - **Always-on visibility refetch** (§2.11): register a `visibilitychange → visible` once-per-foreground refetch regardless of `connected` flag. Self-heals after long backgrounds.
  - "Live" badge: small green dot when `connected === true`.
- Vitest unit test on the hook with a mocked `HubConnection`.

Acceptance 2 (pill increments within 5s, no refresh) lands here.

### C5 — Sample seed + dashboard polish + verification recipe

- `docs/runbooks/slice7-seed.md` — SQL snippet ensuring the dev DB has confirmed bookings + external reservations + an availability block + an active stay spanning today.
- README / QUICKSTART tweak documenting the SignalR dep and the realtime degrade behaviour.
- `docs/runbooks/realtime-degradation.md` — triage guide.
- Verification recipe walk in §7.

---

## 4. Concrete file additions

### Contracts
- `src/VrBook.Contracts/Interfaces/IRealtimeNotifier.cs` — new.
- `src/VrBook.Contracts/Dtos/Reports/OccupancyReportDto.cs` — new.
- `src/VrBook.Contracts/Dtos/Reports/RevenueReportDto.cs` — new.
- `src/VrBook.Contracts/Dtos/Reports/AdrReportDto.cs` — new.
- `src/VrBook.Contracts/Dtos/Reports/SourceReportDto.cs` — new.
- `src/VrBook.Contracts/Dtos/Admin.cs` — **edit**: delete `OccupancyReportRow`, `RevenueReportRow`, `AdrReportRow` (lines 16-40).
- `src/VrBook.Contracts/Dtos/Messaging.cs` — verified, no change.

### Reports module (new project + .sln entry)
- `src/Modules/VrBook.Modules.Reports/VrBook.Modules.Reports.csproj` — new.
- `src/VrBook.sln` — **edit**: add module project entry.
- `src/Modules/VrBook.Modules.Reports/ReportsModule.cs` — new.
- `src/Modules/VrBook.Modules.Reports/Application/Common/ReportRangeQuery.cs` — new.
- `src/Modules/VrBook.Modules.Reports/Application/Common/ReportsAuthorization.cs` — new.
- `src/Modules/VrBook.Modules.Reports/Application/Occupancy/Queries/GetOccupancyReportQuery.cs` + handler — new.
- `src/Modules/VrBook.Modules.Reports/Application/Revenue/Queries/GetRevenueReportQuery.cs` + handler — new.
- `src/Modules/VrBook.Modules.Reports/Application/Adr/Queries/GetAdrReportQuery.cs` + handler — new.
- `src/Modules/VrBook.Modules.Reports/Application/Source/Queries/GetSourceReportQuery.cs` + handler — new.
- `src/Modules/VrBook.Modules.Reports/Application/Realtime/OnBookingPlacedHandler.cs` — new.

### Infrastructure
- `src/VrBook.Infrastructure/Realtime/SignalRRealtimeNotifier.cs` — new (singleton, `Lazy<Task<ServiceHubContext>>`).
- `src/VrBook.Infrastructure/Realtime/NullRealtimeNotifier.cs` — new (dev fallback).
- `src/VrBook.Infrastructure/ServiceCollectionExtensions.cs` — edit: register `IRealtimeNotifier`.

### API
- `src/VrBook.Api/Controllers/AdminController.cs` — **edit**: delete `ReportsController` class (lines 28-47).
- `src/VrBook.Api/Controllers/ReportsController.cs` — new (4 endpoints).
- `src/VrBook.Api/Controllers/ThreadsController.cs` — edit: replace `RealtimeController.Negotiate` 501 with real impl.
- `src/VrBook.Api/Program.cs` — edit: `.AddReportsModule(builder.Configuration)`.

### Web
- `web/package.json` — add `recharts`, `@microsoft/signalr`.
- `web/src/app/admin/reports/page.tsx` — replace stub.
- `web/src/app/admin/page.tsx` — edit: subscribe via `useTentativeBookingPush`; always-on visibility refetch.
- `web/src/components/reports/OccupancyChart.tsx` — new (dynamic-imported).
- `web/src/components/reports/RevenueChart.tsx` — new (dynamic-imported).
- `web/src/components/reports/AdrChart.tsx` — new (`connectNulls={false}`, dynamic-imported).
- `web/src/components/reports/SourceChart.tsx` — new (dynamic-imported).
- `web/src/components/reports/ReportFilters.tsx` — new.
- `web/src/lib/api/reports.ts` — new.
- `web/src/lib/csv.ts` — new (InvariantCulture-equivalent formatting).
- `web/src/lib/realtime/connection.ts` — new (singleton, Strict-Mode-safe).
- `web/src/hooks/useTentativeBookingPush.ts` — new.

### Tests
- `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs` — **edit**: migrate Booking + Sync + Catalog contexts; truncate their schemas.
- `tests/VrBook.Api.IntegrationTests/Reports/OccupancyReportHandlerTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Reports/RevenueReportHandlerTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Reports/AdrReportHandlerTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Reports/SourceReportHandlerTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Reports/ReportsAuthorizationTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Realtime/NegotiateEndpointTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Realtime/OnBookingPlacedHandlerTests.cs` — new (asserts booking-POST does not wait on push).
- `web/src/lib/csv.test.ts` — new (Vitest).
- `web/src/hooks/useTentativeBookingPush.test.ts` — new (Vitest).

### Infra + Ops
- `infra/modules/signalr.bicep` — **edit**: `param keyVaultName` + KV secret child resource.
- `infra/main.bicep` — edit: pass `keyVaultName` to `sigr` module.
- `docs/runbooks/signalr-secret-rotation.md` — new.
- `docs/runbooks/realtime-degradation.md` — new.
- `docs/runbooks/slice7-seed.md` — new.

### CI
- None. The Bicep KV-write is part of `azd up`; no pipeline change needed.

---

## 5. Scope-cut order (drop top first when deadline bites)

REPLAN's risk callout: "SignalR Serverless setup is real ops work. Isolate to this slice so it can be dropped without affecting funnel." Scope-cut order honours that.

1. **SignalR push entirely** — drop C3 + C4 + the C5 dashboard polish + realtime runbooks. Dashboard pill keeps polling on mount. Reports + CSV still ship. Acceptance 1 met; acceptance 2 falls.
2. **CSV export** — drop the CSV button + `web/src/lib/csv.ts`. Reports still render. Acceptance 1's "export" half falls.
3. **Source report** — the heaviest (cross-context read). Drop the tab + handler + test.
4. **ADR report** — derived from Revenue + nights. Drop tab + handler + test.

Phase-1 minimum after all four cuts: **Occupancy + Revenue charts on `/admin/reports`, dashboard polling on mount, no CSV**.

Never falls: C1 occupancy + revenue handlers + endpoints; C2's chart tab shell for those two reports.

---

## 6. Out of scope (Phase 1.5+ / OPS / next slice)

- **Per-property listing-window model in Occupancy denominator** — Phase 1 subtracts blocks but assumes properties are otherwise continuously rentable.
- **Multi-currency Revenue + ADR aggregation** — needs an FX-rate provider. Handlers return 422 on mixed currency.
- **`BookingSource.Manual` + `BookingSource.AirBnb` direct-side slices** — enum values exist but no code writes them today. Source report's "direct" slice covers `BookingSource.Direct` only; expand when those values are written.
- **Saved report views** ("my favourites: last 30 days, property X").
- **Scheduled email reports**.
- **Realtime push for anything other than tentative bookings** — sync conflicts, payment captures, message arrivals. Port + Bicep make these ~30-line adds.
- **Tenant-scoped SignalR routing** — Phase 2 / OPS.M.
- **Materialized views per report** — Phase 1.5 optimisation.
- **Server-side CSV streaming**.
- **Hub upstream (client → server) messages** — Serverless mode would require either upstream webhooks or migrating mode.
- **`Negotiate` per-purpose hub split** — one `notifications` hub; messaging hub is future work.
- **Admin moderation view of all properties' reports**.
- **Source-tab revenue breakdown** — explicitly omitted to avoid the cohort confusion §2.3 documents.

---

## 7. Verification recipe (end-of-slice)

1. **Migrations**: none expected. `dotnet ef migrations list` output unchanged from Slice 6.
2. **Build**: `dotnet build` green; `cd web && npm install && npm run typecheck && npm run lint` green. `rg "OccupancyReportRow|RevenueReportRow|AdrReportRow"` returns zero hits (dead DTOs are gone).
3. **Reports — backend**: seed dev DB via `docs/runbooks/slice7-seed.md`. Then:
   - `curl /api/v1/admin/reports/occupancy?from=...&to=...` returns ~30 daily points. A blocked day on a single-property filter drops `AvailableNights` to 0 → `OccupancyPct = null` (or 0 — pick whichever the contract states).
   - `curl /api/v1/admin/reports/revenue?from=...` — sum of `point.revenue` matches `SELECT SUM(total) FROM booking.bookings WHERE status >= 'Confirmed' AND confirmed_at::date BETWEEN ...`.
   - `curl /api/v1/admin/reports/adr?from=...` — per-day `adr × booked_nights = revenue`; zero-night days emit `"adr": null`.
   - `curl /api/v1/admin/reports/source?from=...` — bookings sum across slices = total in-range bookings; no `revenue` field present.
4. **Reports — UI**: open `/admin/reports` → each tab renders without console errors. Dev-tools network tab shows the chart bundle is loaded **only** when its tab is clicked (verifies dynamic import).
5. **CSV export**: on each tab click "Export CSV". File opens in Excel with no mojibake. Quoted cells parse correctly. Decimal separators are `.` regardless of OS locale.
6. **Property filter**: select a single property → all charts update; CSV reflects the filter.
7. **Date filter**: change `to` to last 7 days → series shortens.
8. **Cross-property auth**: as owner-A, `curl /api/v1/admin/reports/occupancy?propertyId={owner-B-id}` → 403.
9. **Negotiate endpoint**: `curl /api/v1/realtime/negotiate` → 200 `{ url, accessToken, expiresAt }`. (After `azd up`, `signalr-cs` in KV is no longer `'pending-bicep-deploy'` — `az keyvault secret show --name signalr-cs --query value` returns a real `Endpoint=...;AccessKey=...` string.)
10. **Token expiry path**: SDK auto-reconnect calls `accessTokenFactory` → new negotiate fires → reconnection succeeds.
11. **Realtime pill**: open `/admin` in browser A as owner. In browser B as guest, place a new booking on a property the owner owns. Within 5s, browser A's Tentative pill ticks up by 1. (Verifies push → refetch → table update.)
12. **Booking-POST latency unchanged**: instrument `POST /api/v1/bookings` with a substitute `IRealtimeNotifier` whose `NotifyUserAsync` sleeps 500ms. The booking-POST returns in <100ms regardless. (Confirms fire-and-forget.)
13. **Connection-down degrade**: Stop SignalR or clear the secret. Reopen `/admin`. Negotiate 503; hook logs but doesn't throw. Pill shows on-mount value. Switch tabs and back → pill refetches via the visibility fallback. No console explosions.
14. **CI**: integration tests green for `Reports.*` + `Realtime.*`. Web typecheck + lint + Vitest green.

If 3 + 4 + 5 + 8 + 11 + 12 + 13 all pass, the slice ships. If only 11 + 13 fail, ship per scope-cut #1 with realtime deferred.

---

## 8. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/SLICE7_PLAN.md`.
2. I open C1 — Reports module + 4 query handlers + endpoints + tests (including the `AdminController.cs` deletion + `Admin.cs` DTO deletion + `.sln` add + fixture extension).
3. Each commit ends with: I push, CI runs, I report green/red. Slice ends with the verification recipe in §7.

If you reject or want changes: point at the specific decision in §2 or the specific commit in §3; I revise this document and re-submit.
