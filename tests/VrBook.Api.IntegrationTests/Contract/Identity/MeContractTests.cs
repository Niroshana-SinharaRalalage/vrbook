using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Identity;

/// <summary>
/// VRB-300 — contract tests for <c>/api/v1/me</c> (IdentityController). The
/// cross-tenant matrix asserts <em>authorization</em> (who may reach the
/// endpoint); this class asserts the dimensions the status-set matrix does not:
/// happy-path response body, mutation + idempotency, and that the identity
/// returned is the caller's own (no leakage between personas).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class MeContractTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task GET_me_as_OwnerA_returns_own_profile()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.GetAsync("/api/v1/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<UserDto>();
        dto.Should().NotBeNull();
        dto!.Email.Should().Be("owner-a@vrbook.test");
        dto.DisplayName.Should().Be("Owner A");
        dto.IsPlatformAdmin.Should().BeFalse("OwnerA is a tenant admin, not a platform admin.");
    }

    [Fact]
    public async Task GET_me_returns_the_callers_own_identity_not_another_personas()
    {
        var ownerB = fixture.CreateClientAs("OwnerB");

        var dto = await ownerB.GetFromJsonAsync<UserDto>("/api/v1/me");

        dto.Should().NotBeNull();
        dto!.Email.Should().Be("owner-b@vrbook.test",
            "the /me projection must resolve from the caller's own token, never another persona's.");
    }

    [Fact]
    public async Task PUT_me_updates_profile_and_is_idempotent_on_repeat()
    {
        var client = fixture.CreateClientAs("OwnerA");
        // DisplayName kept equal to the seed value so this mutation does not
        // disturb sibling tests in the shared collection; Phone is asserted.
        var body = new { displayName = "Owner A", phone = "+15551230000" };

        var first = await client.PutAsJsonAsync("/api/v1/me", body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstDto = await first.Content.ReadFromJsonAsync<UserDto>();
        firstDto!.Phone.Should().Be("+15551230000");

        // Idempotency: the same request applied again yields the same result,
        // never a duplicate/second-apply error.
        var second = await client.PutAsJsonAsync("/api/v1/me", body);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondDto = await second.Content.ReadFromJsonAsync<UserDto>();
        secondDto!.Phone.Should().Be("+15551230000");
        secondDto.DisplayName.Should().Be(firstDto.DisplayName);
    }

    [Fact]
    public async Task GET_me_anonymous_is_rejected()
    {
        var client = fixture.CreateClientAs(persona: null);

        var resp = await client.GetAsync("/api/v1/me");

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "an authenticated endpoint must reject an anonymous caller before the handler runs.");
    }
}
