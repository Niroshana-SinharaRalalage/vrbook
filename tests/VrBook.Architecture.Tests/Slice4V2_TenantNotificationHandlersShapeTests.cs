using System.Reflection;
using FluentAssertions;
using MediatR;
using VrBook.Contracts.Events;
using VrBook.Modules.Notifications.Domain;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice 4.V2.1 — locks the shape of <c>TenantNotificationHandlers</c>. Ships
/// the tenant.welcome pipeline as documented in
/// <c>docs/SLICE_4_PLAN_V2.md</c> §4.V2.1.
///
/// <para>Uses source-file inspection (mirrors the M.15.4/M.17/M.19 pattern)
/// because the Notifications module isn't referenced by the integration test
/// project. Adding the reference just for this handler would be scope creep;
/// the source-level guards catch the load-bearing invariants (correct event
/// subscription, correct role check, correct once-per-tenant suppression,
/// correct payload shape) with zero additional wiring.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class Slice4V2_TenantNotificationHandlersShapeTests
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
            "src/Modules/VrBook.Modules.Notifications/Application/Handlers/TenantNotificationHandlers.cs"));

    [Fact]
    public void NotificationKind_TenantWelcome_is_defined_at_40()
    {
        ((int)NotificationKind.TenantWelcome).Should().Be(40,
            because: "reserved-40 documented in the plan §7-A3 as the lifecycle-of-user template block start.");
    }

    [Fact]
    public void TenantNotificationHandlers_subscribes_only_to_TenantMembershipCreated()
    {
        var asm = typeof(NotificationKind).Assembly;
        var handlerType = asm.GetType("VrBook.Modules.Notifications.Application.Handlers.TenantNotificationHandlers");
        handlerType.Should().NotBeNull(because: "the handler class must exist under the Handlers folder.");

        var subscribedEvents = handlerType!
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToArray();

        subscribedEvents.Should().ContainSingle()
            .Which.Should().Be<TenantMembershipCreated>(
                because: "§7-Q1-A locked: fire on TenantMembershipCreated, NOT TenantCreated (race-free).");
    }

    [Fact]
    public void TenantNotificationHandlers_guards_on_tenant_admin_role_literal()
    {
        var text = HandlerSource();
        text.Should().Contain("\"tenant_admin\"",
            because: "the DB role literal (identity.tenant_memberships.role) is what MembershipRoles + M.15/M.17 handler guards read. Any role gate must use this literal.");
    }

    [Fact]
    public void TenantNotificationHandlers_suppresses_when_more_than_one_tenant_admin_exists()
    {
        var text = HandlerSource();
        text.Should().Contain("TenantAdminMembershipCount != 1",
            because: "§7-Q1-A: only the FIRST tenant_admin membership triggers welcome. Additional promotions (M.8 flow etc.) are NOT tenant-lifecycle events.");
    }

    [Fact]
    public void TenantNotificationHandlers_reads_both_ITenantSetupContextLookup_and_IUserEmailLookup()
    {
        var text = HandlerSource();
        text.Should().Contain("ITenantSetupContextLookup",
            because: "the handler needs Slug + DisplayName + admin count from Identity module via the Contracts port.");
        text.Should().Contain("IUserEmailLookup",
            because: "the handler needs the founding admin's email + display name for the greeting.");
    }

    [Fact]
    public void TenantNotificationHandlers_queues_NotificationKind_TenantWelcome_with_tenant_id_set()
    {
        var text = HandlerSource();
        text.Should().Contain("NotificationKind.TenantWelcome",
            because: "the handler must emit the correct Kind so MustacheTemplateRenderer.TemplateNameFor routes to tenant.welcome.mustache.");
        text.Should().Contain("tenantId: n.TenantId",
            because: "notification_log.tenant_id must equal the created tenant's id so OPS.M.9 RLS reads work for downstream operator queries.");
    }
}
