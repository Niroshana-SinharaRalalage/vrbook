namespace VrBook.Contracts.Enums;

/// <summary>
/// VRB-215/216 — the launch cancellation models (Q24, owner-locked). Replaces the
/// legacy <see cref="CancellationPolicyCode"/> (<c>Flexible/Moderate/Strict</c>),
/// which VRB-102/215 retire. A property selects one; the resolved policy is
/// snapshotted onto the booking line at Place (immutability).
/// </summary>
public enum CancellationModel
{
    /// <summary>Refund follows the platform-global tiered schedule
    /// (<see cref="VrBook.Contracts.Interfaces.ICancellationTierProvider"/>): full
    /// refund outside the first tier, a partial band, then no refund inside the cutoff.</summary>
    Tiered = 0,

    /// <summary>Booking is non-refundable by default; the guest pays a host-set,
    /// platform-capped flat surcharge at Place to make it fully refundable.</summary>
    RefundableUpgrade = 1,
}
