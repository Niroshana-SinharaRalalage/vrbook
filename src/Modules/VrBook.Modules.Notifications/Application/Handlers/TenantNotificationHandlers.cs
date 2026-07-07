using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 4.V2 — tenant-lifecycle notification handlers. Subscribes to
/// <see cref="TenantMembershipCreated"/> and queues the
/// <see cref="NotificationKind.TenantWelcome"/> email when the row is the
/// tenant's first active <c>tenant_admin</c> membership (per §7-Q1-A of
/// <c>docs/SLICE_4_PLAN_V2.md</c>; race-free by construction).
///
/// <para>Kept as a separate handler class from
/// <see cref="OwnerNotificationHandlers"/> (booking-lifecycle) and
/// <see cref="BookingNotificationHandlers"/> (guest-side) per plan §6-A1:
/// different lookup surface (<see cref="ITenantSetupContextLookup"/> vs
/// property-based), different logging shape.</para>
///
/// <para>Payload matches
/// <c>Templates/Samples/tenant.welcome.json</c>: OwnerFirstName +
/// TenantSlug + TenantDisplayName + DashboardUrl. DashboardUrl is
/// composed from <c>App:WebBaseUrl</c> config (empty in prod → falls
/// through to the handler's fallback string; wizard is Phase-1-only).
/// </para>
/// </summary>
internal sealed class TenantNotificationHandlers(
    NotificationsDbContext db,
    ITenantSetupContextLookup tenants,
    IUserEmailLookup users,
    IConfiguration configuration,
    ILogger<TenantNotificationHandlers> logger) : INotificationHandler<TenantMembershipCreated>
{
    public async Task Handle(TenantMembershipCreated n, CancellationToken cancellationToken)
    {
        // §7-Q1-A: only the FIRST tenant_admin membership triggers welcome.
        // Non-admin memberships (tenant_member reserved shape) never trigger.
        // Role literal matches identity.tenant_memberships.role — the DB shape,
        // NOT the Entra App Role. See ADR-0014.
        if (!string.Equals(n.Role, "tenant_admin", StringComparison.Ordinal))
        {
            return;
        }

        var setup = await tenants.GetAsync(n.TenantId, cancellationToken);
        if (setup is null)
        {
            logger.LogWarning(
                "TenantSetupContext for tenant {TenantId} not found; TenantWelcome for user {UserId} dropped.",
                n.TenantId, n.UserId);
            return;
        }

        if (setup.TenantAdminMembershipCount != 1)
        {
            // Additional tenant_admin added post-founding (M.8 promote flow etc.).
            // Welcome fired on the FIRST membership; subsequent additions are
            // not tenant-lifecycle events.
            return;
        }

        var user = await users.GetAsync(n.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "User {UserId} not found; TenantWelcome for tenant {TenantId} dropped.",
                n.UserId, n.TenantId);
            return;
        }

        var payload = new Dictionary<string, object>
        {
            ["OwnerFirstName"] = FirstName(user.DisplayName),
            ["TenantSlug"] = setup.Slug,
            ["TenantDisplayName"] = setup.DisplayName,
            ["DashboardUrl"] = BuildDashboardUrl(),
        };

        var log = NotificationLog.Queue(
            kind: NotificationKind.TenantWelcome,
            recipientUserId: n.UserId,
            recipientEmail: user.Email,
            subject: $"Welcome to VrBook, {setup.DisplayName}",
            payloadJson: JsonSerializer.Serialize(payload),
            tenantId: n.TenantId);
        db.Logs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued TenantWelcome -> {Email} for tenant {TenantId} ({LogId}).",
            user.Email, n.TenantId, log.Id);
    }

    /// <summary>
    /// First-name extraction for the greeting. Full name display is what the
    /// user provisions with; take the first whitespace-delimited token.
    /// Fallback to the full display name if there's no whitespace.
    /// </summary>
    private static string FirstName(string displayName)
    {
        var trimmed = displayName.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    private string BuildDashboardUrl()
    {
        var baseUrl = configuration["App:WebBaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl)
            ? "/admin/onboarding"
            : $"{baseUrl.TrimEnd('/')}/admin/onboarding";
    }
}
