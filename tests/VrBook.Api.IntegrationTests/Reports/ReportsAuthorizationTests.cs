using FluentAssertions;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Reports.Application.Common;
using Xunit;

namespace VrBook.Api.IntegrationTests.Reports;

/// <summary>
/// Slice 7 C1 — handler-level tests for ReportsAuthorization helper (§2.4 + §2.12).
/// Aggregation correctness is verified via the SLICE7_PLAN §7 staging recipe
/// (the report handlers hit BookingDbContext / SyncDbContext directly so a
/// substituted-DbContext test can't faithfully exercise the SQL — same trade-off
/// as Slice 6's PricingRuleEndpointsTests, which left persistence to staging).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReportsAuthorizationTests
{
    private static readonly Guid OwnerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PropertyOfA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PropertyOfB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static (ICurrentUser user, IPropertyOwnerLookup lookup) Setup(
        Guid? currentUserId = null,
        bool isAdmin = false)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(currentUserId ?? OwnerA);
        user.IsAuthenticated.Returns(true);
        user.IsOwner.Returns(true);
        user.IsAdmin.Returns(isAdmin);

        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(PropertyOfA, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(PropertyOfA, OwnerA, "A's Property"));
        lookup.GetAsync(PropertyOfB, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(PropertyOfB, OwnerB, "B's Property"));
        lookup.ListPropertyIdsOwnedByAsync(OwnerA, Arg.Any<CancellationToken>())
            .Returns(new[] { PropertyOfA });
        lookup.ListPropertyIdsOwnedByAsync(OwnerB, Arg.Any<CancellationToken>())
            .Returns(new[] { PropertyOfB });

        return (user, lookup);
    }

    [Fact]
    public async Task Anonymous_user_throws_Forbidden()
    {
        var (user, lookup) = Setup();
        user.UserId.Returns((Guid?)null);

        var act = () => ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, null, default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*Sign-in required*");
    }

    [Fact]
    public async Task Owner_probing_someone_elses_property_throws_Forbidden()
    {
        var (user, lookup) = Setup(currentUserId: OwnerA);

        var act = () => ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, PropertyOfB, default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*not the owner*");
    }

    [Fact]
    public async Task Owner_probing_own_property_returns_single_id_scope()
    {
        var (user, lookup) = Setup(currentUserId: OwnerA);

        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, PropertyOfA, default);

        scope.Should().NotBeNull().And.HaveCount(1).And.Contain(PropertyOfA);
    }

    [Fact]
    public async Task Owner_without_filter_gets_all_owned_property_ids()
    {
        var (user, lookup) = Setup(currentUserId: OwnerA);

        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, null, default);

        scope.Should().NotBeNull().And.HaveCount(1).And.Contain(PropertyOfA);
    }

    [Fact]
    public async Task Admin_without_filter_gets_null_scope_meaning_all()
    {
        var (user, lookup) = Setup(currentUserId: OwnerA, isAdmin: true);

        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, null, default);

        scope.Should().BeNull(); // null = no filter; controller-side this means "all properties"
    }

    [Fact]
    public async Task Admin_with_specific_property_id_gets_that_one_property_in_scope()
    {
        // Admin can probe ANY property regardless of ownership.
        var (user, lookup) = Setup(currentUserId: OwnerA, isAdmin: true);

        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, PropertyOfB, default);

        scope.Should().NotBeNull().And.HaveCount(1).And.Contain(PropertyOfB);
    }

    [Fact]
    public async Task Unknown_property_throws_NotFound_for_non_admin()
    {
        var (user, lookup) = Setup(currentUserId: OwnerA);
        var unknown = Guid.NewGuid();
        lookup.GetAsync(unknown, Arg.Any<CancellationToken>())
            .Returns((PropertyOwnerSnapshot?)null);

        var act = () => ReportsAuthorization.ResolvePropertyScopeAsync(user, lookup, unknown, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
