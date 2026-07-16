using Microsoft.Extensions.Options;
using VrBook.Modules.Loyalty.Domain;

namespace VrBook.Modules.Loyalty;

/// <summary>
/// VRB-206 (gap G1) — bound from configuration section <c>Loyalty</c>. The tier
/// stay-count thresholds, previously hard-coded in <c>TierDefinition</c> while the
/// <c>Loyalty:*Threshold</c> config keys were dead. Defaults reproduce the old
/// constants (Bronze 1 / Silver 3 / Gold 6), so absent config is behaviour-preserving.
/// </summary>
public sealed class LoyaltyOptions
{
    public const string SectionName = "Loyalty";

    public int BronzeThreshold { get; set; } = 1;
    public int SilverThreshold { get; set; } = 3;
    public int GoldThreshold { get; set; } = 6;

    public LoyaltyThresholds ToThresholds() => new(BronzeThreshold, SilverThreshold, GoldThreshold);
}

/// <summary>VRB-206 — thresholds must be strictly increasing and start at ≥ 1, else
/// tier resolution is nonsensical. Fail-fast at startup (VRB-200 pattern).</summary>
internal sealed class LoyaltyOptionsValidator : IValidateOptions<LoyaltyOptions>
{
    public ValidateOptionsResult Validate(string? name, LoyaltyOptions options)
    {
        if (options.BronzeThreshold < 1
            || options.BronzeThreshold >= options.SilverThreshold
            || options.SilverThreshold >= options.GoldThreshold)
        {
            return ValidateOptionsResult.Fail(
                "Loyalty:{Bronze,Silver,Gold}Threshold must satisfy 1 ≤ Bronze < Silver < Gold " +
                $"(was {options.BronzeThreshold}/{options.SilverThreshold}/{options.GoldThreshold}).");
        }
        return ValidateOptionsResult.Success;
    }
}
