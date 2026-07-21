using FluentAssertions;
using VrBook.Modules.Identity.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// VRB-103 triage (Family-2 #1) — pins that a re-login (<see cref="User.RefreshFromLogin"/>)
/// never overwrites a user's edited DisplayName. Provisioning used to re-sync the IdP name on
/// every request, clobbering a PUT /me edit on the next request (broke IdentityFlowTests.
/// PUT_me_updates_profile). DisplayName is user-owned via UpdateProfile; login only refreshes
/// email-verification + last-login.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserAggregateTests
{
    [Fact]
    public void RefreshFromLogin_does_not_overwrite_an_edited_display_name()
    {
        var user = User.Provision(new Email("guest@vrbook.test"), "Original IdP Name", emailVerified: false);
        user.UpdateProfile("Renamed By User", new PhoneNumber("+1 (555) 010-1234"));

        // A subsequent login refresh (what UserProvisioning does on every request) must
        // NOT clobber the user's own edit.
        user.RefreshFromLogin(emailVerified: true);

        user.DisplayName.Should().Be("Renamed By User");
        user.EmailVerified.Should().BeTrue("a login refresh still upgrades email verification");
    }
}
