using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.17 (M.15 follow-up B) — pins handler-level
/// <c>HasTenantRole(&lt;tenant&gt;, "tenant_admin")</c> guards on the four
/// tenant-scoped admin surfaces whose controller-level
/// <c>[Authorize(Roles="Owner,Admin"|"Admin")]</c> gates were dropped in
/// M.15.3.
///
/// <para>Rationale: without these guards a same-tenant authenticated
/// non-admin caller (guest with a membership, tenant_member if that role
/// ever ships) could reach the notification-retry, sync-conflict-resolve,
/// channel-feed-CRUD, and review-moderation handlers on their own tenant.
/// Documented as Medium/Medium intra-tenant exposure in the
/// <c>OPS_M_15_CLOSE_OUT.md</c> §3.</para>
///
/// <para>Each fact reads the handler source and asserts the guard is
/// present. A regressor that drops the guard fails this test.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM17_TenantAdminHandlerGuardsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "test must run from inside the repo.");
        return dir!.FullName;
    }

    private static string ReadHandler(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue(path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void RetryNotificationHandler_gates_on_tenant_admin_for_tenant_stamped_rows()
    {
        var text = ReadHandler(
            "src/Modules/VrBook.Modules.Notifications/Application/Commands/RetryNotificationCommand.cs");
        text.Should().Contain("HasTenantRole(rowTenant, \"tenant_admin\")",
            because: "post-M.15.3 the controller-level role gate is gone; the tenant-stamped branch of the handler must gate on tenant_admin in the row's tenant.");
        text.Should().Contain("ForbiddenException",
            because: "the role-miss must throw ForbiddenException for the standard RFC 7807 → 403 pipeline.");
        // The NULL-tenant PlatformAdmin gate must stay in place too.
        text.Should().Contain("IsPlatformAdmin",
            because: "NULL-tenant rows (guest-flow emails) remain PlatformAdmin-only.");
    }

    [Fact]
    public void ResolveConflictHandler_gates_on_tenant_admin()
    {
        var text = ReadHandler(
            "src/Modules/VrBook.Modules.Sync/Application/Conflicts/Commands/ResolveConflictCommand.cs");
        text.Should().Contain("HasTenantRole(cmd.TenantId, \"tenant_admin\")");
        text.Should().Contain("ForbiddenException");
    }

    [Fact]
    public void ChannelFeed_write_handlers_gate_on_tenant_admin_via_shared_helper()
    {
        var text = ReadHandler(
            "src/Modules/VrBook.Modules.Sync/Application/ChannelFeeds/Commands/ChannelFeedHandlers.cs");
        text.Should().Contain("ChannelFeedAuthorization.RequireTenantAdmin",
            because: "the shared helper is called at the top of Create/Update/Delete to gate on tenant_admin in the command's tenant.");
        text.Should().Contain("HasTenantRole(tenantId, \"tenant_admin\")",
            because: "the helper's implementation must key on tenant_admin — not a different role token.");
        text.Should().Contain("ForbiddenException",
            because: "role-miss maps to 403 via the standard RFC 7807 pipeline.");

        // Verify all three write handlers reference the helper.
        System.Text.RegularExpressions.Regex
            .Matches(text, @"ChannelFeedAuthorization\.RequireTenantAdmin\(")
            .Count.Should().BeGreaterOrEqualTo(3,
            because: "Create + Update + Delete all must invoke the guard. Two matches would mean one handler is unguarded.");
    }

    [Fact]
    public void Review_moderation_handlers_gate_on_tenant_admin_via_shared_helper()
    {
        var text = ReadHandler(
            "src/Modules/VrBook.Modules.Reviews/Application/Moderation/Commands/ModerationHandlers.cs");
        text.Should().Contain("ReviewModerationAuthorization.RequireTenantAdmin",
            because: "the shared helper is called at the top of Hide/Restore/Reject to gate on tenant_admin.");
        text.Should().Contain("HasTenantRole(tenantId, \"tenant_admin\")",
            because: "the helper implementation must key on tenant_admin.");
        text.Should().Contain("ForbiddenException");

        // Three moderation handlers should each reference the helper.
        System.Text.RegularExpressions.Regex
            .Matches(text, @"ReviewModerationAuthorization\.RequireTenantAdmin\(")
            .Count.Should().BeGreaterOrEqualTo(3,
            because: "Hide + Restore + Reject all must invoke the guard.");
    }

    [Fact]
    public void RespondToReview_gates_on_property_ownership_not_tenant_admin()
    {
        // The owner-response endpoint is different from the mod actions —
        // it's for the property OWNER to publish a response, not for
        // platform/tenant moderation. Tenant_admin bypass would be wrong
        // semantics (an admin who is NOT the property owner shouldn't be
        // able to speak in the owner's voice). Property-ownership check is
        // the correct guard, added in the M.17 follow-up
        // (post-OPS.M.18). PlatformAdmin retains the cross-tenant bypass.
        var text = ReadHandler(
            "src/Modules/VrBook.Modules.Reviews/Application/Moderation/Commands/ModerationHandlers.cs");
        var respondToReviewSection = System.Text.RegularExpressions.Regex.Match(
            text,
            @"class\s+RespondToReviewHandler[\s\S]*?SaveChangesAsync\(cancellationToken\);\s*\}");
        respondToReviewSection.Success.Should().BeTrue(
            because: "RespondToReviewHandler must exist in this file.");

        // Positive: property-ownership check present via IPropertyOwnerLookup.
        respondToReviewSection.Value.Should().Contain("properties.GetAsync(review.PropertyId",
            because: "the handler must look up the property owner to verify ownership. A regressor that drops this lookup opens an intra-tenant hole where any tenant member can respond in the owner's voice.");
        respondToReviewSection.Value.Should().Contain("owner.OwnerUserId != currentUser.UserId",
            because: "the ownership check compares the property's OwnerUserId to the caller's UserId.");
        respondToReviewSection.Value.Should().Contain("IsPlatformAdmin",
            because: "PlatformAdmin retains the cross-tenant bypass — same as elsewhere.");
        respondToReviewSection.Value.Should().Contain("ForbiddenException",
            because: "the ownership-miss must throw ForbiddenException for the RFC 7807 → 403 pipeline.");

        // Negative: tenant_admin bypass has wrong shape here — must NOT
        // sneak into the handler even in a "close the pattern out" PR.
        respondToReviewSection.Value.Should().NotContain("ReviewModerationAuthorization.RequireTenantAdmin",
            because: "RespondToReview is owner-scoped; tenant_admin bypass would let a tenant admin who is NOT the property owner post responses in the owner's voice — wrong shape.");
        respondToReviewSection.Value.Should().NotContain("HasTenantRole",
            because: "HasTenantRole gates on tenant_admin membership; RespondToReview must gate on property ownership instead.");
    }
}
