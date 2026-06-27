using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Tenant aggregate root. A tenant is a business account that owns properties on the
/// VrBook marketplace. Per `docs/MULTI_TENANCY_OPS_PLAN.md` §1, this is a separate
/// aggregate from <see cref="User"/> — a tenant survives owner-of-record changes and
/// is the unit Stripe Connect, platform fees, and per-tenant feature toggles attach to.
///
/// Lifecycle (per `docs/OPS_M_1_PLAN.md` §2.5): PendingOnboarding → Active → Suspended
/// → Closed. The `Deleted` state from MULTI_TENANCY_OPS_PLAN.md §1 is represented by
/// <see cref="AggregateRoot.DeletedAt"/> being non-null, not a status value.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public const string StatusPendingOnboarding = "PendingOnboarding";
    public const string StatusActive = "Active";
    public const string StatusSuspended = "Suspended";
    public const string StatusClosed = "Closed";

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        StatusPendingOnboarding, StatusActive, StatusSuspended, StatusClosed,
    };

    public string Slug { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string Status { get; private set; } = StatusPendingOnboarding;
    public string DefaultCurrency { get; private set; } = "USD";
    public string DefaultTimezone { get; private set; } = "UTC";
    public Email SupportEmail { get; private set; } = default!;
    public int PlatformFeeBps { get; private set; } = 1500;
    public string? StripeAccountId { get; private set; }
    public string? StripeAccountStatus { get; private set; }
    public string? SuspendedReason { get; private set; }

    // OPS.M.5 §3.8 (D8) — Stripe surfaces these two booleans on the connected
    // account. Tenant.UpdateStripeAccountReadiness (Step 2) auto-transitions
    // PendingOnboarding → Active when both are true, Active → Suspended on
    // capability loss. Default false: new tenants haven't onboarded Stripe yet.
    //
    // Setters wired by Step 2's UpdateStripeAccountReadiness method; S1144 is
    // suppressed here because Step 1 ships the column/property pair alone per
    // OPS_M_5_PLAN §9 TDD cadence (schema first, behavior next).
#pragma warning disable S1144 // S1144: Remove unused setters — wired by Step 2.
    public bool ChargesEnabled { get; private set; }
    public bool PayoutsEnabled { get; private set; }
#pragma warning restore S1144

    private Tenant() { }   // EF Core

    public static Tenant Create(
        string slug,
        string displayName,
        Email supportEmail,
        string defaultCurrency = "USD",
        string defaultTimezone = "UTC",
        int platformFeeBps = 1500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(supportEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTimezone);
        if (defaultCurrency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "ISO 4217 currency code must be exactly 3 characters.", nameof(defaultCurrency));
        }
        if (platformFeeBps is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformFeeBps), platformFeeBps,
                "Platform fee must be between 0 and 10000 basis points (0-100%).");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            SupportEmail = supportEmail,
            Status = StatusPendingOnboarding,
            DefaultCurrency = defaultCurrency.Trim().ToUpperInvariant(),
            DefaultTimezone = defaultTimezone.Trim(),
            PlatformFeeBps = platformFeeBps,
        };
        tenant.Raise(new TenantCreated(tenant.Id, tenant.Slug, tenant.DisplayName));
        return tenant;
    }

    public void Activate()
    {
        if (Status != StatusPendingOnboarding)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} cannot be activated from status '{Status}'. Only PendingOnboarding -> Active is allowed.");
        }
        Status = StatusActive;
        Raise(new TenantActivated(Id));
    }

    public void Suspend(string reason, Guid actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != StatusActive)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} cannot be suspended from status '{Status}'. Only Active -> Suspended is allowed.");
        }
        Status = StatusSuspended;
        SuspendedReason = reason.Trim();
        Raise(new TenantSuspended(Id, SuspendedReason, actorId));
    }

    public void Reactivate()
    {
        if (Status != StatusSuspended)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} cannot be reactivated from status '{Status}'. Only Suspended -> Active is allowed.");
        }
        Status = StatusActive;
        SuspendedReason = null;
        Raise(new TenantActivated(Id));
    }

    public void Close()
    {
        if (Status == StatusClosed)
        {
            return;
        }
        Status = StatusClosed;
        Raise(new TenantClosed(Id));
    }

    public void UpdateSupportEmail(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);
        SupportEmail = email;
    }

    public void SetStripeAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        StripeAccountId = accountId.Trim();
    }

    public void UpdateStripeAccountStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        StripeAccountStatus = status.Trim();
    }

    public void SetPlatformFeeBps(int bps)
    {
        if (bps is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bps), bps, "Platform fee must be between 0 and 10000 basis points (0-100%).");
        }
        PlatformFeeBps = bps;
    }

    internal static bool IsAllowedStatus(string status) => AllowedStatuses.Contains(status);
}
