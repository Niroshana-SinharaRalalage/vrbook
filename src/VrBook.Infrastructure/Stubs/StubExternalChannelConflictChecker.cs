using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Stubs;

/// <summary>
/// A0 stub for Booking → Sync integration. Returns "no conflict" so Booking can be
/// built and demonstrated before the Sync module ships in A6.
/// </summary>
public sealed class StubExternalChannelConflictChecker : IExternalChannelConflictChecker
{
    public Task<bool> HasOverlapAsync(
        Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken ct = default)
        => Task.FromResult(false);
}
