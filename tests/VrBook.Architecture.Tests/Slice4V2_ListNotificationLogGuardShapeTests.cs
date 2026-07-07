using System.Reflection;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice 4.V2.3 — locks the handler-level M.17-parity guard on
/// <c>ListNotificationLogHandler</c> + pins the deliberate absence of the
/// orphan <c>booking.cancelled.owner_notice</c> template.
///
/// <para>Pre-4.V2.3 the query filtered to caller's tenant when caller was not
/// PlatformAdmin, but did NOT explicitly require the caller to hold
/// <c>tenant_admin</c> role in that tenant. Post-M.15.3 the controller-level
/// role gate is gone, so any authenticated same-tenant caller (e.g., a guest
/// with a membership) could enumerate the log. This slice adds the
/// <c>HasTenantRole(tid, "tenant_admin")</c> check to match the
/// <c>RetryNotificationHandler</c> M.17 shape.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class Slice4V2_ListNotificationLogGuardShapeTests
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

    private static string HandlerSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Notifications/Application/Queries/ListNotificationLogQuery.cs"));

    [Fact]
    public void ListNotificationLogHandler_gates_on_HasTenantRole_tenant_admin()
    {
        var text = HandlerSource();
        text.Should().Contain("HasTenantRole(callerTenant, \"tenant_admin\")",
            because: "post-M.15.3 the controller-level role gate is gone; the handler MUST fence off non-tenant_admin same-tenant callers via HasTenantRole. Mirrors the RetryNotificationHandler M.17 pattern.");
    }

    [Fact]
    public void ListNotificationLogHandler_still_bypasses_for_PlatformAdmin()
    {
        var text = HandlerSource();
        text.Should().Contain("currentUser.IsPlatformAdmin",
            because: "PlatformAdmin retains cross-tenant enumeration authority — ADR-0014 shape.");
    }

    [Fact]
    public void ListNotificationLogHandler_throws_ForbiddenException_on_role_miss()
    {
        var text = HandlerSource();
        text.Should().Contain("ForbiddenException",
            because: "role-miss maps to 403 via the standard RFC 7807 pipeline.");
    }

    [Fact]
    public void Orphan_booking_cancelled_owner_notice_template_is_absent()
    {
        var asm = typeof(VrBook.Modules.Notifications.NotificationsModule).Assembly;
        var manifest = asm.GetManifestResourceNames();
        manifest.Should().NotContain(
            "VrBook.Modules.Notifications.Templates.booking.cancelled.owner_notice.mustache",
            because: "the orphan template + sample were deleted in 4.V2.3 per §7-Q4-A locked. If a future slice re-adds the template, add a case to MustacheTemplateRenderer.TemplateNameFor + a handler that queues it — dead resources accrue risk.");
        manifest.Should().NotContain(
            "VrBook.Modules.Notifications.Templates.Samples.booking.cancelled.owner_notice.json",
            because: "the sample fixture pair must be deleted too.");
    }
}
