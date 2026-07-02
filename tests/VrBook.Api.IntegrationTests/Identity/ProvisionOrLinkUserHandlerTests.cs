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
/// Slice OPS.M.13 (M.13.3 Phase B) — end-to-end integration tests for
/// the email-first provisioning algorithm per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.5.
///
/// <para>Exercises the new handler through the mediator pipeline with
/// a live Postgres testcontainer + real EF Core + real audit pipeline
/// behavior. Each test resets identity.users (CASCADE cleans the child
/// user_identities rows) so tests are order-independent.</para>
///
/// <para>Categorized <c>Integration</c> — runs in CI's non-blocking
/// Integration job. Local: <c>docker</c> must be running; skip
/// otherwise.</para>
///
/// <para>Race-path coverage (23505 on users_email_active_lower_uq or
/// user_identities_provider_extid_uq) is not exercised here because
/// reliably reproducing the race would need two overlapping transactions
/// on the same connection — brittle at best. The DB constraints ARE
/// exercised (partial-UNIQUE + composite-UNIQUE), so the race handling
/// paths would fire under real load. Failure mode is well-scoped
/// (change tracker clear + re-query the winner), so we accept the risk
/// in exchange for reliable CI.</para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(IdentityApiCollection))]
public sealed class ProvisionOrLinkUserHandlerTests(IdentityApiFixture fixture)
{
    private const string EntraProvider = "entra";
    private const string ExistingOid = "8f1c3a2b-9d4e-4567-8b90-1234567890ab";
    private const string NewOid = "abcdef12-3456-7890-abcd-1234567890ab";
    private const string SharedEmail = "shared@example.com";

    private static ProvisionOrLinkUserCommand Cmd(
        string oid,
        string email = SharedEmail,
        bool verified = true,
        string displayName = "Test User",
        string provider = EntraProvider) =>
        new(provider, oid, email, verified, displayName);

    private async Task ResetAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE identity.users, identity.audit_log, identity.tenant_memberships, identity.user_identities CASCADE;");
    }

    // ---- Branch 1: identity hit (returning sign-in) ----

    [Fact]
    public async Task Branch1_identity_hit_returns_existing_user_and_bumps_last_seen()
    {
        await ResetAsync();
        Guid seededUserId;
        DateTimeOffset priorLastSeen;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var user = User.Provision(new Email(SharedEmail), "Seeded User", emailVerified: true);
            var identity = UserIdentity.Create(user.Id, EntraProvider, ExistingOid, clock.UtcNow.AddMinutes(-30));
            db.Users.Add(user);
            db.UserIdentities.Add(identity);
            await db.SaveChangesAsync();
            seededUserId = user.Id;
            priorLastSeen = identity.LastSeenAt;
        }

        using var handlerScope = fixture.Services.CreateScope();
        var mediator = handlerScope.ServiceProvider.GetRequiredService<IMediator>();
        var returnedId = await mediator.Send(Cmd(ExistingOid));

        returnedId.Should().Be(seededUserId, "Branch 1 returns the linked users.Id when the identity mapping already exists.");

        using var verifyScope = fixture.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var refreshed = await vdb.UserIdentities.FirstAsync(i => i.UserId == seededUserId);
        refreshed.LastSeenAt.Should().BeAfter(priorLastSeen, "Branch 1 bumps LastSeenAt on every returning sign-in.");
    }

    // ---- Branch 2: identity miss + verified email hit (link path) ----

    [Fact]
    public async Task Branch2_identity_miss_email_hit_verified_links_new_identity()
    {
        await ResetAsync();
        Guid seededUserId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = User.Provision(new Email(SharedEmail), "Existing Profile", emailVerified: true);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            seededUserId = user.Id;
            // NOTE: no UserIdentity seeded for ExistingOid — simulates a
            // pre-M.13 user whose oid mapping lives in the legacy column,
            // not in user_identities. In real M.13.4 backfill this becomes
            // moot; in the transitional window this branch is critical.
        }

        using var handlerScope = fixture.Services.CreateScope();
        var mediator = handlerScope.ServiceProvider.GetRequiredService<IMediator>();
        var returnedId = await mediator.Send(Cmd(NewOid, verified: true));

        returnedId.Should().Be(seededUserId, "Branch 2 links the new identity to the existing user row rather than provisioning a new one.");

        using var verifyScope = fixture.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var linked = await vdb.UserIdentities.FirstOrDefaultAsync(
            i => i.UserId == seededUserId && i.Provider == EntraProvider && i.ExternalId == NewOid);
        linked.Should().NotBeNull("Branch 2 must persist a new user_identities row bound to the existing user.");
    }

    [Fact]
    public async Task Branch2_identity_miss_email_hit_unverified_throws_email_unverified_cannot_bind_profile()
    {
        await ResetAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = User.Provision(new Email(SharedEmail), "Existing Profile", emailVerified: true);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var handlerScope = fixture.Services.CreateScope();
        var mediator = handlerScope.ServiceProvider.GetRequiredService<IMediator>();
        var act = () => mediator.Send(Cmd(NewOid, verified: false));

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(ex => ex.Rule == "email_unverified_cannot_bind_profile",
                because: "verified-email guard blocks unverified identities from binding to existing profiles per §3.5 Branch 2.");
    }

    // ---- Branch 3: identity miss + email miss (fresh provision) ----

    [Fact]
    public async Task Branch3_identity_miss_email_miss_provisions_fresh_user_and_first_identity()
    {
        await ResetAsync();

        using var handlerScope = fixture.Services.CreateScope();
        var mediator = handlerScope.ServiceProvider.GetRequiredService<IMediator>();
        var newId = await mediator.Send(Cmd(NewOid, email: "fresh@example.com", verified: true));

        newId.Should().NotBeEmpty("Branch 3 provisions a fresh user row.");

        using var verifyScope = fixture.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await vdb.Users.FirstAsync(u => u.Id == newId);
        user.Email.Value.Should().Be("fresh@example.com");
        user.EmailVerified.Should().BeTrue();
        var identity = await vdb.UserIdentities.FirstAsync(
            i => i.UserId == newId && i.Provider == EntraProvider && i.ExternalId == NewOid);
        identity.Should().NotBeNull("Branch 3 must persist the first user_identities row in the same transaction.");
    }

    [Fact]
    public async Task Branch3_email_is_normalized_to_lowercase_on_fresh_provision()
    {
        await ResetAsync();

        using var handlerScope = fixture.Services.CreateScope();
        var mediator = handlerScope.ServiceProvider.GetRequiredService<IMediator>();
        var newId = await mediator.Send(Cmd(NewOid, email: "Mixed.Case@Example.COM", verified: true));

        using var verifyScope = fixture.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await vdb.Users.FirstAsync(u => u.Id == newId);
        user.Email.Value.Should().Be("mixed.case@example.com",
            because: "the handler lowercases the incoming email before construction so the partial-UNIQUE index buckets consistently.");
    }
}
