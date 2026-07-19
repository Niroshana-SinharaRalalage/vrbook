namespace VrBook.Modules.Admin.Application;

/// <summary>
/// VRB-203 — feature-flag naming convention + the registry of known flags.
///
/// <para><b>Convention:</b> <c>Features:&lt;Area&gt;.&lt;Capability&gt;</c> — the
/// <c>Features:</c> config section, then a dotted <c>Area.Capability</c> pair
/// (e.g. <c>Features:Booking.InstantBook</c>). Bind/override under that key in
/// config or the <c>admin.feature_flags</c> table.</para>
///
/// <para>Every flag surfaced through the admin toggle API is declared here with
/// its safe default, so the list endpoint can show config-backed flags that have
/// no override row yet.</para>
/// </summary>
public static class FeatureFlagKeys
{
    public const string Section = "Features";

    /// <summary>Global loyalty tracking on/off (migrated from the legacy
    /// <c>Loyalty:Enabled</c> — VRB-203). Default: on.</summary>
    public const string LoyaltyEnabled = "Features:Loyalty.Enabled";

    /// <summary>Select the Redis-backed hold store instead of Postgres (migrated
    /// from the legacy <c>Features:UseRedisHoldStore</c> — VRB-203). Startup-time
    /// selection, not a live toggle. Default: off.</summary>
    public const string BookingUseRedisHoldStore = "Features:Booking.UseRedisHoldStore";

    /// <summary>VRB-102 — route cancellation refunds through the snapshotted
    /// cancellation-policy engine (tiered / refundable-upgrade) instead of the flat
    /// <c>Refund:ServiceFeePercent</c>. Consumed by the refund path (Phase B) once
    /// the per-booking policy snapshot lands; falls back to the flat refund when off.
    /// Default: off (dev/staging override to on via config).</summary>
    public const string CancellationEngineV2 = "Features:Cancellation.EngineV2";

    /// <summary>VRB-103 — use the real Stripe-Tax <c>ITaxCalculator</c> (platform-facilitator
    /// tax + fail-closed) instead of the zero-tax stub. Startup-time DI selection (like the
    /// hold store), not a live toggle: the calculator is chosen at composition. Off ⇒ the stub
    /// (quotes show $0 = current behavior). Default: off prod until Stripe Tax is enabled on the
    /// platform account; on for dev/staging with test keys.</summary>
    public const string StripeTaxEnabled = "Features:StripeTax.Enabled";

    /// <summary>Known flags + their built-in safe defaults, for the list endpoint.</summary>
    public static readonly IReadOnlyDictionary<string, bool> KnownDefaults = new Dictionary<string, bool>
    {
        [LoyaltyEnabled] = true,
        [BookingUseRedisHoldStore] = false,
        [CancellationEngineV2] = false,
        [StripeTaxEnabled] = false,
    };

    /// <summary>The memory-cache key for a flag — shared by the resolver (read) and the
    /// admin set-command (invalidate) so a PUT clears the exact entry.</summary>
    public static string CacheKey(string key) => $"featureflag::{key}";
}
