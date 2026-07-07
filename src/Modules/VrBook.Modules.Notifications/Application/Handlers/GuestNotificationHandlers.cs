using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 4.V2 — guest-side user-lifecycle notification handler. Subscribes
/// only to <see cref="UserRegistered"/> and queues the
/// <see cref="NotificationKind.GuestWelcome"/> email. Ignores
/// <see cref="UserOidRebound"/> per §7-Q2-A locked (rebound is a metadata
/// refresh, not a signup).
///
/// <para>Founding tenant admins receive BOTH a `guest.welcome` (from this
/// handler) AND a `tenant.welcome` (from <see cref="TenantNotificationHandlers"/>
/// on the subsequent `TenantMembershipCreated`). This is accepted per
/// §5-Q2 rationale: the two events model DIFFERENT lifecycle stages
/// ("you signed in" + "you set up a workspace") and the deliverability
/// cost of a rare double is lower than the correctness cost of a
/// membership race-check. See <c>docs/SLICE_4_PLAN_V2.md</c> §4-#1.</para>
///
/// <para>Kept as a separate handler class from
/// <see cref="TenantNotificationHandlers"/> +
/// <see cref="OwnerNotificationHandlers"/> +
/// <see cref="BookingNotificationHandlers"/> per plan §6-A1.</para>
/// </summary>
internal sealed class GuestNotificationHandlers(
    NotificationsDbContext db,
    IConfiguration configuration,
    ILogger<GuestNotificationHandlers> logger) : INotificationHandler<UserRegistered>
{
    public async Task Handle(UserRegistered n, CancellationToken cancellationToken)
    {
        // Guest welcome is tenant-less — the user has no tenant at signup
        // time per MTOP §1. RLS on notification_log accepts tenant_id=null
        // rows; the ListNotificationLog query filters them at app layer
        // (§7-Q3-A locked: keep the app-layer filter; defer RLS tighten
        // to a dedicated OPS.M.10.1 slice).
        var payload = new Dictionary<string, object>
        {
            ["FirstName"] = FirstName(n.DisplayName),
            ["HasReturnTo"] = false,
            ["ReturnTo"] = string.Empty,
            ["PropertiesUrl"] = BuildPropertiesUrl(),
        };

        var log = NotificationLog.Queue(
            kind: NotificationKind.GuestWelcome,
            recipientUserId: n.UserId,
            recipientEmail: n.Email,
            subject: "Welcome to VrBook",
            payloadJson: JsonSerializer.Serialize(payload),
            tenantId: null);
        db.Logs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued GuestWelcome -> {Email} for user {UserId} ({LogId}).",
            n.Email, n.UserId, log.Id);
    }

    private static string FirstName(string displayName)
    {
        var trimmed = displayName.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    private string BuildPropertiesUrl()
    {
        var baseUrl = configuration["App:WebBaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl)
            ? "/properties"
            : $"{baseUrl.TrimEnd('/')}/properties";
    }
}
