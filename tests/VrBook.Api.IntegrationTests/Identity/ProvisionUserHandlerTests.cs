using FluentAssertions;
using NSubstitute;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.10.2 F11.7.6.1 — RED tests for the provisioning upsert
/// design described in <c>docs/OPS_M_10_2_F11_7_6_MULTI_ROW_USER_FIX.md</c>.
///
/// <para>Every test in this file EXCEPT the two baselines
/// (<see cref="Handler_by_oid_hit_is_unchanged"/> and
/// <see cref="Handler_by_email_miss_provisions_new_row"/>) is expected to
/// FAIL until F11.7.6.2 + F11.7.6.3 land. That's the intent — TDD RED
/// captures the target behavior before the code exists. F11.7.6.5's
/// architecture test (source-text scan) enforces that the handler
/// references the new email-lookup and rebind pathways.</para>
///
/// <para>The two baseline tests SHOULD pass today: they lock in the
/// existing oid-hit + fresh-provision paths so a future refactor
/// doesn't accidentally regress them.</para>
///
/// <para>Uses NSubstitute for repo + UoW (same pattern as
/// <c>TenantAuthorizationBehaviorTests</c>). No Postgres testcontainer
/// so these run under the default <c>Category!=Integration</c> filter.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProvisionUserHandlerTests
{
    private const string RealEntraOidA = "8f1c3a2b-9d4e-4567-8b90-1234567890ab";
    private const string RealEntraOidB = "12341234-1234-1234-1234-1234567890ab";
    private const string DevAuthOidGuest = "dev-guest-00000001";
    private const string DevAuthOidOwner = "dev-owner-00000000";

    private const string TargetEmail = "shared@example.com";

    private static ProvisionUserCommand NewCmd(string oid, string email = TargetEmail) =>
        new(oid, email, "Test User", EmailVerified: true, IsOwner: false, IsAdmin: false);

    private static (IUserRepository users, IUnitOfWork uow) NewMocks() =>
        (Substitute.For<IUserRepository>(), Substitute.For<IUnitOfWork>());

    private static ProvisionUserHandler NewHandler(IUserRepository users, IUnitOfWork uow) =>
        new(users, uow);

    private static User NewUser(string oid, string email = TargetEmail, bool isPlatformAdmin = false)
    {
        var u = User.Provision(oid, new Email(email), "Test User", emailVerified: true, isOwner: false, isAdmin: false);
        if (isPlatformAdmin)
        {
            u.GrantPlatformAdmin(actorId: u.Id);
        }
        return u;
    }

    // ---- Baselines (should PASS today) ----

    [Fact]
    public async Task Handler_by_oid_hit_is_unchanged()
    {
        var (users, uow) = NewMocks();
        var existing = NewUser(RealEntraOidA);
        users.GetByB2CObjectIdAsync(RealEntraOidA).Returns(existing);

        var sut = NewHandler(users, uow);
        var id = await sut.Handle(NewCmd(RealEntraOidA), default);

        id.Should().Be(existing.Id);
        await users.DidNotReceive().AddAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task Handler_by_email_miss_provisions_new_row()
    {
        var (users, uow) = NewMocks();
        users.GetByB2CObjectIdAsync(RealEntraOidA).Returns((User?)null);
        // GetActiveByEmailAsync is the F11.7.6.2 new surface; a fresh oid + no email match
        // must fall through to the provision path.
        users.GetActiveByEmailAsync(TargetEmail).Returns(Array.Empty<User>());

        var sut = NewHandler(users, uow);
        var id = await sut.Handle(NewCmd(RealEntraOidA), default);

        id.Should().NotBeEmpty();
        await users.Received(1).AddAsync(Arg.Any<User>());
    }

    // ---- RED targets (should FAIL until F11.7.6.2 + .3 land) ----

    [Fact]
    public async Task Handler_by_email_hit_oid_miss_rebinds_oid_when_row_is_dev_origin()
    {
        // DevAuth-origin row exists for TargetEmail (oid=dev-guest-00000001).
        // A fresh real-Entra oid arrives with the same email. Handler should
        // pick the existing row (only match) and rebind its oid to the real
        // Entra one, NOT provision a new row.
        var (users, uow) = NewMocks();
        var devRow = NewUser(DevAuthOidGuest);
        users.GetByB2CObjectIdAsync(RealEntraOidA).Returns((User?)null);
        users.GetActiveByEmailAsync(TargetEmail).Returns(new[] { devRow });

        var sut = NewHandler(users, uow);
        var id = await sut.Handle(NewCmd(RealEntraOidA), default);

        id.Should().Be(devRow.Id);
        devRow.B2CObjectId.Should().Be(RealEntraOidA);
        await users.DidNotReceive().AddAsync(Arg.Any<User>());
        // Domain event emitted (F11.7.6.2 introduces UserOidRebound).
        devRow.DequeueEvents().Should().ContainSingle(e => e is UserOidRebound);
    }

    [Fact]
    public async Task Handler_by_email_hit_oid_miss_throws_when_both_oids_are_real_entra()
    {
        // Existing row's oid is a real Entra GUID; incoming oid is a different
        // real Entra GUID. Guardrail: throw email_already_claimed. Do NOT
        // rebind (the two oids are two humans sharing an email).
        var (users, uow) = NewMocks();
        var existing = NewUser(RealEntraOidA);
        users.GetByB2CObjectIdAsync(RealEntraOidB).Returns((User?)null);
        users.GetActiveByEmailAsync(TargetEmail).Returns(new[] { existing });

        var sut = NewHandler(users, uow);
        var act = () => sut.Handle(NewCmd(RealEntraOidB), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(ex => ex.Rule == "email_already_claimed");
        existing.B2CObjectId.Should().Be(RealEntraOidA, "guardrail must not mutate the row");
        await users.DidNotReceive().AddAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task Handler_by_email_hit_oid_miss_when_multi_row_selects_platform_admin_survivor()
    {
        // Multi-row DB state: one PA row + one non-PA row for the same email.
        // Survivor policy (§3): PA > has-membership > oldest CreatedAt.
        // Incoming DevAuth oid should rebind the PA row (survivor), NOT the
        // non-PA row.
        var (users, uow) = NewMocks();
        var paRow = NewUser(RealEntraOidA, isPlatformAdmin: true);
        var plainRow = NewUser(DevAuthOidGuest, isPlatformAdmin: false);
        users.GetByB2CObjectIdAsync(DevAuthOidOwner).Returns((User?)null);
        users.GetActiveByEmailAsync(TargetEmail).Returns(new[] { plainRow, paRow });

        var sut = NewHandler(users, uow);
        var id = await sut.Handle(NewCmd(DevAuthOidOwner), default);

        id.Should().Be(paRow.Id);
        paRow.B2CObjectId.Should().Be(DevAuthOidOwner);
        plainRow.B2CObjectId.Should().Be(DevAuthOidGuest, "non-survivor must not be mutated by the handler");
    }

    // NOTE: "handler ignores soft-deleted rows" is verified at the REPO
    // level (via the integration test in F11.7.6.6 —
    // GetActiveByEmailAsync uses the global query filter which excludes
    // soft-deleted). At the mock-repo level here it's structurally
    // identical to Handler_by_email_miss_provisions_new_row (both mock
    // an empty repo response), so the unit test was redundant.
}
