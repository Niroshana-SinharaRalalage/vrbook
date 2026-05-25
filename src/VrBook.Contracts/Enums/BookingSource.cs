namespace VrBook.Contracts.Enums;

/// <summary>
/// How the booking originated. <c>Direct</c> is the platform itself;
/// <c>AirBnb</c> means it was imported via iCal sync.
/// </summary>
public enum BookingSource
{
    Direct = 0,
    AirBnb = 1,
    Manual = 2,
}
