namespace VrBook.Modules.Payment.Application;

/// <summary>
/// Bound from configuration section <c>Refund</c>. Platform-wide service-fee retention
/// applied to captured-booking refunds. Set to 0 (default) for full refunds.
/// Per-property fees are a future iteration (A5.1) - move to Property domain when needed.
/// </summary>
public sealed class RefundOptions
{
    public const string SectionName = "Refund";

    /// <summary>Percentage of the captured amount the platform retains. Range 0..100.</summary>
    public decimal ServiceFeePercent { get; set; }
}
