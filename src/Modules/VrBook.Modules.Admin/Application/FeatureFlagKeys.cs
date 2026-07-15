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

    /// <summary>Known flags + their built-in safe defaults, for the list endpoint.</summary>
    public static readonly IReadOnlyDictionary<string, bool> KnownDefaults = new Dictionary<string, bool>
    {
        [LoyaltyEnabled] = true,
        [BookingUseRedisHoldStore] = false,
    };

    /// <summary>The memory-cache key for a flag — shared by the resolver (read) and the
    /// admin set-command (invalidate) so a PUT clears the exact entry.</summary>
    public static string CacheKey(string key) => $"featureflag::{key}";
}
