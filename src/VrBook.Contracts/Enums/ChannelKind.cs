namespace VrBook.Contracts.Enums;

/// <summary>
/// External booking channels. Phase 1 supports AirBnB only;
/// VRBO and Booking.com are Phase 2 placeholders (see proposal §22.2).
/// </summary>
public enum ChannelKind
{
    AirBnb = 0,
    Vrbo = 1,
    BookingCom = 2,
    Other = 99,
}
