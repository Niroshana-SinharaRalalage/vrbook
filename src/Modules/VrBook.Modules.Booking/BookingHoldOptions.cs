using Microsoft.Extensions.Options;

namespace VrBook.Modules.Booking;

/// <summary>
/// VRB-208 (gap G3) — the checkout-hold TTL, bound from configuration section
/// <c>Booking</c>. Was a hard-coded <c>TimeSpan.FromMinutes(15)</c> in
/// <c>CreateHoldHandler</c> while <c>Booking:HoldDurationMinutes</c> sat dead in
/// appsettings + Bicep. Now wired (per TL, over removal) so ops can tune the hold
/// window without a code change. Default reproduces the old 15-minute constant.
/// </summary>
public sealed class BookingHoldOptions
{
    public const string SectionName = "Booking";

    public int HoldDurationMinutes { get; set; } = 15;

    public TimeSpan HoldTtl => TimeSpan.FromMinutes(HoldDurationMinutes);
}

/// <summary>VRB-208 — a non-positive hold TTL would create already-expired holds;
/// fail fast at startup (VRB-200 pattern).</summary>
internal sealed class BookingHoldOptionsValidator : IValidateOptions<BookingHoldOptions>
{
    public ValidateOptionsResult Validate(string? name, BookingHoldOptions options)
    {
        if (options.HoldDurationMinutes < 1)
        {
            return ValidateOptionsResult.Fail(
                $"Booking:HoldDurationMinutes must be ≥ 1 (was {options.HoldDurationMinutes}).");
        }
        return ValidateOptionsResult.Success;
    }
}
