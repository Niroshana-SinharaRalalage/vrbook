using VrBook.Contracts.Interfaces;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-102 Phase B — test double for <see cref="ICancellationPolicyResolver"/>.
/// Agent 2's real implementation registers via DI; until then (and in tests) this
/// returns a fixed snapshot so the Place-time stamping + refund-read loop can be
/// exercised end-to-end.
/// </summary>
internal sealed class FakeCancellationPolicyResolver(CancellationPolicySnapshot snapshot) : ICancellationPolicyResolver
{
    public Task<CancellationPolicySnapshot> ResolveAsync(Guid propertyId, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(snapshot);
}
