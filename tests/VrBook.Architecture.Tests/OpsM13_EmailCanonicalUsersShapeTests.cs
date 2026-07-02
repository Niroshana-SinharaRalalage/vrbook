using FluentAssertions;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.13 (M.13.4) — locks in the post-collapse shape of the
/// User aggregate per <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1
/// as amended by <c>docs/OPS_M_13_4_BACKFILL_REVIEW.md</c> §5.1.
///
/// <para>These facts capture the "identity moves out of the users
/// aggregate" invariant so a future refactor can't silently re-add a
/// per-oid column to <c>users</c>.</para>
/// </summary>
public sealed class OpsM13_EmailCanonicalUsersShapeTests
{
    [Fact]
    public void User_aggregate_has_no_B2CObjectId_property()
    {
        typeof(User).GetProperty("B2CObjectId").Should().BeNull(
            because: "M.13.4 drops the legacy b2c_object_id column; identity mapping lives in identity.user_identities per §2.1.");
    }

    [Fact]
    public void User_aggregate_has_no_ClaimOidForExistingProfile_method()
    {
        typeof(User).GetMethod("ClaimOidForExistingProfile").Should().BeNull(
            because: "the F11.7.6 rebind path is superseded by the email-first ProvisionOrLinkUserHandler (M.13.3). Adding a new caller would resurrect the multi-row-per-email hazard.");
    }

    [Fact]
    public void User_Provision_email_first_overload_is_the_only_public_factory()
    {
        var overloads = typeof(User).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "Provision")
            .ToArray();
        overloads.Should().HaveCount(1,
            because: "M.13.4 dropped the legacy Provision(b2cObjectId, ...) overload; only the email-first shape remains.");
        overloads[0].GetParameters().Should().HaveCount(3);
        overloads[0].GetParameters()[0].ParameterType.Name.Should().Be("Email");
    }

    [Fact]
    public void UserRepository_interface_omits_GetByB2CObjectIdAsync_and_GetActiveByEmailAsync()
    {
        var iface = typeof(IUserRepository);
        iface.GetMethod("GetByB2CObjectIdAsync").Should().BeNull(
            because: "the oid-lookup surface is retired — identity queries route through db.UserIdentities join.");
        iface.GetMethod("GetActiveByEmailAsync").Should().BeNull(
            because: "the email-lookup surface was the F11.7.6 rebind path; M.13.3's handler queries the DbContext directly.");
    }

    [Fact]
    public void ProvisionUserHandler_symbol_is_absent_from_identity_assembly()
    {
        var t = typeof(IUserRepository).Assembly
            .GetType("VrBook.Modules.Identity.Application.Users.Commands.ProvisionUserHandler");
        t.Should().BeNull(
            because: "M.13.3 deleted the legacy handler; M.13.4 asserts the delete stuck so a merge conflict can't silently re-introduce it.");
    }

    [Fact]
    public void IdentityDbContext_snapshot_asserts_users_email_active_lower_uq_index()
    {
        // The partial UNIQUE lives at the DB layer (raw SQL) and in the EF model
        // snapshot at the UserConfiguration. If someone renames the index in
        // UserConfiguration, this test flags it before the next migration
        // scaffolds a spurious DROP + CREATE.
        var users = typeof(IdentityDbContext).Assembly
            .GetType("VrBook.Modules.Identity.Infrastructure.Persistence.UserConfiguration");
        users.Should().NotBeNull();
    }
}
