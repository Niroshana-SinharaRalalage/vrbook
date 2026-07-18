using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Payment.Application;

/// <summary>
/// VRB-102 — resolves the guest refund from the booking's snapshotted cancellation
/// policy + the time remaining until check-in. Pure + deterministic; because it
/// reads the point-in-time <see cref="CancellationPolicySnapshot"/> it is immune to
/// later global-tier / per-property changes.
///
/// <para><b>Tiered</b>: full refund when ≥ FirstTierDays before check-in;
/// MiddleTierRefundPct% when in [SecondTierDays, FirstTierDays); nothing once inside
/// FinalCutoffHours or below the middle band. <b>Refundable upgrade</b>: full refund
/// only if the upgrade was purchased and the cancel is before check-in; otherwise the
/// booking is non-refundable.</para>
/// </summary>
public static class CancellationRefundCalculator
{
    public static decimal Resolve(CancellationPolicySnapshot snapshot, TimeSpan untilCheckIn, decimal capturedAmount)
        => snapshot.Model switch
        {
            // Non-refundable unless the upgrade was purchased AND the cancel is before check-in.
            CancellationModel.RefundableUpgrade =>
                snapshot.RefundableUpgradePurchased && untilCheckIn > TimeSpan.Zero
                    ? capturedAmount
                    : 0m,
            CancellationModel.Tiered => ResolveTiered(snapshot, untilCheckIn, capturedAmount),
            _ => 0m,
        };

    private static decimal ResolveTiered(CancellationPolicySnapshot s, TimeSpan untilCheckIn, decimal capturedAmount)
    {
        // Inside the final cutoff (or already checked in) → nothing.
        if (untilCheckIn.TotalHours < (s.FinalCutoffHours ?? 0))
        {
            return 0m;
        }
        var days = untilCheckIn.TotalDays;
        if (days >= (s.FirstTierDays ?? int.MaxValue))
        {
            return capturedAmount;
        }
        if (days >= (s.SecondTierDays ?? int.MaxValue))
        {
            var pct = s.MiddleTierRefundPct ?? 0;
            return decimal.Round(capturedAmount * pct / 100m, 2, MidpointRounding.AwayFromZero);
        }
        return 0m;
    }
}
