using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Api.IntegrationTests;

[Collection(nameof(IdentityApiCollection))]
[Trait("Category", "Integration")]
public sealed class IdentityFlowTests(IdentityApiFixture fixture)
{
    [Fact]
    public async Task GET_me_anonymously_returns_401()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: false);

        var response = await client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_DevAuth_provisions_user_and_returns_200()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        var response = await client.GetAsync("/api/v1/me");

        response.IsSuccessStatusCode.Should().BeTrue(
            "DevAuth issues a synthetic principal and the provisioning middleware creates the user row. Got {0}",
            response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserDto>();
        dto.Should().NotBeNull();
        dto!.Email.Should().Be("owner@vrbook.test");
        dto.DisplayName.Should().Be("Test Owner");
        dto.IsOwner.Should().BeTrue();
        dto.IsAdmin.Should().BeTrue();
        dto.EmailVerified.Should().BeTrue();

        // User row should exist now.
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PUT_me_updates_profile()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        // Force provisioning by a GET first.
        await client.GetAsync("/api/v1/me");

        var update = new UpdateProfileRequest("Renamed Owner", "+1 (555) 010-1234");
        var put = await client.PutAsJsonAsync("/api/v1/me", update);

        put.IsSuccessStatusCode.Should().BeTrue("PUT /me should succeed for an authenticated owner. Got {0}", put.StatusCode);

        var get = await client.GetFromJsonAsync<UserDto>("/api/v1/me");
        get!.DisplayName.Should().Be("Renamed Owner");
        get.Phone.Should().Be("+1 (555) 010-1234");
    }

    [Fact]
    public async Task PUT_me_with_empty_display_name_returns_400()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);
        await client.GetAsync("/api/v1/me");

        var put = await client.PutAsJsonAsync("/api/v1/me", new UpdateProfileRequest(string.Empty, null));

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DELETE_me_soft_deletes()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);
        await client.GetAsync("/api/v1/me");

        var del = await client.DeleteAsync("/api/v1/me");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var soft = await db.Users.IgnoreQueryFilters().SingleAsync();
        soft.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GET_admin_users_anonymously_returns_401()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: false);

        var response = await client.GetAsync("/api/v1/admin/users?q=test");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_admin_users_with_DevAuth_owner_returns_200()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);
        await client.GetAsync("/api/v1/me"); // provision one user

        var response = await client.GetFromJsonAsync<OffsetPagedResult<UserDto>>("/api/v1/admin/users");
        response.Should().NotBeNull();
        response!.Total.Should().BeGreaterThanOrEqualTo(1);
        response.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuditLog_entry_recorded_for_UpdateProfile()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);
        await client.GetAsync("/api/v1/me");
        await client.PutAsJsonAsync("/api/v1/me",
            new UpdateProfileRequest("Audit Test", null));

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entries = await db.AuditLog
            .Where(a => a.Action.StartsWith("user.update-profile"))
            .ToListAsync();

        entries.Should().NotBeEmpty();
        entries[0].ActorRole.Should().Be("admin");
    }
}
