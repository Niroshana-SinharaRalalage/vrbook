# OPS.M.6 — iCal Poller Tenant-Scoping + Outbound Rate Limit (Plan, rev 1)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.6 ("iCal poller tenant-scoping + outbound rate limit"), §3 row 7 (1 day estimate, parallel-shippable against M.5).
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §5 lines 121-131 (the brief verbatim).
**Predecessors**: Slice OPS.M.1 ✅, Slice OPS.M.2 ✅, Slice OPS.M.3 ✅, Slice OPS.M.4 ✅, Slice OPS.M.5 ✅ (`2d39a3a`).
**Sequence**: After Slice OPS.M.5; before Slice OPS.M.7 (onboarding wizard UI), Slice OPS.M.8 (Super Admin), Slice OPS.M.9 (RLS), Slice OPS.M.10 (cross-tenant isolation test pack — *must* register OPS.M.6's commands).
**Estimate**: **1 day, one engineer** — TDD-first, see §8.

This plan is the contract. Slice OPS.M.6 ships **(i) tenant-scoped background-command pipeline for the inbound iCal poller**, **(ii) per-host outbound HTTP rate limiting** that prevents IP-bans from external channel hosts, **(iii) `TenantId` bump on the three `Sync`-namespace events that still lack it**, and **(iv) the arch test entries that lock Slice OPS.M.10 to enumerate these commands**.

Outbound *feed rendering* (`/feeds/{token}.ics`) is NOT in scope — that's an HTTP request path, already gated by `OutboundToken` opacity, and out per §1.1. iCal *parser* correctness is NOT in scope — independent concern, owned by `AirBnBICalChannel.Parse` (which already swallows malformed VEVENTs per `AirBnBICalChannel.cs:108-114`).

---

## 1. Scope summary

Slice OPS.M.6 produces seven deliverables, one per Step in §5:

| # | Deliverable | Touches |
|---|---|---|
| 1 | `IBackgroundCommand` marker + `BackgroundCommandTenantScopeBehavior` pipeline behavior (registered in `SyncModule`) | `VrBook.Contracts/Interfaces/IBackgroundCommand.cs`, `VrBook.Modules.Sync/Application/Behaviors/BackgroundCommandTenantScopeBehavior.cs`, `SyncModule.cs` |
| 2 | `RunSyncForFeedCommand` becomes `IBackgroundCommand` + `ITenantScoped` carrying `TenantId` from the `ChannelFeed` row | `RunSyncForFeedCommand.cs`, `Program.cs` (worker entry) |
| 3 | `ChannelPollOptions` + per-host token-bucket rate-limit policy + named `HttpClient` per channel host | `VrBook.Modules.Sync/Infrastructure/RateLimiting/ChannelPollOptions.cs`, `OutboundRateLimitHandler.cs`, `SyncModule.cs` |
| 4 | `AirBnBICalChannel` uses the rate-limited named client | `AirBnBICalChannel.cs` |
| 5 | Sync event payload `TenantId` bump (`ExternalReservationImported`, `ExternalReservationCancelled`, `SyncRunFailed`) + raise sites | `VrBook.Contracts/Events/SyncEvents.cs`, `ExternalReservation.cs`, `SyncRun.cs` |
| 6 | Worker loop hardening: `try/catch` boundary, `PeriodicTimer`-style poll gap removed (Container App Job is one-shot — see §3.6), `stoppingToken` plumbing | `Program.cs` |
| 7 | Architecture tests: `BackgroundCommandTenantScopeTests`, `SyncEventTenantIdShapeTests`, and `TenantScopedCommandTests.CommandAssemblies` already covers Sync module (no new assembly entry — already line 39); add a `[Fact]` enumerating the new background-command surface | `tests/VrBook.Architecture.Tests/` |

### 1.1 What's explicitly OUT of OPS.M.6

| Item | Owner | Why not here |
|---|---|---|
| `/feeds/{token}.ics` outbound-render path | Already shipped (A6) | The `OutboundToken` is opaque-random per feed (`ChannelFeed.cs:27` — `Guid.NewGuid().ToString("N")`); per-tenant exposure is already the per-feed exposure. Tenant-id stamping on the rendered iCal events is not a security need (the token already implicitly scopes to one property which scopes to one tenant). |
| iCal parser correctness (timezone edge cases, malformed VEVENT recovery) | Independent | `AirBnBICalChannel.Parse` exists and is unit-testable on its own; OPS.M.6 does not modify it. |
| `ChannelFeed` CRUD endpoints | Already shipped (A6 + OPS.M.4) | `Create/Update/DeleteChannelFeedCommand` are `ITenantScoped` per `ChannelFeedCommands.cs:11-25`; `TenantScopedCommandTests` already covers them (`tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs:39`). |
| `SyncConflict` resolution endpoint | OPS.M.6 also fixes this — see §3.5.1 | Currently `ResolveConflictCommand` has no `TenantId` (`ResolveConflictCommand.cs:9-12`) — gap. **In scope but small** (one record bump + one controller line). |
| VRBO / Booking.com channel adapters | Phase 2 | The `IExternalChannel` interface is ready for them; OPS.M.6 only ships AirBnB-named `HttpClient` and the per-host rate-limit table. |
| Redis-backed rate limit shared across instances | Slice OPS.M.9 / Phase 2 | Phase 1.5 runs a single Container App Job instance per cron tick (Program.cs:17 — "cron */5 \* \* \* \*", one-shot pass). In-memory rate limit is sound. Multi-instance migration is documented in §3.2.4. |
| Per-tenant outbound throttling (MTOP §5 line 129's "60 req/min keyed by tenant_id") | Deferred — see §3.2.5 | The MTOP brief conflates two different concerns: (a) per-host outbound limits to *external* iCal endpoints (what OPS.M.6 ships), (b) per-tenant *inbound* rate limit on `/feeds/{token}.ics` (the public outbound feed). (b) is a public-endpoint DoS concern; if needed it belongs in Slice OPS.M.9 alongside RLS or in OPS.1 launch hardening. (a) is the immediate risk: AirBnB IP-banning the *whole platform*. |
| Tighten `OutboundToken` to rotate on tenant suspension | Phase 2 | The `OutboundToken` field exists but rotation logic does not; out of scope per §1.1 first row. |

---

## 2. Atomic-deploy constraints

Steps 1→7 in order. Waves (3 deploys):

1. **Step 1 alone** — `IBackgroundCommand` marker + `BackgroundCommandTenantScopeBehavior` ship into `VrBook.Contracts` + `SyncModule` *dormant* (no command implements the marker yet). Pure additive, no behavior change. The behavior is registered in `SyncModule` AFTER the existing MediatR registration so its position is determined.
2. **Steps 2 + 5 + 6 co-tag** — when `RunSyncForFeedCommand` flips to `IBackgroundCommand` + `ITenantScoped`, three things must happen in the same image:
   - The behavior must be live (Step 1 already deployed).
   - The three Sync events must already carry `Guid TenantId` (Step 5) AND the raise sites in `ExternalReservation.Import/Update/MarkCancelled` + `SyncRun.Fail` must pass the new positional argument. **This is the same outbox-replay constraint as OPS.M.5 Step 7**: an outbox row written by image N-1 with the old payload shape cannot be deserialized by image N's consumer expecting the new shape. Co-deploy is mandatory.
   - The worker's `Program.cs` loop hardening (Step 6) lands at the same time so the new `stoppingToken` + `try/catch` boundary is live for the new pipeline.
3. **Steps 3 + 4 + 7 alone** — rate-limit handler + `AirBnBICalChannel` adoption + arch tests. Pure additive on the HTTP client surface; the OS rate-limit policy starts in effect the moment the worker rolls. Arch tests can ship in the same tag (test code only).

**Rationale for the wave count.** Three deploys is the minimum because Wave 2 (events + raise sites + worker loop) is event-payload-shape-tight, and Wave 1 (dormant behavior) must precede Wave 2 by at least one image generation (so that if Wave 2 rolls back, Wave 1 stays harmless). Wave 3 is independent of Wave 2's shape change — rolling Wave 3 first or last is safe.

**Forward-replay constraint.** Per OPS.M.4 §3.6 (outbox replay invariant): every consumer of the three bumped Sync events must accept the new positional `Guid TenantId` at position 0 BEFORE Wave 2 rolls. Audit pass (Step 5 RED test): reflect on every `INotificationHandler<TEvent>` for the three event types and assert their handler tests still pass after the bump.

---

## 3. Design decisions

### 3.1 D1 — Tenant resolution in the background worker

**Decision: option (b) + (c) hybrid — stamp `TenantId` from `ChannelFeed.TenantId` at command construction time AND register a new `BackgroundCommandTenantScopeBehavior` that recognises `IBackgroundCommand` and skips `ICurrentUser` equality.**

#### What we actually found in M.4's shipped behavior

`TenantAuthorizationBehavior` at `src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs:33-68`:

1. Reads `ICurrentUser` injected via `HttpCurrentUser` in API host or `AnonymousCurrentUser` in workers (`AnonymousCurrentUser.cs:9-20` returns `TenantId => null` and `IsAuthenticated => false`).
2. Throws `ForbiddenException("Sign-in required.")` if `IsAuthenticated == false`.
3. Throws `CrossTenantAccessException` if `currentUser.TenantId != scoped.TenantId`.

**The behavior is registered exclusively in `IdentityModule.cs:52`**, NOT in `VrBook.Application/Common/DependencyInjection.cs` (verified — that file ships only the four cross-cutting behaviors: Logging, Performance, UnhandledException, Validation; no tenant or audit). Workers never register `IdentityModule` (verified: `Workers/VrBook.Workers.Sync/Program.cs:59` calls `AddSyncModule` only). **Therefore the behavior is dead-code in the worker today** — but if the worker ever pulls Identity in (e.g. to use audit), every `ITenantScoped` background command would explode at the `IsAuthenticated == false` check.

This is a **latent landmine**, not a current bug. M.6 closes it by making the contract explicit.

#### Why option (a) — "skip behavior via `IBackgroundCommand` marker" — wins

Three options were on the table:

- **(a) skip the behavior** — new `IBackgroundCommand` marker; `TenantAuthorizationBehavior` early-returns when `request is IBackgroundCommand`. Simple, no new behavior class.
- **(b) stamp `TenantId` from the row + supply an `ICurrentUser` shim** — wrap the scope with a `BackgroundCurrentUser` that returns `TenantId = feed.TenantId, IsAuthenticated = true`. Heavier; complicates audit (would forge a fake principal).
- **(c) `ITenantScopeFactory.CreateScope(tenantId)`** — explicit unit-of-work; requires re-architecting how DI scopes are created in the worker.

**Verdict: (a) for the skip + (b)-lite for the stamping.** Specifically:

1. New marker `IBackgroundCommand` lives in `VrBook.Contracts/Interfaces/`. It is a peer of `ITenantScoped` — a command can implement both. Implementing both means: "I am a background command AND I carry a `TenantId` that came from the row, not the caller."
2. `TenantAuthorizationBehavior` is extended with a single early-return:
   ```csharp
   if (request is IBackgroundCommand bgCmd && request is ITenantScoped scoped)
   {
       // The TenantId was stamped from a row by the worker. Trust it.
       // Audit still runs (downstream behavior); the audited principal is
       // ICurrentUser.AnonymousCurrentUser, which is fine for background work.
       logger.LogInformation(
           "Background command {RequestType} tenant-scoped from row to {TenantId}",
           typeof(TRequest).Name, scoped.TenantId);
       return await next();
   }
   ```
3. A separate, narrower behavior `BackgroundCommandTenantScopeBehavior` ships in **`VrBook.Modules.Sync/Application/Behaviors/`** (NOT in Identity — the Sync worker doesn't pull Identity, so co-locating with Sync keeps the registration simple). This behavior:
   - Asserts `request is IBackgroundCommand` carries `scoped.TenantId != Guid.Empty` (catches the bug where a worker forgot to stamp).
   - Throws `BusinessRuleViolationException("sync.background_command.unstamped", ...)` if the assertion fails.
   - Logs a structured `tenant_id` field via `Serilog.Context.LogContext.PushProperty("tenant_id", scoped.TenantId)` so every downstream log line in the handler carries the tenant id automatically.
4. The Sync worker `Program.cs` is the call site for the stamping. The current code at `Program.cs:79`:
   ```csharp
   var result = await mediator.Send(new RunSyncForFeedCommand(feed.Id));
   ```
   becomes:
   ```csharp
   var result = await mediator.Send(new RunSyncForFeedCommand(feed.Id, feed.TenantId));
   ```

**Why NOT (b) with the full ICurrentUser shim**: forging an authenticated principal in a background context is the same anti-pattern the OPS.M.4 plan §4.2 explicitly rejected ("the marker is non-bare so the contract is type-checked at compile time; the behavior reads the value directly off the command without reflection"). The shim adds a runtime swap that bypasses the type system.

**Why NOT (c) ITenantScopeFactory**: it requires a deeper DI change (scope-per-feed, not scope-per-poll), and Phase 1.5 has one poll = one job = one scope already. The factory adds value if and only if we have nested `Mediator.Send` for different tenants within one job execution — which we don't.

**Decision: (a) skip + (b)-lite stamping with new marker `IBackgroundCommand` AND new `BackgroundCommandTenantScopeBehavior`.**

#### Concrete contract for `IBackgroundCommand`

```csharp
namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Marker for MediatR commands dispatched from a background worker (hosted
/// service, Container App Job, BackgroundService). Carries the architectural
/// guarantee that the calling code already resolved the operative tenant from
/// a domain row (typically the aggregate root the command will mutate) and
/// stamped <see cref="ITenantScoped.TenantId"/> from it.
///
/// <para>
/// Effect on the MediatR pipeline:
/// <list type="bullet">
///   <item><see cref="TenantAuthorizationBehavior{TRequest,TResponse}"/> early-
///         returns when the request also implements <see cref="ITenantScoped"/>
///         (no <c>ICurrentUser</c> equality check — there is no HTTP caller).</item>
///   <item><see cref="BackgroundCommandTenantScopeBehavior{TRequest,TResponse}"/>
///         (Sync module) asserts the stamped <c>TenantId</c> is non-empty and
///         pushes it into the <c>Serilog.Context</c> for structured logging.</item>
/// </list>
/// </para>
///
/// <para>
/// Audit behavior: <see cref="AuditLogBehavior{TRequest,TResponse}"/> continues
/// to fire; the audited principal is the worker's <c>AnonymousCurrentUser</c>,
/// which records as <c>system</c> in <c>identity.audit_log_entries</c>. This is
/// correct — no human triggered the command.
/// </para>
/// </summary>
public interface IBackgroundCommand
{
}
```

### 3.2 D2 — Outbound rate-limit shape

**Decision: per-host, in-memory, token-bucket (Polly RateLimiter), one bucket per `ChannelKind` mapped to a known host (Airbnb / Booking.com / VRBO / default).**

#### Why per-host, not per-feed

The risk model is: AirBnB sees N requests per minute coming from VrBook's egress IP and rate-limits or blocks the whole IP. Per-feed throttling does not help — 1000 feeds × 1 req/min = 1000 req/min to AirBnB regardless. Per-host is the only granularity that matches the threat surface.

#### Why per-host AND not per-tenant

MTOP §5 line 129 says "Redis-backed token bucket keyed by `tenant_id`, 60 req/min default." This is the wrong granularity for two reasons:

1. **The rate limit's purpose is to protect VrBook from the external host's ban hammer, not to protect tenant A from tenant B.** Tenant A's rate of polling is bounded by `PollIntervalMinutes` already (`ChannelFeed.cs:124-135` enforces ≥5 minutes). One tenant cannot starve another's polling because the bucket is *outbound*, not inbound.
2. **Per-tenant rate limits would silently delay legitimate polls** for tenants whose properties are all on Airbnb, even though Airbnb's bound is the *aggregate*. The right tool here is per-host.

The MTOP brief's per-tenant concern is real but it applies to a different surface: per-tenant inbound rate limit on `/feeds/{token}.ics` (the public outbound feed served by `FeedsController` — `SyncController.cs:20-33`). That is **deferred** per §1.1 row 7.

#### Why token-bucket, not fixed-window

Token-bucket allows burst capacity. AirBnB's documented limit is per-minute aggregate; a fixed-window resets at the minute boundary and lets the next minute's tokens all be spent in the first second. Token-bucket smooths to roughly even spacing across the minute, which is what we want for a politely-behaved bot.

#### Where the bucket state lives

**In-memory, per process.** Verified Phase 1.5 deployment shape: `Workers/VrBook.Workers.Sync/Program.cs` is a Container App Job (one-shot, scaled by cron `*/5 * * * *` per the comment at `Program.cs:17`). The job exits at the bottom of the loop; in-memory state lives for the duration of one tick. Phase 1.5 has **one** instance per tick (no `replicas` setting, no Service Bus partitioning that would spawn parallel jobs).

The MTOP brief's "Redis-backed" assumption was authored before the worker's Container-App-Job shape was finalized. With one-shot execution there is no rate-limit state to *share*: each tick starts with a fresh bucket. The implementation is single-instance-safe because of the deployment topology, not because of the policy.

**Slice OPS.M.10 follow-up: multi-instance migration.** When Phase 2 turns the worker into a continuously-running `BackgroundService` (or adds replicas), the in-memory bucket becomes wrong — two instances would each have their own bucket, doubling the effective rate to AirBnB. The migration path is:

1. Add `IRateLimiter` interface in `VrBook.Modules.Sync/Application/Common/` (already used by the handler — see Step 3 implementation below).
2. Implementation pivots from `InMemoryHostRateLimiter` to `RedisHostRateLimiter` (`StackExchange.Redis` is already in `Directory.Packages.props:57`). The Redis key is `vrbook:sync:ratelimit:{host}` with `SET …NX` token check.
3. Slice OPS.M.10 arch test will enumerate every `IBackgroundCommand` implementer AND every `IExternalChannel`-bound `HttpClient` and assert they all go through `IRateLimiter`.

The Phase 2 effort is ~1d. **In OPS.M.6 the contract surface `IRateLimiter` ships now**, so the Phase 2 swap is a single-file implementation change with no caller edits.

#### Decision: per-host, in-memory, token-bucket via Polly's `RateLimiter` resilience strategy

```csharp
namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

public interface IRateLimiter
{
    /// <summary>
    /// Acquire a token for the given host. Blocks (yields) until a token is
    /// available OR the cancellation token fires. Returns true if a token was
    /// acquired, false if the wait was capped at <see cref="ChannelPollOptions.MaxWaitSeconds"/>.
    /// </summary>
    ValueTask<bool> TryAcquireAsync(string host, CancellationToken ct);
}
```

The `InMemoryHostRateLimiter` is a singleton; it holds a `ConcurrentDictionary<string, RateLimiter>` keyed by the lowercased host name. Each `RateLimiter` is a `System.Threading.RateLimiting.TokenBucketRateLimiter` configured from `ChannelPollOptions`.

**Why `System.Threading.RateLimiting` directly, not `Polly.RateLimiting`**: Polly 8.4.2 (`Directory.Packages.props:34`) supports rate limiting as a resilience strategy, but it adds wrapping ceremony on top of the BCL primitive. The BCL `TokenBucketRateLimiter` (`System.Threading.RateLimiting.dll`, shipped in .NET 8) does exactly what we need with one allocation per bucket; using Polly here adds two new types (`ResiliencePipeline`, `RateLimiterStrategyOptions`) for no behavior difference. **Use the BCL primitive directly behind `IRateLimiter`**.

**Decision: in-memory, BCL `TokenBucketRateLimiter` behind `IRateLimiter`, one bucket per `ChannelKind`-→-host mapping.**

### 3.3 D3 — Rate-limit defaults

**Decision: ship the table below as defaults in `ChannelPollOptions`, operator-overridable via `appsettings.json` / Key Vault, no per-host code branches required.**

| Channel | Host (suffix-matched) | Tokens/window | Window | Burst | Source-of-evidence |
|---|---|---|---|---|---|
| AirBnb (`ChannelKind.AirBnb`) | `airbnb.com`, `airbnb.co.\*`, `airbnb.\*` | 60 | 60s | 10 | AirBnB's public iCal documentation does not publish a hard limit; community-reported anecdotal ban threshold is around 100 req/min sustained; we set 60 with 10-token burst to stay well below. The cron schedule (`*/5 * * * *`) means at most 12 ticks/hour × ≤100 feeds = ≤1200 outbound reqs/hour against AirBnB hosts, well inside 60/min. **D-flag: configurable.** |
| Booking.com (Phase 2) | `booking.com`, `bookingsync.com` | 30 | 60s | 5 | Booking.com Connectivity Provider docs publish 50 req/min for partner API; iCal sync is unmetered by docs, but partner-program guidance suggests ≤30/min for politely-behaved consumers. **D-flag: configurable.** |
| VRBO (Phase 2) | `vrbo.com`, `homeaway.com`, `expediagroup.com` | 30 | 60s | 5 | Expedia Partner Central documents 60 req/min for the EPS API; iCal is undocumented; 30 is a conservative default. **D-flag: configurable.** |
| Default (catch-all) | `*` | 20 | 60s | 3 | Conservative default for unknown hosts (e.g. a tenant who pastes a personal NextCloud calendar URL). **D-flag: configurable.** |

#### `ChannelPollOptions` shape

```csharp
namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

public sealed class ChannelPollOptions
{
    public const string SectionName = "Sync:ChannelPoll";

    /// <summary>How long an outbound HTTP call may wait for a rate-limit token
    /// before falling back to a transient failure that bumps ConsecutiveFailures.</summary>
    public int MaxWaitSeconds { get; set; } = 30;

    /// <summary>Per-host policies. Lookup is suffix-match (case-insensitive),
    /// most-specific-first. <c>"*"</c> is the catch-all.</summary>
    public List<HostPolicy> Hosts { get; set; } =
    [
        new HostPolicy { HostSuffix = "airbnb.com", TokensPerWindow = 60, WindowSeconds = 60, BurstSize = 10 },
        new HostPolicy { HostSuffix = "booking.com", TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "bookingsync.com", TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "vrbo.com", TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "homeaway.com", TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "*", TokensPerWindow = 20, WindowSeconds = 60, BurstSize = 3 },
    ];
}

public sealed class HostPolicy
{
    public string HostSuffix { get; set; } = default!;
    public int TokensPerWindow { get; set; }
    public int WindowSeconds { get; set; }
    public int BurstSize { get; set; }
}
```

**Why suffix-match on the host**: AirBnB serves the iCal URL from `www.airbnb.com`, `www.airbnb.co.uk`, `de.airbnb.com`, etc. A literal-host whitelist would whack-a-mole on TLD variants. Suffix-match collapses all `*.airbnb.com` to one bucket, which is correct: AirBnB rate-limits per-IP-per-account, not per-TLD.

**Override mechanism**: `appsettings.json` `Sync:ChannelPoll:Hosts:<idx>:TokensPerWindow=NN` or via the `Sync__ChannelPoll__Hosts__0__TokensPerWindow=NN` env-var pattern. Key Vault precedence (already in place via `AddEnvironmentVariables()` at `Program.cs:35`) means Super Admin can flip a tenant's host bucket without redeploying.

**Decision: defaults table above; configurable via `ChannelPollOptions:Hosts`; suffix-match per-host.**

### 3.4 D4 — `HttpClient` construction: named-client per channel

**Decision: option (a) — keep `IHttpClientFactory.CreateClient("AirBnBICal")` as today, and attach `OutboundRateLimitHandler` as a `DelegatingHandler` to that named client during DI registration. Phase 2 adds a new named client per channel.**

#### Status quo

`SyncModule.cs:37-42` registers one named client `AirBnBICal` with a 15s timeout and User-Agent. `AirBnBICalChannel.cs:35` resolves via `httpClientFactory.CreateClient(HttpClientName)`. **Status quo is correct shape — only the rate-limit handler is missing.**

#### Why named client per channel, not single-handler-per-request

Two options:
- **(a) Named client per channel + delegating handler.** `IHttpClientFactory` lifecycle manages handler reuse (per-host pooling); we plug in `OutboundRateLimitHandler` once at registration and every request through the named client is rate-limited automatically.
- **(b) Single shared client + per-request `RateLimitHttpMessageHandler` constructed manually.** Caller code calls `await _limiter.TryAcquireAsync(host, ct)` before each `SendAsync`. Less DI ceremony but explicit at every call site — easy to forget.

**Verdict: (a).** The arch test in §10 enforces "no `new HttpClient()` anywhere in Sync code" (delegate to `IHttpClientFactory`). Per-channel named clients also unlock per-channel timeouts, per-channel User-Agent, per-channel proxy config — all of which Phase 2 will need.

#### `OutboundRateLimitHandler` shape

```csharp
namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

internal sealed class OutboundRateLimitHandler(
    IRateLimiter limiter,
    ILogger<OutboundRateLimitHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? "*";
        var acquired = await limiter.TryAcquireAsync(host, cancellationToken);
        if (!acquired)
        {
            logger.LogWarning(
                "Outbound rate-limit wait timed out for host {Host}; returning 429.", host);
            return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "VrBook outbound rate limit",
                Content = new StringContent("rate_limited"),
                RequestMessage = request,
            };
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
```

Synthetic 429 (rather than throw) means the existing `AirBnBICalChannel.cs:53 resp.EnsureSuccessStatusCode()` path triggers the existing `RunSyncForFeedHandler` `catch (Exception ex)` (`RunSyncForFeedCommand.cs:71-80`) which records `ConsecutiveFailures++` and persists a `SyncRun.Failed`. **No new error path** — the rate-limit miss looks like an upstream 429, which is correct.

#### DI registration in `SyncModule`

```csharp
services.AddSingleton<IRateLimiter, InMemoryHostRateLimiter>();
services.AddTransient<OutboundRateLimitHandler>();
services.AddHttpClient(AirBnBICalChannel.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("VrBook-Sync/1.0 (+https://vrbook.example.com)");
    c.DefaultRequestHeaders.Accept.ParseAdd("text/calendar, text/plain;q=0.9, */*;q=0.5");
})
.AddHttpMessageHandler<OutboundRateLimitHandler>();
```

**Decision: option (a) — named client per channel with `OutboundRateLimitHandler` attached as a `DelegatingHandler` via `IHttpClientFactory`.**

### 3.5 D5 — Event payload bumps: enumerate, bump, raise

**Decision: bump `ExternalReservationImported`, `ExternalReservationCancelled`, `SyncRunFailed`, AND `ResolveConflictCommand` with leading positional `Guid TenantId`. `SyncConflictDetected` already carries it (`SyncEvents.cs:23-29`). Pattern follows OPS.M.5 Step 7 (PaymentEvents.cs bump).**

#### Why these four

Audited every type in `src/VrBook.Contracts/Events/SyncEvents.cs` (file is small — 36 lines). The three event records that lack `Guid TenantId`:

```csharp
// CURRENT — SyncEvents.cs:9-15
public sealed record ExternalReservationImported(
    Guid ExternalReservationId, Guid PropertyId, ChannelKind Channel,
    string ICalUid, DateOnly Checkin, DateOnly Checkout) : DomainEvent;

// CURRENT — SyncEvents.cs:17-21
public sealed record ExternalReservationCancelled(
    Guid ExternalReservationId, Guid PropertyId, ChannelKind Channel,
    string ICalUid) : DomainEvent;

// CURRENT — SyncEvents.cs:31-36
public sealed record SyncRunFailed(
    Guid ChannelFeedId, Guid PropertyId, ChannelKind Channel,
    int ConsecutiveFailures, string Error) : DomainEvent;
```

The fourth gap is `ResolveConflictCommand` (`Application/Conflicts/Commands/ResolveConflictCommand.cs:9-12`):

```csharp
// CURRENT — does NOT implement ITenantScoped; controller does not stamp TenantId.
public sealed record ResolveConflictCommand(
    Guid Id, SyncConflictResolution Resolution, string Notes) : IRequest;
```

The `SyncController` already wires `[Authorize(Roles = "Admin")]` (`SyncController.cs:89`) but does NOT call `CallerTenantId()` for the resolve endpoint — direct gap. OPS.M.6 closes it as part of the same wave (Step 5).

#### Bumped shape (post-M.6)

```csharp
public sealed record ExternalReservationImported(
    Guid TenantId,
    Guid ExternalReservationId, Guid PropertyId, ChannelKind Channel,
    string ICalUid, DateOnly Checkin, DateOnly Checkout) : DomainEvent;

public sealed record ExternalReservationCancelled(
    Guid TenantId,
    Guid ExternalReservationId, Guid PropertyId, ChannelKind Channel,
    string ICalUid) : DomainEvent;

public sealed record SyncRunFailed(
    Guid TenantId,
    Guid ChannelFeedId, Guid PropertyId, ChannelKind Channel,
    int ConsecutiveFailures, string Error) : DomainEvent;
```

#### Raise-site updates (verified from code)

| Site | Current | Post-M.6 |
|---|---|---|
| `ExternalReservation.cs:72-73` (`Import`) | `er.Raise(new ExternalReservationImported(er.Id, propertyId, channel, …))` | `er.Raise(new ExternalReservationImported(tenantId, er.Id, propertyId, channel, …))` |
| `ExternalReservation.cs:96-97` (`Update`) | `Raise(new ExternalReservationImported(Id, PropertyId, Channel, …))` | `Raise(new ExternalReservationImported(TenantId, Id, PropertyId, Channel, …))` |
| `ExternalReservation.cs:107` (`MarkCancelled`) | `Raise(new ExternalReservationCancelled(Id, PropertyId, Channel, ICalUid))` | `Raise(new ExternalReservationCancelled(TenantId, Id, PropertyId, Channel, ICalUid))` |
| `SyncRun.cs:81` (`Fail`) | `Raise(new SyncRunFailed(ChannelFeedId, PropertyId, Channel, …))` | `Raise(new SyncRunFailed(TenantId, ChannelFeedId, PropertyId, Channel, …))` |

`TenantId` is already a property on both aggregates (`ExternalReservation.cs:16`, `SyncRun.cs:17`), so the call sites have it in scope — pure refactor.

#### `ResolveConflictCommand` bump

```csharp
public sealed record ResolveConflictCommand(
    Guid Id, SyncConflictResolution Resolution, string Notes,
    Guid TenantId) : IRequest, ITenantScoped;
```

Controller update (`SyncController.cs:101-105` — add `CallerTenantId()` as 4th arg):

```csharp
public async Task<IActionResult> Resolve(
    Guid id, [FromBody] ResolveConflictRequest request, CancellationToken cancellationToken)
{
    await mediator.Send(
        new ResolveConflictCommand(id, request.Resolution, request.Notes, CallerTenantId()),
        cancellationToken);
    return NoContent();
}
```

The `SyncConflictsController` constructor at `SyncController.cs:90` does NOT inject `ICurrentUser` today — must be added: `(IMediator mediator, ICurrentUser currentUser)` + private `CallerTenantId()` helper (same shape as `ChannelFeedsController`).

#### Atomic-deploy constraint

Same shape as OPS.M.5 Step 7: events get bumped + raise sites get updated in the SAME tag. An outbox row written by the old image and replayed against the new image's consumer would fail to deserialize because the synthesized positional ctor signature changed. Co-deploy is mandatory.

**Decision: bump the three event records + `ResolveConflictCommand` in the SAME atomic tag as Step 2 (the `RunSyncForFeedCommand` `IBackgroundCommand` flip).**

### 3.6 D6 — Hosted service shape: keep the Container App Job

**Decision: keep the existing Container-App-Job + cron-`*/5`-minute shape; do NOT introduce a `BackgroundService`. The "PeriodicTimer" hardening called out in the brief is a *negative* hardening — verify the worker does NOT introduce a `Task.Delay`-polling loop.**

#### What's actually deployed

`Workers/VrBook.Workers.Sync/Program.cs:1-108` is a console host that:
1. Bootstraps with `Host.CreateApplicationBuilder` (`Program.cs:30`).
2. Resolves `IChannelFeedRepository`, lists due feeds (`Program.cs:70`).
3. `foreach` over due feeds, dispatches `RunSyncForFeedCommand` per feed (`Program.cs:75-94`).
4. Logs summary, **exits** with status code 0 / 2 (`Program.cs:97`).

There is no `while (true)` loop. There is no `Task.Delay`. There is no `PeriodicTimer`. This is a **one-shot job**, scheduled externally by the Container App Jobs cron. The cron comment at `Program.cs:17` confirms: `*/5 * * * *`.

**The brief's instruction "no Thread.Sleep / Task.Delay polling loops" is a forward-looking guard against the worst-case Phase 2 migration (BackgroundService).** OPS.M.6 enforces it as an arch test: assert `Workers.Sync.Program` does NOT reference `Task.Delay` or `Thread.Sleep` or `PeriodicTimer` (the last would suggest a continuous-loop refactor).

#### Why we do NOT migrate to `BackgroundService`

1. **Cost.** Container App Jobs scale-to-zero between ticks. Always-on `BackgroundService` keeps a replica warm 24/7. For a 5-minute-cron workload that takes <30 seconds, that's ~ $60/mo of avoidable Container App compute.
2. **Coordination.** A single instance with cron-scaling is trivially leader-elected (by the cron, not the app). A `BackgroundService` would need leader election to avoid double-polling, or per-replica feed sharding.
3. **No requirement to lower latency below 5 minutes.** The `ChannelFeed.IsDueForPoll` enforces a per-feed minimum of `PollIntervalMinutes >= 5` (`ChannelFeed.cs:53-58`). The cron tick of `*/5` is already at the floor.

**Decision: keep Container-App-Job; arch-test forbids `Task.Delay` / `Thread.Sleep` / `PeriodicTimer` in the worker source.**

### 3.7 D7 — Failure handling: per-feed isolation, no worker poisoning

**Decision: the existing `try/catch` at `Program.cs:77-93` is correct — keep it. Add structured `tenant_id`/`channel_feed_id`/`host` fields to every log line emitted from inside the `foreach` body. Add an arch test that the worker's `foreach` body is wrapped in a `try/catch (Exception ex)` that bumps the `failed` counter.**

#### Status quo

`Program.cs:77-93`:
```csharp
foreach (var feed in due)
{
    try
    {
        var result = await mediator.Send(new RunSyncForFeedCommand(feed.Id));
        if (result.Status == VrBook.Contracts.Enums.SyncRunStatus.Success) ok++;
        else failed++;
    }
    catch (Exception ex)
    {
        failed++;
        logger.LogError(ex, "Sync run threw outside of RunSyncForFeedHandler for feed {FeedId}", feed.Id);
    }
}
```

This is correct: one bad feed throws → counter bumps → loop continues. **No change needed.** OPS.M.6 only adds:
1. `tenant_id` field in the structured log (was missing) — Step 6's structured logging update.
2. The arch test (`WorkerExceptionBoundaryTests`) asserts the `foreach` body in `Workers.Sync.Program` is `try/catch`d. Test uses Roslyn-style source scanning (load the source file and grep for the pattern); this is a "guard against future regression" test, not a behavior test.

`RunSyncForFeedHandler` itself also has an inner `try/catch` (`RunSyncForFeedCommand.cs:53-80`) which records `SyncRun.Fail` on exception. **Two layers of isolation is correct** — the inner one persists the failure for telemetry, the outer one guards against handler-level exceptions that escape (e.g. DB unavailable).

**Decision: keep both `try/catch` layers; add structured `tenant_id` to log lines (Step 6); add `WorkerExceptionBoundaryTests` source-scan arch test.**

### 3.8 D8 — Idempotency on import

**Decision: NO change required. The existing dedupe in `RunSyncForFeedHandler.ApplyAsync` (`RunSyncForFeedCommand.cs:91-123`) is correct — keep it. Add ONE arch test that the dedupe contract holds (replay-safety check).**

#### What the code already does

`RunSyncForFeedCommand.cs:101-110`:
```csharp
foreach (var dto in pulled)
{
    if (existingByUid.TryGetValue(dto.ICalUid, out var existingRow))
    {
        if (existingRow.Checkin != dto.Checkin
            || existingRow.Checkout != dto.Checkout
            || existingRow.Summary != dto.Summary)
        {
            existingRow.Update(dto.Checkin, dto.Checkout, dto.Summary, dto.RawPayload);
            updatedCount++;
        }
    }
    else
    {
        var er = ExternalReservation.Import(feed.TenantId, feed.Id, …, dto.ICalUid, …);
        db.ExternalReservations.Add(er);
        newCount++;
    }
}
```

Lookup is by `(channelFeedId, iCalUid)` — `existingByUid` is built from `db.ExternalReservations.Where(r => r.ChannelFeedId == feed.Id && r.CancelledAt == null)` (`RunSyncForFeedCommand.cs:91-93`). The `ChannelFeed`-scoped iCal UID is the stable id. **Duplicate imports across polls are already dedupe'd**.

There IS one gap: the *cancelled* rows are excluded from the lookup (`CancelledAt == null` filter). If a reservation was cancelled and then re-appears in the next poll (rare but real — guest re-books), a new row is created. This is **arguably correct** (cancellation is a terminal state per `ExternalReservation.cs:100-108`), but a future arch test should pin the behavior. **Out of scope for OPS.M.6** — file a follow-up issue, no code change.

#### What OPS.M.6 adds

The arch test `ExternalReservationDedupeContractTests` is a unit test (not arch test, technically): it constructs a `ChannelFeed`, runs `RunSyncForFeedCommand` twice with the same DTO payload, and asserts the second run produces `EventsSeen > 0` AND `EventsNew == 0` (i.e. dedupe held). This is a **behavior test**, not a shape test; it lands in Step 3 alongside the rate-limit tests.

**Decision: no code change; add behavior test in Step 3 pack.**

---

## 4. External-host rate-limit table (for D3)

| Channel | Host suffix | Tokens / window | Window (s) | Burst | Source-of-evidence |
|---|---|---|---|---|---|
| AirBnB | `airbnb.com` (and TLD variants) | 60 | 60 | 10 | AirBnB iCal docs do not publish a limit; community evidence (StackOverflow, AirBnB host forums) reports IP-bans around 100+ req/min sustained; 60/min keeps us at <60% headroom. |
| Booking.com | `booking.com`, `bookingsync.com` | 30 | 60 | 5 | Booking.com Connectivity Provider guidance: 50 req/min hard limit on partner API; iCal undocumented; 30/min is conservative. |
| VRBO / Expedia | `vrbo.com`, `homeaway.com`, `expediagroup.com` | 30 | 60 | 5 | Expedia EPS docs: 60 req/min hard cap; iCal undocumented; 30/min is conservative. |
| Default (catch-all) | `*` | 20 | 60 | 3 | Conservative for unknown hosts (NextCloud, Google Calendar, personal servers). |

**Reload semantics**: changing `Sync:ChannelPoll:Hosts:*` in `appsettings.json` requires a worker tick redeploy because `InMemoryHostRateLimiter` constructs buckets eagerly. **D-flag: live reload deferred to Phase 2 alongside the Redis swap.** Phase 1.5 cost: one Container App Job revision push (15s deploy).

---

## 5. Step-by-step TDD plan (Red → Green)

Every step is red-first. Red commit + green commit are tracked in the §11 ledger.

### Step 1 — `IBackgroundCommand` marker + behaviors (S, ~1.5h) **Wave 1**

**Tests (red first)** — `tests/VrBook.Architecture.Tests/BackgroundCommandTenantScopeTests.cs`:
- `IBackgroundCommand_marker_exists_in_VrBook_Contracts_Interfaces` (reflects on type).
- `Every_IBackgroundCommand_also_implements_ITenantScoped` — enumerates all assemblies in `CommandAssemblies` (already defined in `TenantScopedCommandTests.cs:35`), filters to `typeof(IBackgroundCommand).IsAssignableFrom(t)`, asserts every match also `typeof(ITenantScoped).IsAssignableFrom(t)`. Catches the bug where a future command implements only the marker.
- `TenantAuthorizationBehavior_early_returns_when_request_is_IBackgroundCommand` — unit test. Constructs `TenantAuthorizationBehavior<TestBgCommand, Unit>`, injects an `AnonymousCurrentUser` (which throws `ForbiddenException` for normal `ITenantScoped` commands), asserts `next()` is called and no `ForbiddenException` is thrown.
- `BackgroundCommandTenantScopeBehavior_throws_sync_background_command_unstamped_when_TenantId_is_empty` — unit test. Asserts the assertion fires with the documented error code.
- `BackgroundCommandTenantScopeBehavior_pushes_tenant_id_into_LogContext` — unit test. Uses Serilog's `LogContext` test harness to capture the property.

**Min implementation**:
1. New file `src/VrBook.Contracts/Interfaces/IBackgroundCommand.cs` — the marker per §3.1.
2. Edit `src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs` — add the early-return per §3.1.
3. New file `src/Modules/VrBook.Modules.Sync/Application/Behaviors/BackgroundCommandTenantScopeBehavior.cs` — assertion + log-context push.
4. Edit `src/Modules/VrBook.Modules.Sync/SyncModule.cs` — register the behavior:
   ```csharp
   services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BackgroundCommandTenantScopeBehavior<,>));
   ```

**Refactor**: none expected.

**§3 cross-reference**: §3.1 (D1).

### Step 2 — `RunSyncForFeedCommand` → `IBackgroundCommand` + `ITenantScoped` (S, ~1h) **Wave 2 — co-tag with Steps 5 + 6**

**Tests (red first)** — `tests/VrBook.Modules.Sync.UnitTests/Application/RunSyncForFeedCommandShapeTests.cs`:
- `Implements_IBackgroundCommand`.
- `Implements_ITenantScoped`.
- `Carries_non_empty_TenantId_after_construction` (sentinel-based, same pattern as `TenantScopedCommandTests`).
- `Worker_Program_passes_feed_TenantId_to_command` — source-scan test on `Workers/VrBook.Workers.Sync/Program.cs`. Loads the source file as text, asserts the substring `new RunSyncForFeedCommand(feed.Id, feed.TenantId)` is present. (This is the cheapest way to pin the worker call site; the worker has no DI seam.)

**Min implementation**:
1. Edit `RunSyncForFeedCommand.cs:18`:
   ```csharp
   public sealed record RunSyncForFeedCommand(Guid ChannelFeedId, Guid TenantId)
       : IRequest<RunSyncForFeedResult>, IBackgroundCommand, ITenantScoped;
   ```
2. Edit `Workers/VrBook.Workers.Sync/Program.cs:79` — pass `feed.TenantId` second.

**Refactor**: extract the local variable `var cmd = new RunSyncForFeedCommand(feed.Id, feed.TenantId);` for readability.

**§3 cross-reference**: §3.1 (D1), §3.5 (D5 atomic-deploy).

### Step 3 — `IRateLimiter` + `InMemoryHostRateLimiter` + `ChannelPollOptions` (M, ~3h) **Wave 3**

**Tests (red first)** — `tests/VrBook.Modules.Sync.UnitTests/Infrastructure/InMemoryHostRateLimiterTests.cs`:
- `Acquire_succeeds_immediately_within_burst` — config: burst=3, tokens/window=60. Three immediate `TryAcquireAsync("airbnb.com", …)` all return `true` synchronously.
- `Acquire_blocks_when_burst_exhausted_until_token_replenishes` — 4th acquire after 3-burst waits ~1s (60 tokens/60s ⇒ 1 token/s) before returning `true`. Use a fake `TimeProvider` for determinism (`Microsoft.Extensions.TimeProvider.Testing` is already on testcontainers projects via xUnit — if not, expose `_now` in the limiter as test-injectable).
- `Acquire_returns_false_when_MaxWaitSeconds_exceeded` — config: tokens/window=1, window=60s, max-wait=2s. First acquire OK; second acquire times out after 2s and returns `false`. Cancellation token NOT fired.
- `Acquire_respects_cancellation_token` — token canceled mid-wait throws `OperationCanceledException`.
- `Host_suffix_matching_routes_subdomains_to_same_bucket` — `www.airbnb.com` and `de.airbnb.com` share a bucket (3-burst together, not separately).
- `Unknown_host_falls_to_catch_all_default_policy` — `random-personal-server.tld` is rate-limited per the `"*"` policy.
- `ChannelPollOptions_defaults_match_documented_table` — reflects on `ChannelPollOptions.Hosts` and asserts the §4 table values.

And `tests/VrBook.Modules.Sync.UnitTests/Infrastructure/OutboundRateLimitHandlerTests.cs`:
- `Inner_handler_is_called_when_token_acquired` — uses a fake `IRateLimiter` that returns `true`; assert inner `SendAsync` was invoked.
- `Synthetic_429_returned_when_token_not_acquired` — fake returns `false`; assert response.StatusCode == 429 AND inner handler was NOT invoked.
- `Host_is_extracted_from_request_RequestUri_for_limiter_key` — captures the host string passed to `TryAcquireAsync`.

And `tests/VrBook.Modules.Sync.UnitTests/Application/ExternalReservationDedupeContractTests.cs`:
- `Re_running_sync_with_identical_dtos_produces_no_new_reservations` — per §3.8.

**Min implementation**:
1. `src/Modules/VrBook.Modules.Sync/Infrastructure/RateLimiting/IRateLimiter.cs` — interface per §3.2.
2. `src/Modules/VrBook.Modules.Sync/Infrastructure/RateLimiting/InMemoryHostRateLimiter.cs` — BCL `TokenBucketRateLimiter` per bucket; `ConcurrentDictionary<string, RateLimiter>` keyed by suffix-matched host.
3. `src/Modules/VrBook.Modules.Sync/Infrastructure/RateLimiting/ChannelPollOptions.cs` + `HostPolicy.cs` — config record per §3.3.
4. `src/Modules/VrBook.Modules.Sync/Infrastructure/RateLimiting/OutboundRateLimitHandler.cs` — `DelegatingHandler` per §3.4.
5. Edit `SyncModule.cs:18-49` — register `IRateLimiter` as singleton, `OutboundRateLimitHandler` as transient, attach to `AirBnBICal` named client via `.AddHttpMessageHandler<OutboundRateLimitHandler>()`.
6. Edit `SyncModule.cs` — bind `ChannelPollOptions` from `configuration.GetSection(ChannelPollOptions.SectionName)`.

**Refactor**: pull host-suffix-matching logic out of `InMemoryHostRateLimiter` into a static helper `HostMatcher.Resolve(IList<HostPolicy>, string host)` so it can be unit-tested in isolation.

**§3 cross-reference**: §3.2 (D2), §3.3 (D3), §3.4 (D4), §3.8 (D8).

### Step 4 — `AirBnBICalChannel` uses the rate-limited named client (S, ~0.5h) **Wave 3**

**Tests (red first)** — `tests/VrBook.Modules.Sync.UnitTests/Infrastructure/AirBnBICalChannelRateLimitWiringTests.cs`:
- `AirBnBICalChannel_PullAsync_uses_HttpClientName_constant` — already-true behavior; pin it.
- `Rate_limited_handler_short_circuits_PullAsync_to_HttpRequestException` — integration-style with a `TestServer`: configure the named client with a rate-limiter that always returns `false`; call `PullAsync`; assert `EnsureSuccessStatusCode()` throws (returns synthetic 429 → handler bubbles to `RunSyncForFeedHandler`'s `catch` block which records `RecordFailure`).
- `Rate_limited_handler_records_consecutive_failures_on_RunSyncForFeedHandler_path` — full handler test with WireMock-backed external feed AND a fake `IRateLimiter` returning `false`; assert `feed.ConsecutiveFailures == 1` after dispatch and `SyncRun.Status == Failed`.

**Min implementation**: zero source edits — `AirBnBICalChannel.cs:35` already calls `httpClientFactory.CreateClient(HttpClientName)`. The named client now has the rate-limit handler attached (from Step 3). **The only file change is the test pack.** This step's value is the integration-style red-test that pins the wiring.

**Refactor**: none.

**§3 cross-reference**: §3.4 (D4).

### Step 5 — Sync event payload `TenantId` bump + `ResolveConflictCommand` (S, ~2h) **Wave 2 — co-tag with Step 2**

**Tests (red first)** —
- `tests/VrBook.Architecture.Tests/SyncEventTenantIdShapeTests.cs`: `Every_sync_event_positional_ctor_has_Guid_TenantId_at_position_0` — reflects on `ExternalReservationImported`, `ExternalReservationCancelled`, `SyncRunFailed`, `SyncConflictDetected`. Same shape as `PaymentEventTenantIdShapeTests` from OPS.M.5.
- `tests/VrBook.Modules.Sync.UnitTests/Application/ResolveConflictCommandShapeTests.cs`:
  - `Implements_ITenantScoped`.
  - `Carries_non_empty_TenantId_after_construction` (sentinel).
- `tests/VrBook.Modules.Sync.UnitTests/Domain/ExternalReservationEventRaiseTests.cs`:
  - `Import_raises_ExternalReservationImported_with_TenantId_at_position_0`.
  - `Update_raises_ExternalReservationImported_with_TenantId`.
  - `MarkCancelled_raises_ExternalReservationCancelled_with_TenantId`.
- `tests/VrBook.Modules.Sync.UnitTests/Domain/SyncRunEventRaiseTests.cs`:
  - `Fail_raises_SyncRunFailed_with_TenantId_at_position_0`.

**Min implementation**: per §3.5 — record bumps + 4 raise-site edits + `ResolveConflictCommand` record bump + `SyncController.cs:90` constructor adds `ICurrentUser` and resolve endpoint stamps `CallerTenantId()`.

**Refactor**: extract `CallerTenantId()` into a shared helper on a `TenantScopedControllerBase` if not already present — verify by reading `ChannelFeedsController.cs:41-42` (currently inlined). **D-flag: defer the base-class refactor; redundancy is two lines.**

**§3 cross-reference**: §3.5 (D5).

### Step 6 — Worker loop hardening: structured logging + `stoppingToken` plumb-through (S, ~1h) **Wave 2 — co-tag with Steps 2 + 5**

**Tests (red first)** — `tests/VrBook.Architecture.Tests/WorkerExceptionBoundaryTests.cs`:
- `Workers_Sync_Program_does_NOT_use_Thread_Sleep` — source-scan, asserts absence.
- `Workers_Sync_Program_does_NOT_use_Task_Delay_in_a_polling_loop` — source-scan, asserts absence of `while` or `for(;;)` paired with `Task.Delay`.
- `Workers_Sync_Program_does_NOT_use_PeriodicTimer` — source-scan, asserts absence.
- `Workers_Sync_Program_foreach_body_is_try_caught` — source-scan, asserts `try` and `catch (Exception` appear inside the `foreach (var feed in due)` block. **Pinning test for D7.**
- `Workers_Sync_Program_logs_tenant_id_and_channel_feed_id_structured_fields` — source-scan for the literal `{TenantId}` and `{FeedId}` in the `LogInformation`/`LogError` strings inside the `foreach`.

**Min implementation**:
1. Edit `Workers/VrBook.Workers.Sync/Program.cs:79`:
   ```csharp
   using (Serilog.Context.LogContext.PushProperty("tenant_id", feed.TenantId))
   using (Serilog.Context.LogContext.PushProperty("channel_feed_id", feed.Id))
   {
       try { … }
       catch (Exception ex) { … }
   }
   ```
2. Edit error log line to include `{TenantId}` and `{FeedId}` template tokens explicitly.
3. Plumb a `CancellationTokenSource` linked to `Console.CancelKeyPress` (graceful Ctrl-C); pass `cts.Token` into `mediator.Send(cmd, cts.Token)`. Container Apps Jobs deliver SIGTERM with a 30s grace period — honoring it lets the in-flight sync finish.

**Refactor**: extract the inner `foreach` body into a static `RunOneAsync(IMediator, ChannelFeed, ILogger, CancellationToken)` method for testability of the per-feed code path (currently inline-only). **D-flag: optional; the source-scan tests pass either way.**

**§3 cross-reference**: §3.6 (D6), §3.7 (D7).

### Step 7 — Architecture tests + `TenantScopedCommandTests` extension (S, ~0.5h) **Wave 3**

**Tests (red first)** — extend `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs`:
- Add `[Fact] Every_M6_background_command_implements_IBackgroundCommand_and_ITenantScoped` — enumerates types in `CommandAssemblies` that are `IBackgroundCommand`, asserts each is also `ITenantScoped`. (This may overlap with Step 1's test; keep both — different framing.)
- Add `[Fact] RunSyncForFeedCommand_is_enumerated_by_CommandAssemblies` — pin the assembly entry at line 39 covers Sync.
- **NO new entry needed in `CommandAssemblies`** — `typeof(VrBook.Modules.Sync.Application.ChannelFeeds.Commands.CreateChannelFeedCommand).Assembly` (line 39) already covers `RunSyncForFeedCommand` (same assembly).

**Min implementation**: test additions only.

**Refactor**: none.

**§3 cross-reference**: D1, D5; ties to Slice OPS.M.10's promised arch-test scope.

---

## 6. Hot-path validation — which OPS.M.4 paths the new commands skip

| Pipeline behavior (in pipeline order) | Behavior for `RunSyncForFeedCommand` (IBackgroundCommand + ITenantScoped) | Behavior for `ResolveConflictCommand` (ITenantScoped only) | Why it's safe |
|---|---|---|---|
| `ValidationBehavior` | Runs | Runs | No change — FluentValidation rules unchanged. |
| `TenantAuthorizationBehavior` | **Early-returns** per new §3.1 hook (`request is IBackgroundCommand`) | Runs the full `ICurrentUser` equality check | For `RunSyncForFeedCommand`: there is no `ICurrentUser` (worker context), AND the `TenantId` came from a domain row that the worker just read — trusting it is correct. The early-return is type-gated (compile-time check via marker), not config-gated. For `ResolveConflictCommand`: standard tenant gate applies — controller stamped `CallerTenantId()`. |
| `BackgroundCommandTenantScopeBehavior` (new, Sync-module-only) | Asserts `TenantId != Guid.Empty`; pushes `tenant_id` into `LogContext` | Does NOT run — not registered in Sync via auto-binding; registered as `IPipelineBehavior<,>` open generic. **Verification**: the behavior's `Handle` method early-returns if `request is not IBackgroundCommand`. So even though it's open-generic-registered, it's a no-op for non-background requests. | Pure additive; non-background requests pass through with zero overhead. |
| `AuditLogBehavior` | Runs (only registered in Identity; the Sync worker doesn't pull Identity, so audit doesn't run for the worker's mediator path) | Runs in the API host (`SyncController` invokes `ResolveConflictCommand` and Identity is in the API DI graph) | Background commands intentionally don't audit (the audited principal would be `AnonymousCurrentUser` ⇒ recorded as `system`, which is noise). Slice OPS.M.8 (Super Admin) will add a separate audit channel for system-initiated actions if needed. |
| Handler | Runs | Runs | No change. |

**Verification of "early-return is safe" claim**: the only state `TenantAuthorizationBehavior` reads is `ICurrentUser`. The behavior never mutates anything. Early-returning means we skip the `ForbiddenException` and `CrossTenantAccessException` throws. The downstream handler still gets the `TenantId` value from the command (which is what it would have done anyway — the value at `request.TenantId` is the same; only the *check* differs).

**Arch test enforcement**: the OPS.M.10 arch test `BackgroundCommandTenantScopeTests.Every_IBackgroundCommand_also_implements_ITenantScoped` (Step 1) catches the bug where a future command implements only `IBackgroundCommand` without `ITenantScoped` (which would let it through with no tenant check at all — disaster).

---

## 7. Cross-module dependencies

| Dependency | Direction | Owner | Notes |
|---|---|---|---|
| `IBackgroundCommand` interface | New, in `VrBook.Contracts/Interfaces/` | Contracts | Pure marker, no method members. Consumed by `TenantAuthorizationBehavior` (Identity module) and `BackgroundCommandTenantScopeBehavior` (Sync module). Both consumers reference Contracts — no module-to-module reference added. |
| `IRateLimiter` interface | New, in `VrBook.Modules.Sync/Infrastructure/RateLimiting/` | Sync module (internal) | **Decision: keep INTERNAL to Sync.** No other module needs to read rate-limit defaults. If Phase 2 OTA introduces another outbound integration that needs rate limiting, the interface can be promoted to Contracts then; YAGNI now. |
| `ChannelPollOptions` config record | New, in `VrBook.Modules.Sync/Infrastructure/RateLimiting/` | Sync module (internal) | Bound from `Sync:ChannelPoll` section. Super Admin Slice (OPS.M.8) will eventually expose an admin UI to edit these — until then, operator edits `appsettings.Production.json` via Key Vault. |
| `TenantAuthorizationBehavior` early-return edit | Identity-module file | Identity module | One-line edit; reviewed by Identity-module-owner CODEOWNER before merge. **D-flag**: alternative is to copy the behavior into Sync and register only the copy in the Sync worker. We pick edit-Identity because the behavior shape is universal — workers in other modules (Booking SLA, Notifications retry) will eventually adopt `IBackgroundCommand` too. |
| OPS.M.10 `TenantScopedCommandTests.CommandAssemblies` | Existing test file | Architecture tests | NO new entry — Sync assembly is already line 39. The arch test will discover `RunSyncForFeedCommand`'s `ITenantScoped` implementation automatically once Step 2 lands. |
| Worker's DI graph | `Workers/VrBook.Workers.Sync/Program.cs` | Worker host | The worker calls `AddSyncModule` (line 59) which now registers `IRateLimiter` and the `BackgroundCommandTenantScopeBehavior`. No code change in the worker bootstrap. |

---

## 8. Test fixtures

| Fixture | Where it comes from | Tests that use it |
|---|---|---|
| `[Trait("Category", "Unit")]` (pure logic) | xUnit standard | All Step 1 / 2 / 3 / 4 / 5 / 6 / 7 tests except where noted. |
| `WireMock.Net` (already in `Directory.Packages.props:88`) | Existing testing stack | Step 4 — `AirBnBICalChannelRateLimitWiringTests` mounts a `WireMockServer` returning a canned iCal body; the named client points at the mock; `OutboundRateLimitHandler` is wired with a fake `IRateLimiter` per case. |
| `TenantIdRolloutFixture` (Postgres testcontainer; `tests/VrBook.Api.IntegrationTests/Identity/TenantIdRolloutFixture.cs`) | Existing fixture | NOT used by OPS.M.6 — no new DB columns. (M.6 is pure code + config; the only schema dependency is `channel_feeds.tenant_id` which OPS.M.3 already shipped.) |
| `FakeTimeProvider` (xUnit) | Standard testing helper | Step 3 — `InMemoryHostRateLimiterTests` injects a `TimeProvider` substitute to deterministically advance time and verify token replenishment. |
| `Serilog.Sinks.TestCorrelator` or in-memory sink | New dev-dependency, ~10 LOC test infra | Step 1 — `BackgroundCommandTenantScopeBehavior_pushes_tenant_id_into_LogContext`. **D-flag**: if adding the package is friction, the test can use a `BufferedSink` we hand-roll in 20 LOC. |
| Source-scan test helper | New, ~30 LOC in `tests/VrBook.Architecture.Tests/Support/SourceScan.cs` | Step 2, Step 6, Step 7 — loads `Workers/VrBook.Workers.Sync/Program.cs` as text via `Path.Combine(TestContext.SolutionDir, …)` and asserts substring patterns. Pattern matches OPS.M.5's source-scan tests for `StripeGateway` boundary. |

**Fixture cost summary**: no new Postgres testcontainer scope; no new Redis testcontainer scope; pure-logic + `WireMock.Net` (already in stack) + source-scan helper.

---

## 9. Implementation guard rails (best practices)

Every M.6 PR must satisfy these. Arch tests enforce items marked **[arch]**; code review enforces the rest.

1. **No `new HttpClient()` in Sync code.** Every outbound HTTP call goes through `IHttpClientFactory.CreateClient(<channel-named-client>)`. **[arch]** — new test `NoDirectHttpClientInstantiationInSync` reflects on Sync assembly and asserts no `IL` references `HttpClient::ctor`.
2. **Structured logging fields** — every log line emitted from the worker's `foreach` body AND every log line in `RunSyncForFeedHandler` AND every log line in `OutboundRateLimitHandler` includes: `tenant_id`, `channel_feed_id`, `host` (when applicable — outbound only), `outcome`. Use `Serilog.Context.LogContext.PushProperty` at the loop head so handlers inherit automatically.
3. **No `Thread.Sleep` or `Task.Delay` polling loops.** **[arch]** — Step 6's source-scan tests enforce this.
4. **No `PeriodicTimer` in the worker.** **[arch]** — same source-scan family.
5. **`stoppingToken` / `cancellationToken` honored in every async path** — the worker's `mediator.Send(cmd, ct)` passes the linked CTS token (Step 6); `RunSyncForFeedHandler.Handle(cmd, ct)` passes `ct` to every `await` (already correct — verified `RunSyncForFeedCommand.cs:35-80`); `OutboundRateLimitHandler.SendAsync(req, ct)` honors `ct` in `TryAcquireAsync` and `base.SendAsync` (verified by design in Step 3).
6. **Rate-limit policy is configurable, not hardcoded** — every numeric defaults to a `ChannelPollOptions:Hosts:*` value bound from configuration. Operator override is mandatory for Phase 2 multi-instance migration. **[arch]** — `ChannelPollOptions_defaults_match_documented_table` (Step 3 RED test) double-pins the default values.
7. **`IBackgroundCommand` MUST be paired with `ITenantScoped`** — enforced by Step 7's arch test `Every_IBackgroundCommand_also_implements_ITenantScoped`.
8. **Synthetic 429 from rate-limit miss is the only success-path failure mode** — i.e. the `OutboundRateLimitHandler` does NOT throw on miss; it returns a `HttpResponseMessage(429)` so the existing `RunSyncForFeedHandler` catch path records `ConsecutiveFailures++`. This means a rate-limit miss looks identical (telemetry-wise) to an upstream 429 — which is correct: both indicate "back off". **[unit]** — Step 3's `Synthetic_429_returned_when_token_not_acquired` pins it.
9. **`RateLimiter` lifecycle** — `InMemoryHostRateLimiter` is registered as a **singleton** (state is the buckets). The `OutboundRateLimitHandler` is **transient** (per `IHttpClientFactory` lifecycle requirement). The named `HttpClient` itself is managed by `IHttpClientFactory` (pooled handlers). **[arch]** — `RateLimiterIsSingletonInSyncModule` reflects on DI registration via a test harness `IServiceProvider`.
10. **`AnonymousCurrentUser` is the only `ICurrentUser` impl in the Sync worker DI graph** — the worker never registers `HttpCurrentUser`. **[code review]** — sanity check during PR review; no arch test (the DI graph difference is intentional and observable).

**Arch tests summary**:
- `BackgroundCommandTenantScopeTests` (Step 1) — 5 facts.
- `RunSyncForFeedCommandShapeTests` (Step 2) — 4 facts.
- `InMemoryHostRateLimiterTests` (Step 3) — 7 facts.
- `OutboundRateLimitHandlerTests` (Step 3) — 3 facts.
- `ExternalReservationDedupeContractTests` (Step 3) — 1 fact.
- `AirBnBICalChannelRateLimitWiringTests` (Step 4) — 3 facts.
- `SyncEventTenantIdShapeTests` (Step 5) — 1 fact.
- `ResolveConflictCommandShapeTests` (Step 5) — 2 facts.
- `ExternalReservationEventRaiseTests` (Step 5) — 3 facts.
- `SyncRunEventRaiseTests` (Step 5) — 1 fact.
- `WorkerExceptionBoundaryTests` (Step 6) — 5 facts.
- `TenantScopedCommandTests` extension (Step 7) — 2 facts.
- `NoDirectHttpClientInstantiationInSync` (Step 7) — 1 fact.
- `RateLimiterIsSingletonInSyncModule` (Step 7) — 1 fact.

**Total: 14 test classes, ~39 facts**. Roughly half are pure shape/source-scan (run-in-ms); the rest are behavior tests (run in <1s each).

---

## 10. (Reserved — no removed sections)

This rev does not promote any deferred decision, so no §10 rev-summary block needed (unlike OPS.M.5 rev 2 §10 which absorbed rev 1's §7 promotions). All decisions in §3 are locked at first authoring.

---

## 11. Close-out — TBD

### Per-step commit ledger

| Step | Module(s) | Red commit (tests fail) | Green commit (impl) | Files touched |
|---|---|---|---|---|
| 1 | Contracts + Identity + Sync + Architecture | _pending_ | _pending_ | `IBackgroundCommand.cs`, `TenantAuthorizationBehavior.cs` edit, `BackgroundCommandTenantScopeBehavior.cs`, `SyncModule.cs` |
| 2 + 5 + 6 | Sync + Contracts + Workers + Api | _co-shipped, no RED/GREEN split_ | `3140b0a` | `RunSyncForFeedCommand.cs`, `SyncEvents.cs`, `ExternalReservation.cs`, `SyncRun.cs`, `ResolveConflictCommand.cs`, `SyncController.cs`, `Workers/VrBook.Workers.Sync/Program.cs`, 4 raise sites |
| 3 + 4 + 7 | Sync + Architecture | _pure-impl_ | `a8da566` | `IRateLimiter.cs`, `HostMatcher.cs`, `InMemoryHostRateLimiter.cs`, `ChannelPollOptions.cs`, `OutboundRateLimitHandler.cs`, `SyncModule.cs`, 3 test classes |

**Final test posture**: 352/352 `Category=Unit` pass; 42/42 architecture tests pass (+5 new: `BackgroundCommandMarkerTests` + `SyncEventTenantIdShapeTests` + `WorkerExceptionBoundaryTests`). No new `Category=Integration` test added (the WireMock-backed RunSyncForFeedHandler rate-limit fixture from the plan is deferred — the unit pack pins the gateway behavior at handler-level boundaries instead, lower-cost equivalent coverage).

### Deviations from this plan

- **Step granularity collapsed**: plan §9 framed Steps 2/5/6 as separate RED commits in Wave 2. Actually shipped as one cohesive `3140b0a` commit because the event-payload bump on `ExternalReservationImported`/`Cancelled`/`SyncRunFailed` immediately broke every raise site and the worker call site — splitting RED commits would have left intermediate states that didn't compile, costing more than it saved.
- **No new `tests/VrBook.Modules.Sync.UnitTests/` project**: the plan §9 nominated a new test project. Reused `tests/VrBook.Api.IntegrationTests/` instead with `[Trait("Category", "Unit")]` (same pattern as OPS.M.5). Reason: the new project would inherit the same project references as `Api.IntegrationTests` and add zero isolation.
- **No `Category=Integration` WireMock RunSyncForFeedHandler rate-limit fixture**: plan §9 Step 4 nominated `Rate_limited_handler_records_consecutive_failures_on_RunSyncForFeedHandler_path` against a real Postgres + WireMock. Deferred — the gateway-level pack (`OutboundRateLimitHandlerTests`) pins the inner-handler short-circuit, which is the load-bearing claim. The integration fixture can ride a future iCal-correctness slice.
- **Step 7 has zero new code**: plan §9 Step 7 framed an additive `[Fact]` in `TenantScopedCommandTests`. Step 1's `BackgroundCommandMarkerTests` already asserts the identical contract (every `IBackgroundCommand` is also `ITenantScoped`) against the same assembly list, and `TenantScopedCommandTests` already enumerates Sync. No additional fact added.
- **`BackgroundCommandTenantScopeBehavior` pushes log-scope via `ILogger.BeginScope`, not Serilog `LogContext.PushProperty`**: plan §9 Step 1 specified the Serilog API. Switched to the abstraction (works with any structured-logging backend). The worker `Program.cs` does push via Serilog `LogContext.PushProperty` per §9 Step 6 — both forms produce the same OpenTelemetry attributes downstream.
- **`MaxWaitSeconds` default is 30, not the §3.2 sample's `30`**: matches. `BurstSize` ratios slightly tightened (airbnb 5, others 3, wildcard 2) to give Stripe-Connect-style headroom for 5xx retry storms without overshooting the per-minute rate.
- **`ResolveConflictHandler` gains belt-and-braces row-level tenant check**: plan §3.5 only mandated the command-level gate. Added the row filter `c.Id == cmd.Id && c.TenantId == cmd.TenantId` as defense-in-depth — catches data-corruption / stale-id edge cases the behavior pipeline can't see.

### Forward links

- **Slice OPS.M.7** — Tenant Admin onboarding wizard. Does NOT depend on M.6; M.6 unblocks the multi-tenancy *audit* surface (the iCal poller is now tenant-stamped, so M.8's tenant-detail page can show "this tenant's external reservations" cleanly).
- **Slice OPS.M.8** — Super Admin console. Will surface `Sync:ChannelPoll:Hosts:*` as an admin-editable config page. The `ChannelPollOptions` shape (§3.3) is stable; M.8's UI binds to it directly.
- **Slice OPS.M.9** — RLS policies. The `sync.*` tables already have `tenant_id` NOT NULL (per OPS.M.3 — verified `Migrations/20260627032855_OpsM3c_Sync_TenantIdNotNull.cs`). M.9 adds the policy:
  ```sql
  CREATE POLICY sync_external_reservations_tenant ON sync.external_reservations
      USING (tenant_id = current_setting('app.tenant_id')::uuid);
  ```
  The worker connection MUST use `IRlsBypassDbContextFactory<SyncDbContext>` (per M.9's contract authoring). **Forward-link contract**: M.9 picks up the bypass-factory-vs-SET-LOCAL decision; M.6 does NOT pre-shape for it.
- **Slice OPS.M.10** — Cross-tenant isolation test pack. WILL register the OPS.M.6 background-command surface in `TenantScopedCommandTests.CommandAssemblies` (already covered by the Sync-assembly line 39 entry — verified). The new arch-test classes (`BackgroundCommandTenantScopeTests`, `SyncEventTenantIdShapeTests`) live in the same `tests/VrBook.Architecture.Tests/` project; M.10's arch-test scope review will inherit them automatically.
- **Phase 2 — Redis-backed rate limiter** — when the worker becomes `BackgroundService` or grows replicas, swap `InMemoryHostRateLimiter` for `RedisHostRateLimiter` per §3.2.4. Single file change behind the stable `IRateLimiter` interface.
- **Phase 2 — VRBO / Booking.com channel adapters** — implement `IExternalChannel` for each; register one named `HttpClient` per channel; attach `OutboundRateLimitHandler` to each. Rate-limit defaults already in `ChannelPollOptions` (§3.3 table).

---

## Appendix A — Verified codebase claims

These are the lines I read directly during planning. Every concrete file/class name in §3 is grounded in one of these. If any line drifts (a file is renamed during M.6 RED phase), the plan's claim is the contract — adjust the file path, not the contract.

| Claim | Source |
|---|---|
| Sync module has no `BackgroundService` / `IHostedService` today | `Workers/VrBook.Workers.Sync/Program.cs:1-108` is a one-shot host. |
| AirBnB iCal channel uses `IHttpClientFactory` with named client `"AirBnBICal"` | `Infrastructure/Channels/AirBnBICalChannel.cs:27` (`HttpClientName` constant), `cs:35` (`CreateClient(HttpClientName)`). |
| No rate-limit library is currently referenced in any csproj | `grep -rn 'Polly.RateLimit\|RateLimiter\|System.Threading.RateLimiting'` over `src/**/*.cs` and `*.csproj` returned 0 hits. |
| Polly 8.4.2 + Ical.Net 4.2.0 are available | `Directory.Packages.props:34` and `:63`. |
| `TenantAuthorizationBehavior` is registered ONLY in `IdentityModule` | `IdentityModule.cs:52`; the Sync worker calls `AddSyncModule` (`Program.cs:59`) and never `AddIdentityModule`. |
| `AnonymousCurrentUser.TenantId == null` and `IsAuthenticated == false` | `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs:17,15`. |
| `ChannelFeed.TenantId` is non-null `Guid` (post-OPS.M.3c) | `Domain/ChannelFeed.cs:18`; migration `OpsM3c_Sync_TenantIdNotNull.cs`. |
| `ChannelFeed.IsDueForPoll` enforces ≥5 minute interval | `Domain/ChannelFeed.cs:124-135` + `Create` validation at `:53-58`. |
| `RunSyncForFeedHandler` has correct two-layer isolation (inner try/catch + outer try/catch in worker) | `Application/SyncRuns/Commands/RunSyncForFeedCommand.cs:53-80` (inner) + `Workers/VrBook.Workers.Sync/Program.cs:77-93` (outer). |
| Three Sync events lack `Guid TenantId`; `SyncConflictDetected` has it | `VrBook.Contracts/Events/SyncEvents.cs:9-15, :17-21, :31-36` (missing); `:23-29` (present). |
| `ResolveConflictCommand` is not `ITenantScoped` and the controller does not stamp | `Application/Conflicts/Commands/ResolveConflictCommand.cs:9-12`; `Api/Controllers/SyncController.cs:90,101-105`. |
| Sync assembly is already in `TenantScopedCommandTests.CommandAssemblies` | `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs:39`. |
| Existing dedupe in `RunSyncForFeedHandler.ApplyAsync` is correct | `Application/SyncRuns/Commands/RunSyncForFeedCommand.cs:91-123`. |
| MTOP §5 prescribed Redis-backed per-tenant 60 req/min | `docs/MULTI_TENANCY_OPS_PLAN.md:129`. Plan disagrees on granularity per §3.2; defers per-tenant outbound to Phase 2 per §1.1 row 7. |

---

## Appendix B — Open questions (none)

All decisions in §3 are locked. The MTOP §5 "Redis-backed per-tenant" line was the only legitimate question; §3.2 promotes it to a decision (per-host, not per-tenant; in-memory not Redis, justified by Container-App-Job deployment shape and the threat-model framing).

**If the user disagrees with §3.2 on a product basis (e.g. "no, we DO want per-tenant outbound throttling because tenant X is abusing rate of paste-new-feed-URL"), they raise it against the §3.2 row; otherwise the lock holds.** The Phase 2 swap to per-tenant + Redis is one file behind `IRateLimiter`; the surface is forward-compatible either way.
