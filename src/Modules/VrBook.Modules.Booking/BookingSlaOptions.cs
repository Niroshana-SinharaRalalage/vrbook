using Microsoft.Extensions.Options;

namespace VrBook.Modules.Booking;

/// <summary>
/// VRB-207 (gap G2 / Q1) — the Tentative-booking hold window, bound from
/// configuration section <c>Booking</c>. Was a hard-coded <c>AddHours(24)</c> in
/// <c>Booking.Place</c> while <c>Booking:TentativeSlaHours</c> sat unread and the
/// Bicep comment claimed "6h" (a three-way inconsistency). The owner-locked value
/// is <b>48h</b> (Q1, 2026-07-13); the default here reflects that. The expiry sweep
/// needs no separate copy — it expires on the <c>TentativeUntil</c> that
/// <c>Place</c> stamps from this value, so domain + worker share one source of truth.
/// </summary>
public sealed class BookingSlaOptions
{
    public const string SectionName = "Booking";

    public int TentativeSlaHours { get; set; } = 48;

    public TimeSpan TentativeSla => TimeSpan.FromHours(TentativeSlaHours);
}

/// <summary>VRB-207 — a non-positive SLA would stamp an already-expired hold; fail
/// fast at startup (VRB-200 pattern).</summary>
internal sealed class BookingSlaOptionsValidator : IValidateOptions<BookingSlaOptions>
{
    public ValidateOptionsResult Validate(string? name, BookingSlaOptions options)
    {
        if (options.TentativeSlaHours < 1)
        {
            return ValidateOptionsResult.Fail(
                $"Booking:TentativeSlaHours must be ≥ 1 (was {options.TentativeSlaHours}).");
        }
        return ValidateOptionsResult.Success;
    }
}
