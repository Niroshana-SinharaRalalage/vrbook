using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 4 polish: builds the dictionary the Mustache renderer reads, merging
/// the <see cref="BookingEmailSnapshot"/> (property title, total, line items)
/// with event-specific extras (Reason, RefundAmount, TentativeUntil, etc.).
/// </summary>
internal static class NotificationPayload
{
    public static Dictionary<string, object> Build(
        BookingEmailSnapshot? booking,
        Dictionary<string, object>? extras)
    {
        var dict = new Dictionary<string, object>();
        if (booking is not null)
        {
            dict["BookingId"] = booking.BookingId.ToString();
            dict["Reference"] = booking.Reference;
            dict["PropertyTitle"] = booking.PropertyTitle;
            dict["GuestDisplayName"] = booking.GuestDisplayName;
            dict["Checkin"] = booking.Checkin;
            dict["Checkout"] = booking.Checkout;
            dict["Nights"] = booking.Nights;
            dict["GuestCount"] = booking.GuestCount;
            dict["Currency"] = booking.Currency;
            dict["Subtotal"] = booking.Subtotal;
            dict["Fees"] = booking.Fees;
            dict["Taxes"] = booking.Taxes;
            dict["Total"] = booking.Total;
            dict["CancellationPolicy"] = booking.CancellationPolicy;
        }
        if (extras is not null)
        {
            foreach (var (k, v) in extras)
            {
                dict[k] = v;
            }
        }
        return dict;
    }
}
