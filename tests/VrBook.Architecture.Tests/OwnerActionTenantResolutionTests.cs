using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.7.5.2 — locks the BookingsController owner-action
/// shape: Confirm, Reject, CheckIn, CheckOut MUST resolve the tenant id
/// via <c>IGuestTenantResolver.ResolveFromBookingIdAsync</c> (path-
/// resolved). They MUST NOT consult any <c>CallerTenantId()</c> helper
/// (deleted in this slice; the F11.7 walk showed it was blocking
/// PlatformAdmin's cross-tenant operator surface before the M.4 gate's
/// PlatformAdmin bypass could fire).
///
/// <para>The Cancel endpoint is intentionally NOT covered here — it's the
/// guest-side path and does not take a tenant id (the
/// <see cref="VrBook.Modules.Booking.Application.Commands.CancelBookingHandler"/>
/// opens a BackgroundTenantScope from the resolver internally).</para>
///
/// <para>This test reads the controller as source text rather than via
/// reflection because the method bodies are what changed (the IL footprint
/// of the constructor is identical pre/post fix). A regressor that
/// reintroduces <c>CallerTenantId()</c> fails this test before merge.</para>
/// </summary>
public sealed class OwnerActionTenantResolutionTests
{
    private const string ControllerRelativePath =
        "src/VrBook.Api/Controllers/BookingsController.cs";

    private static string ReadControllerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ControllerRelativePath)))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read the controller source.");
        return File.ReadAllText(Path.Combine(dir!.FullName, ControllerRelativePath));
    }

    [Theory]
    [InlineData("Confirm")]
    [InlineData("Reject")]
    [InlineData("CheckIn")]
    [InlineData("CheckOut")]
    public void Owner_action_invokes_ResolveBookingTenantAsync(string methodName)
    {
        // Match the method signature line + a small lookahead window through
        // the body — captures whatever shape the body has (block or expression
        // body) up to the next `[HttpPost(` or class brace.
        var src = ReadControllerSource();
        var pattern = new Regex(
            $@"public\s+async\s+Task<ActionResult<BookingDto>>\s+{methodName}\s*\([^)]*\)\s*(?:=>|\{{)(?<body>(?:(?!\[HttpPost\(|\n\}}).)*)",
            RegexOptions.Singleline);
        var m = pattern.Match(src);
        m.Success.Should().BeTrue(
            because: $"BookingsController must declare {methodName} with the expected signature.");
        var body = m.Groups["body"].Value;
        body.Should().Match(
            b => b.Contains("ResolveBookingTenantAsync", StringComparison.Ordinal)
                || b.Contains("ResolveFromBookingIdAsync", StringComparison.Ordinal),
            because: $"{methodName} must path-resolve the tenant from the booking id (via ResolveBookingTenantAsync OR direct IGuestTenantResolver call) so the M.4 gate's PlatformAdmin bypass is reachable.");
    }

    [Fact]
    public void CallerTenantId_helper_is_deleted()
    {
        var src = ReadControllerSource();
        src.Should().NotContain("CallerTenantId",
            because: "F11.7.5.2 deletes the helper; the path-resolved tenant is the canonical mechanism. A regressor reintroducing it fails this test before merge.");
    }

    [Fact]
    public void Owner_action_does_not_throw_the_old_membership_error()
    {
        var src = ReadControllerSource();
        src.Should().NotContain("Owner action requires a tenant membership",
            because: "the error string used to fire from CallerTenantId() before the PlatformAdmin bypass; deleting the helper deletes this string too.");
    }

    [Fact]
    public void BookingsController_injects_IGuestTenantResolver()
    {
        var src = ReadControllerSource();
        // Primary-constructor syntax. Match the entire BookingsController
        // declaration so we don't false-positive on the admin controller
        // below it.
        var ctor = new Regex(
            @"public\s+sealed\s+class\s+BookingsController\s*\((?<params>[^)]+)\)",
            RegexOptions.Singleline).Match(src);
        ctor.Success.Should().BeTrue(
            because: "BookingsController uses a primary constructor.");
        ctor.Groups["params"].Value.Should().Contain("IGuestTenantResolver",
            because: "the controller must inject IGuestTenantResolver so the owner endpoints can path-resolve booking tenants.");
    }

    [Fact]
    public void BookingsController_does_not_inject_ICurrentUser_now_that_helper_is_gone()
    {
        var src = ReadControllerSource();
        var ctor = new Regex(
            @"public\s+sealed\s+class\s+BookingsController\s*\((?<params>[^)]+)\)",
            RegexOptions.Singleline).Match(src);
        ctor.Success.Should().BeTrue();
        ctor.Groups["params"].Value.Should().NotContain("ICurrentUser",
            because: "after F11.7.5.2 the controller no longer touches ICurrentUser directly; reintroducing it suggests a CallerTenantId-style helper is creeping back in.");
    }
}
