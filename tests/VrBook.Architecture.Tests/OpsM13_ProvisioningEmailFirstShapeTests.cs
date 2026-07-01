using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Identity.Application.Users.Commands;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.13 (M.13.3) — locks in the shape of the new
/// email-first provisioning command + handler per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.5.
///
/// <para>These are reflection-only shape assertions — they verify the
/// contract (fields on the command, types the handler depends on),
/// not the branching behavior. Branch coverage lives in the
/// integration tests <c>ProvisionOrLinkUserHandlerTests</c> alongside
/// this commit.</para>
/// </summary>
public sealed class OpsM13_ProvisioningEmailFirstShapeTests
{
    [Fact]
    public void ProvisionOrLinkUserCommand_carries_provider_externalId_email_emailVerified_displayName()
    {
        var t = typeof(ProvisionOrLinkUserCommand);
        t.GetProperty(nameof(ProvisionOrLinkUserCommand.Provider))?.PropertyType.Should().Be(typeof(string),
            because: "Provider distinguishes 'entra' from federated 'google'/'microsoft' post-M.12.");
        t.GetProperty(nameof(ProvisionOrLinkUserCommand.ExternalId))?.PropertyType.Should().Be(typeof(string),
            because: "ExternalId is the provider's user id (oid for Entra); pair (Provider, ExternalId) is UNIQUE.");
        t.GetProperty(nameof(ProvisionOrLinkUserCommand.Email))?.PropertyType.Should().Be(typeof(string),
            because: "Email is the identity-linking key on Branch 2 of the algorithm.");
        t.GetProperty(nameof(ProvisionOrLinkUserCommand.EmailVerified))?.PropertyType.Should().Be(typeof(bool),
            because: "EmailVerified guards Branch 2 — unverified emails throw email_unverified_cannot_bind_profile.");
        t.GetProperty(nameof(ProvisionOrLinkUserCommand.DisplayName))?.PropertyType.Should().Be(typeof(string),
            because: "DisplayName refreshes the profile on every sign-in via RefreshFromLogin.");
    }

    [Fact]
    public void ProvisionOrLinkUserCommand_is_auditable()
    {
        var iAuditable = typeof(ProvisionOrLinkUserCommand).Assembly
            .GetType("VrBook.Modules.Identity.Application.Behaviors.IAuditable");
        iAuditable.Should().NotBeNull();
        typeof(ProvisionOrLinkUserCommand).Should().BeAssignableTo(iAuditable!,
            because: "Every write command emits an audit_log row via the audit pipeline behavior.");
    }

    [Fact]
    public void ProvisionOrLinkUserHandler_type_exists_in_expected_namespace()
    {
        var t = typeof(ProvisionOrLinkUserCommand).Assembly
            .GetType("VrBook.Modules.Identity.Application.Users.Commands.ProvisionOrLinkUserHandler");
        t.Should().NotBeNull(
            because: "the handler for the new email-first algorithm is the M.13.3 deliverable; a rename or delete must trip this.");
        t!.IsAbstract.Should().BeFalse();
        t.IsSealed.Should().BeTrue(because: "Handlers are sealed to prevent inheritance-based mocking.");
    }

    [Fact]
    public void ProvisionOrLinkUserHandler_depends_on_IdentityDbContext_IUnitOfWork_IDateTimeProvider()
    {
        var t = typeof(ProvisionOrLinkUserCommand).Assembly
            .GetType("VrBook.Modules.Identity.Application.Users.Commands.ProvisionOrLinkUserHandler")!;
        var ctor = t.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        paramTypes.Should().Contain("IdentityDbContext",
            because: "the handler queries UserIdentities + Users directly (no repository intermediary).");
        paramTypes.Should().Contain("IUnitOfWork",
            because: "SaveChangesAsync goes through the same UoW as every other command.");
        paramTypes.Should().Contain("IDateTimeProvider",
            because: "FirstSeenAt + LastSeenAt come from IDateTimeProvider.UtcNow, not DateTimeOffset.UtcNow, for testability.");
    }

    [Fact]
    public void User_has_email_first_Provision_overload_without_b2c_object_id_argument()
    {
        var t = typeof(VrBook.Modules.Identity.Domain.User);
        var provisionOverloads = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Provision")
            .ToArray();
        provisionOverloads.Should().HaveCountGreaterThan(1,
            because: "M.13.3 adds an email-first overload alongside the (deprecated) oid-first overload.");
        provisionOverloads.Should().Contain(m =>
            m.GetParameters().Length == 3
            && m.GetParameters()[0].ParameterType.Name == "Email"
            && m.GetParameters()[1].ParameterType == typeof(string)
            && m.GetParameters()[2].ParameterType == typeof(bool),
            because: "Provision(Email, displayName, emailVerified) is the M.13 shape used by ProvisionOrLinkUserHandler Branch 3.");
    }
}
