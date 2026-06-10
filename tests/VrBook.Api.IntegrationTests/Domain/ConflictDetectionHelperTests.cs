using FluentAssertions;
using NSubstitute;
using VrBook.Contracts.Enums;
using VrBook.Modules.Sync.Application.Conflicts.Handlers;
using VrBook.Modules.Sync.Domain;
using VrBook.Modules.Sync.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A6 stage 5 conflict-detection idempotency helper. The full
/// handler integration (with SyncDbContext + cross-module booking lookup) is
/// exercised by the deferred Docker integration tests. These tests cover the
/// pure dedupe + create branching that handlers share.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConflictDetectionHelperTests
{
    private static (Guid propertyId, Guid bookingId, Guid externalId) NewIds() =>
        (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task RecordIfMissingAsync_skips_when_existing_pending_conflict_present()
    {
        var (propertyId, bookingId, externalId) = NewIds();
        // Build a non-resolved SyncConflict via Domain.Detect (only constructor path).
        var existing = SyncConflict.Detect(propertyId, bookingId, externalId, ChannelKind.AirBnb);
        existing.IsResolved.Should().BeFalse();

        var conflicts = Substitute.For<ISyncConflictRepository>();
        conflicts.FindByPairAsync(bookingId, externalId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await ConflictDetectionHelpers.RecordIfMissingAsync(
            db: null!, // not touched when existing pending is found - helper short-circuits
            conflicts: conflicts,
            propertyId, bookingId, externalId, ChannelKind.AirBnb,
            ct: CancellationToken.None);

        result.Should().BeFalse("existing pending conflict means no new row appended");
        await conflicts.Received(1).FindByPairAsync(bookingId, externalId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordIfMissingAsync_creates_new_when_existing_is_resolved()
    {
        var (propertyId, bookingId, externalId) = NewIds();
        var resolved = SyncConflict.Detect(propertyId, bookingId, externalId, ChannelKind.AirBnb);
        resolved.Resolve(SyncConflictResolution.OwnerKeptDirect, "test");
        resolved.IsResolved.Should().BeTrue();

        var conflicts = Substitute.For<ISyncConflictRepository>();
        conflicts.FindByPairAsync(bookingId, externalId, Arg.Any<CancellationToken>())
            .Returns(resolved);

        // db.SyncConflicts.Add is the next call after the dedupe check returns
        // !IsResolved=false. Without a real DbContext we can't fully assert here;
        // this test confirms the helper does NOT short-circuit on a resolved row.
        Func<Task> act = () => ConflictDetectionHelpers.RecordIfMissingAsync(
            db: null!,
            conflicts: conflicts,
            propertyId, bookingId, externalId, ChannelKind.AirBnb,
            ct: CancellationToken.None);

        // Helper hits the db parameter (null) when trying to .Add — assert it gets that far.
        await act.Should().ThrowAsync<NullReferenceException>(
            because: "resolved-existing should NOT short-circuit; control flows past dedupe into db.Add");
    }
}
