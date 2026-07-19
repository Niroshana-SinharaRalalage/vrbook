using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.12.4 — layer 1 admin-vs-social protection integration tests.
///
/// <para>Verifies <c>ProvisionOrLinkUserHandler</c> REFUSES to add a
/// social <c>user_identities</c> row on Branch 2 when the matched user
/// carries any admin authority (<c>is_platform_admin</c> OR any
/// active <c>tenant_memberships</c> row). Owner policy 2026-07-05:
/// admin users MUST NEVER have a social identity linked.</para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(IdentityApiCollection))]
public sealed class ProvisionOrLinkUserHandler_AdminSocialRefuseTests(IdentityApiFixture fixture)
{
    private const string EntraProvider = "entra";
    private const string SharedEmail = "admin@vrbook.test";
    private const string ExistingEntraOid = "8f1c3a2b-9d4e-4567-8b90-1234567890ab";
    private const string NewGoogleSub = "google-sub-987654";

    private async Task ResetAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE identity.users, identity.audit_log, identity.tenant_memberships, identity.user_identities CASCADE;");
    }

    private async Task<Guid> SeedEntraUserAsync(bool isPlatformAdmin, int tenantMembershipCount)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var user = User.Provision(new Email(SharedEmail), "Admin User", emailVerified: true);
        if (isPlatformAdmin)
        {
            user.GrantPlatformAdmin(actorId: user.Id);
        }
        var entraIdentity = UserIdentity.Create(user.Id, EntraProvider, ExistingEntraOid, clock.UtcNow.AddMinutes(-30));
        db.Users.Add(user);
        db.UserIdentities.Add(entraIdentity);

        for (var i = 0; i < tenantMembershipCount; i++)
        {
            // A membership FKs to a real tenant (FK_tenant_memberships_tenants_tenant_id,
            // OPS.M.1). Seed the parent tenant first — a random Guid violated the FK (23503).
            var slug = $"t-{Guid.NewGuid():N}"[..12];
            var tenant = Tenant.Create(slug, $"Acme {i}", new Email(SharedEmail));
            db.Tenants.Add(tenant);
            var m = TenantMembership.Create(
                userId: user.Id,
                tenantId: tenant.Id,
                role: "tenant_admin",
                isPrimary: i == 0);
            db.Set<TenantMembership>().Add(m);
        }
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Guest_google_signin_on_non_admin_user_LINKS_normally()
    {
        await ResetAsync();
        await SeedEntraUserAsync(isPlatformAdmin: false, tenantMembershipCount: 0);

        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "google",
            ExternalId: NewGoogleSub,
            Email: SharedEmail,
            EmailVerified: true,
            DisplayName: "Alice Google");

        var returnedId = await mediator.Send(cmd);
        returnedId.Should().NotBe(Guid.Empty);

        using var verifyScope = fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var identities = await db.UserIdentities
            .Where(i => i.UserId == returnedId)
            .Select(i => i.Provider)
            .ToListAsync();
        identities.Should().BeEquivalentTo("entra", "google");
    }

    [Fact]
    public async Task Google_signin_on_platform_admin_REFUSED()
    {
        await ResetAsync();
        await SeedEntraUserAsync(isPlatformAdmin: true, tenantMembershipCount: 0);

        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "google",
            ExternalId: NewGoogleSub,
            Email: SharedEmail,
            EmailVerified: true,
            DisplayName: "Alice Google");

        var act = async () => await mediator.Send(cmd);
        var ex = (await act.Should().ThrowAsync<BusinessRuleViolationException>()).Which;
        ex.Rule.Should().Be("admin_social_signin_refused");

        // No google identity row was created.
        using var verifyScope = fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var providers = await db.UserIdentities.Select(i => i.Provider).ToListAsync();
        providers.Should().OnlyContain(p => p == "entra");
    }

    [Fact]
    public async Task Google_signin_on_tenant_admin_REFUSED()
    {
        await ResetAsync();
        await SeedEntraUserAsync(isPlatformAdmin: false, tenantMembershipCount: 1);

        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "google",
            ExternalId: NewGoogleSub,
            Email: SharedEmail,
            EmailVerified: true,
            DisplayName: "Alice Google");

        var act = async () => await mediator.Send(cmd);
        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .Which.Rule.Should().Be("admin_social_signin_refused");
    }

    [Fact]
    public async Task Facebook_signin_on_platform_admin_REFUSED_same_rule()
    {
        await ResetAsync();
        await SeedEntraUserAsync(isPlatformAdmin: true, tenantMembershipCount: 0);

        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "facebook",
            ExternalId: "fb-sub-123",
            Email: SharedEmail,
            EmailVerified: true,
            DisplayName: "Bob Facebook");

        (await mediator.Awaiting(m => m.Send(cmd)).Should()
            .ThrowAsync<BusinessRuleViolationException>())
            .Which.Rule.Should().Be("admin_social_signin_refused");
    }

    [Fact]
    public async Task Fresh_google_signin_no_existing_email_LANDS_as_guest_via_Branch_3()
    {
        await ResetAsync();
        // No existing user seeded — Branch 3 provisions fresh + guest.

        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "google",
            ExternalId: NewGoogleSub,
            Email: "guest-only@example.com",
            EmailVerified: true,
            DisplayName: "Fresh Guest");

        var id = await mediator.Send(cmd);
        id.Should().NotBe(Guid.Empty);

        using var verify = fixture.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == id);
        user.IsPlatformAdmin.Should().BeFalse();
        var identities = await db.UserIdentities.Where(i => i.UserId == id).Select(i => i.Provider).ToListAsync();
        identities.Should().BeEquivalentTo("google");
    }

    [Fact]
    public async Task Entra_local_signin_on_admin_NOT_refused_regardless_of_authority()
    {
        // Entra-local sign-in on an admin is the NORMAL admin path.
        await ResetAsync();
        var userId = await SeedEntraUserAsync(isPlatformAdmin: true, tenantMembershipCount: 2);

        // Different entra oid → Branch 2 (email hit, verified) links the
        // second entra identity. Should succeed because provider is not
        // in SocialProviderKeys.
        using var scope = fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cmd = new ProvisionOrLinkUserCommand(
            Provider: "entra",
            ExternalId: "different-entra-oid",
            Email: SharedEmail,
            EmailVerified: true,
            DisplayName: "Admin Again");

        var returnedId = await mediator.Send(cmd);
        returnedId.Should().Be(userId);
    }
}
