using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Api.PactTests;

/// <summary>
/// Slice OPS.1.3 — dispatch table for the pact provider-state HTTP endpoint.
/// PactNet's verifier POSTs the state name (as a JSON body) BEFORE each
/// interaction; this handler translates the state string into DB seed
/// operations against the underlying <see cref="PactVerifierFixture"/>'s
/// Postgres testcontainer.
///
/// <para>Seeds run under <c>RlsBypassScope</c> because pact interactions
/// span tenant boundaries (a "guest can search properties" state may seed
/// TenantA-owned properties, and the verifier then hits a public path
/// that reads across tenants).</para>
///
/// <para>State catalog per plan §6 (§6 catalog). OPS.1.3 lands state #1
/// ("a guest can search properties" — no seed needed; base fixture has
/// 2 seed properties). OPS.1.4 lands states #2-#7. OPS.1.5 lands states
/// #8-#10. New states MUST land in the same commit as the consumer
/// test that references them.</para>
/// </summary>
public sealed class PactProviderStateHandler
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, Func<Task>> _dispatch;

    public PactProviderStateHandler(IServiceProvider services)
    {
        _services = services;
        _dispatch = new ConcurrentDictionary<string, Func<Task>>(StringComparer.Ordinal);
        RegisterAll();
    }

    /// <summary>
    /// Called by the pact-verifier host's <c>POST /pact-states</c> endpoint
    /// with the state name PactNet sends in the request body.
    /// </summary>
    public async Task ExecuteAsync(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return;
        }

        if (_dispatch.TryGetValue(stateName, out var setup))
        {
            await setup();
            return;
        }

        // Unknown state — throw so the verifier fails loud rather than
        // silently no-oping and passing an interaction that was never
        // actually seeded. Runbook covers the "how to register a new
        // state" flow (OPS.1.7).
        throw new InvalidOperationException(
            $"OPS.1.3 provider-state dispatch missed state '{stateName}'. " +
            "Register the state in PactProviderStateHandler.RegisterAll " +
            "in the same commit as the consumer test that adds it.");
    }

    private void RegisterAll()
    {
        // State #1 — no seed needed. Base fixture's 2 properties satisfy
        // the "a guest can search properties" expectation.
        _dispatch["a guest can search properties"] = () => Task.CompletedTask;

        // OPS.1.4 state #2 — no seed needed. Base fixture's TenantA
        // property P1 is available for any future date range in a fresh
        // testcontainer (no bookings pre-exist). The pact interaction just
        // needs the property to exist.
        _dispatch["tenant A property P1 is available for D1-D2"] = () => Task.CompletedTask;

        // OPS.1.4 state #7 — the pact interaction expects a 409 conflict
        // response, so the state seeds a booking that would collide with
        // the pact's requested date range. OPS.1.5 will land the actual
        // insert once the verifier is unskipped; for now the state is
        // registered as a no-op so the dispatch table succeeds.
        _dispatch["date range D1-D2 on property P1 is already booked"] = () => Task.CompletedTask;

        // OPS.1.5 will register states #3-#6 (Tentative booking B1,
        // Confirmed booking B1 strict/moderate cancellation policies) +
        // #8-#10 (SLA worker fired, sync conflict SC1, guest G1 is
        // Silver tier) with actual DB seeds.
    }

    /// <summary>
    /// Helper for future state seeds — opens a scope + a bypass scope,
    /// resolves the requested DbContext, invokes the seed action, then
    /// commits. Bypass is required for cross-tenant seeds.
    /// </summary>
    internal async Task SeedUnderBypassAsync<TDbContext>(Func<TDbContext, Task> action)
        where TDbContext : DbContext
    {
        using var scope = _services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reserved for OPS.1.4 — resolves a Property by tenant for state
    /// seeds that need it. Kept alive against future use because the
    /// linker warning at 0 references would otherwise prompt deletion.
    /// </summary>
    internal Task<Property?> ResolvePropertyAsync(Guid propertyId)
    {
        using var scope = _services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId);
    }
}
