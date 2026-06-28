using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Application.Tenants.Commands;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.5 Step 5 — pins the onboarding command handlers per
/// `docs/OPS_M_5_PLAN.md` §3.3 (D3) + §9 Step 5.
///
/// <para>Each command implements <see cref="ITenantScoped"/> so
/// <c>TenantAuthorizationBehavior</c> gates cross-tenant attempts; the
/// handlers themselves talk to the Stripe Connect gateway abstraction so
/// the actual SDK orchestration stays integration-only.</para>
/// </summary>
[Collection(nameof(TenantIdRolloutCollection))]
public sealed class StripeOnboardingCommandsTests
{
    private readonly TenantIdRolloutFixture _fixture;

    public StripeOnboardingCommandsTests(TenantIdRolloutFixture fixture) => _fixture = fixture;

    // The ITenantScoped marker is enforced for all four onboarding commands by
    // tests/VrBook.Architecture.Tests/TenantScopedCommandTests via reflection
    // over the loaded assemblies — no need to duplicate the assertion here.

    [Fact]
    public async Task OnboardTenantStripe_calls_gateway_with_TenantId_persists_StripeAccount_and_returns_AccountId()
    {
        await using var db = NewDb();
        var tenant = await SeedTenantAsync(db);

        var gateway = Substitute.For<IStripeConnectGateway>();
        gateway.CreateConnectAccountAsync(tenant.Id, Arg.Any<string>(), "US", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("acct_fake_new"));

        var handler = new OnboardTenantStripeHandler(db, gateway);
        var result = await handler.Handle(
            new OnboardTenantStripeCommand(tenant.Id, "US"), default);

        result.StripeAccountId.Should().Be("acct_fake_new");
        await gateway.Received(1).CreateConnectAccountAsync(
            tenant.Id, tenant.SupportEmail.Value, "US", Arg.Any<CancellationToken>());

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id);
        reloaded.StripeAccountId.Should().Be("acct_fake_new",
            "the handler must persist the new account id on the tenant.");
    }

    [Fact]
    public async Task OnboardTenantStripe_returns_existing_id_when_tenant_already_onboarded()
    {
        await using var db = NewDb();
        var tenant = await SeedTenantAsync(db);
        tenant.SetStripeAccount("acct_already_exists");
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IStripeConnectGateway>();
        var handler = new OnboardTenantStripeHandler(db, gateway);

        var result = await handler.Handle(
            new OnboardTenantStripeCommand(tenant.Id, "US"), default);

        result.StripeAccountId.Should().Be("acct_already_exists");
        await gateway.DidNotReceive().CreateConnectAccountAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateStripeAccountLink_returns_url_and_expiry_from_gateway()
    {
        await using var db = NewDb();
        var tenant = await SeedTenantAsync(db);
        tenant.SetStripeAccount("acct_seed");
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IStripeConnectGateway>();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        gateway.CreateAccountLinkAsync("acct_seed", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StripeAccountLink("https://stripe.example/onboarding", expiry)));

        var handler = new GenerateStripeAccountLinkHandler(db, gateway);
        var result = await handler.Handle(new GenerateStripeAccountLinkCommand(tenant.Id), default);

        result.Url.Should().Be("https://stripe.example/onboarding");
        result.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public async Task SetTenantPlatformFeeBps_persists_new_fee_bps_for_platform_admin()
    {
        await using var db = NewDb();
        var tenant = await SeedTenantAsync(db);

        // OPS.M.8 Step 5 — handler now requires platform-admin defense-in-depth.
        var currentUser = Substitute.For<VrBook.Contracts.Interfaces.ICurrentUser>();
        currentUser.IsPlatformAdmin.Returns(true);
        var handler = new SetTenantPlatformFeeBpsHandler(db, currentUser);
        await handler.Handle(new SetTenantPlatformFeeBpsCommand(tenant.Id, 2000), default);

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id);
        reloaded.PlatformFeeBps.Should().Be(2000);
    }

    [Fact]
    public async Task SetTenantPlatformFeeBps_throws_ForbiddenException_for_non_platform_admin()
    {
        await using var db = NewDb();
        var tenant = await SeedTenantAsync(db);

        var currentUser = Substitute.For<VrBook.Contracts.Interfaces.ICurrentUser>();
        currentUser.IsPlatformAdmin.Returns(false);
        var handler = new SetTenantPlatformFeeBpsHandler(db, currentUser);

        Func<Task> act = () => handler.Handle(
            new SetTenantPlatformFeeBpsCommand(tenant.Id, 2000), default);
        await act.Should().ThrowAsync<VrBook.Domain.Common.ForbiddenException>();
    }

    private IdentityDbContext NewDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(opts => opts.UseNpgsql(_fixture.ConnectionString));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IdentityDbContext>();
    }

    private static async Task<Tenant> SeedTenantAsync(IdentityDbContext db)
    {
        var slug = $"acme-{Guid.NewGuid():N}";
        var tenant = Tenant.Create(slug, $"Display-{slug}", new Email($"ops-{slug}@example.com"));
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

}
