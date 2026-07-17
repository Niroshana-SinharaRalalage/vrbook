namespace VrBook.Modules.Admin.Domain;

/// <summary>
/// VRB-216 — the platform-global cancellation tier schedule (<c>admin.cancellation_tiers</c>),
/// platform-admin editable. Single active row; <see cref="Version"/> increments on each
/// edit so a booking-line snapshot can record which version it was placed under
/// (provenance — the concrete values also live on the booking, which is the real
/// immutability guard). Platform-scoped: no <c>tenant_id</c>, no RLS. <see cref="Id"/>
/// is a fixed singleton key.
/// </summary>
public sealed class CancellationTiers
{
    /// <summary>Fixed singleton primary key — there is exactly one active tier row.</summary>
    public static readonly Guid SingletonId = new("cccccccc-0000-0000-0000-000000000001");

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public int FirstTierDays { get; private set; }
    public int SecondTierDays { get; private set; }
    public int MiddleTierRefundPct { get; private set; }
    public int FinalCutoffHours { get; private set; }
    public int UpgradePricePct { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CancellationTiers() { } // EF

    public static CancellationTiers Seed(
        int firstTierDays, int secondTierDays, int middleTierRefundPct, int finalCutoffHours,
        int upgradePricePct, Guid updatedByUserId, DateTimeOffset updatedAt) =>
        new()
        {
            Id = SingletonId,
            Version = 1,
            FirstTierDays = firstTierDays,
            SecondTierDays = secondTierDays,
            MiddleTierRefundPct = middleTierRefundPct,
            FinalCutoffHours = finalCutoffHours,
            UpgradePricePct = upgradePricePct,
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

    /// <summary>Applies a platform-admin edit, bumping <see cref="Version"/>.</summary>
    public void Update(
        int firstTierDays, int secondTierDays, int middleTierRefundPct, int finalCutoffHours,
        int upgradePricePct, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        Version++;
        FirstTierDays = firstTierDays;
        SecondTierDays = secondTierDays;
        MiddleTierRefundPct = middleTierRefundPct;
        FinalCutoffHours = finalCutoffHours;
        UpgradePricePct = upgradePricePct;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
    }
}
