using System.Reflection;
using FluentAssertions;
using MediatR;
using VrBook.Contracts.Events;
using VrBook.Modules.Notifications.Domain;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice 4.V2.2 — locks the shape of <c>GuestNotificationHandlers</c>. Ships
/// the guest.welcome pipeline as documented in
/// <c>docs/SLICE_4_PLAN_V2.md</c> §4.V2.2.
/// </summary>
[Trait("Category", "Unit")]
public sealed class Slice4V2_GuestNotificationHandlersShapeTests
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
            "src/Modules/VrBook.Modules.Notifications/Application/Handlers/GuestNotificationHandlers.cs"));

    [Fact]
    public void NotificationKind_GuestWelcome_is_defined_at_41()
    {
        ((int)NotificationKind.GuestWelcome).Should().Be(41,
            because: "reserved-41 documented in the plan §7-A3 as the guest lifecycle-of-user template.");
    }

    [Fact]
    public void GuestNotificationHandlers_subscribes_only_to_UserRegistered()
    {
        var asm = typeof(NotificationKind).Assembly;
        var handlerType = asm.GetType("VrBook.Modules.Notifications.Application.Handlers.GuestNotificationHandlers");
        handlerType.Should().NotBeNull(because: "the handler class must exist under the Handlers folder.");

        var subscribedEvents = handlerType!
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToArray();

        subscribedEvents.Should().ContainSingle()
            .Which.Should().Be<UserRegistered>(
                because: "§7-Q2-A locked: fire on UserRegistered only. UserOidRebound is a metadata refresh, not a signup — must not trigger a second welcome.");
    }

    [Fact]
    public void GuestNotificationHandlers_does_NOT_subscribe_to_UserOidRebound()
    {
        var asm = typeof(NotificationKind).Assembly;
        var handlerType = asm.GetType("VrBook.Modules.Notifications.Application.Handlers.GuestNotificationHandlers");
        handlerType.Should().NotBeNull();

        var subscribesToRebound = handlerType!
            .GetInterfaces()
            .Any(i => i.IsGenericType
                     && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)
                     && i.GetGenericArguments()[0] == typeof(UserOidRebound));
        subscribesToRebound.Should().BeFalse(
            because: "§7-Q2-A explicitly excludes UserOidRebound as a welcome trigger.");
    }

    [Fact]
    public void GuestNotificationHandlers_queues_with_null_tenant_id()
    {
        var text = HandlerSource();
        text.Should().Contain("tenantId: null",
            because: "guests are tenant-less at signup per MTOP §1. Any non-null tenant_id would fail OPS.M.9 RLS reads for downstream operator queries.");
    }

    [Fact]
    public void GuestNotificationHandlers_uses_NotificationKind_GuestWelcome()
    {
        var text = HandlerSource();
        text.Should().Contain("NotificationKind.GuestWelcome",
            because: "the handler must emit the correct Kind so MustacheTemplateRenderer.TemplateNameFor routes to guest.welcome.mustache.");
    }
}
