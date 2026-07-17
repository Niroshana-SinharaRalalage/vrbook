namespace VrBook.Modules.Admin.Domain;

/// <summary>
/// VRB-216 — the platform tax posture singleton (<c>admin.tax_posture</c>): the
/// marketplace-facilitator flag + a per-state enablement roster stored as JSON
/// (Q25). Platform-admin set; posture only — the tax engine is PAY VRB-103.
/// Platform-scoped: no RLS.
/// </summary>
public sealed class TaxPostureRow
{
    public static readonly Guid SingletonId = new("aaaaaaaa-0000-0000-0000-000000000001");

    public Guid Id { get; private set; }
    public bool FacilitatorActive { get; private set; }

    /// <summary>Per-state enablement as a JSON object, e.g. <c>{"CA":true,"NY":true}</c>.</summary>
    public string PerStateJson { get; private set; } = "{}";

    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TaxPostureRow() { } // EF

    public static TaxPostureRow Seed(bool facilitatorActive, string perStateJson, Guid updatedByUserId, DateTimeOffset updatedAt) =>
        new()
        {
            Id = SingletonId,
            FacilitatorActive = facilitatorActive,
            PerStateJson = perStateJson,
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

    public void Set(bool facilitatorActive, string perStateJson, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        FacilitatorActive = facilitatorActive;
        PerStateJson = perStateJson;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
    }
}
